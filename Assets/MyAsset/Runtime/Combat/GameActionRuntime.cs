using System;
using System.Collections.Generic;
using Game.Core;
using R3;
using UnityEngine;

namespace Game.Runtime
{
    public sealed class GameActionRuntime : IDisposable
    {
        private enum Builtin { Move, Stop, Idle, Dodge, Jump, Guard, Flinch, GuardBreak, Knockback, WakeUp, Effects }
        private sealed class CombatExecution
        {
            public AttackMotionData attack;
            public MagicDefinition magic;
            public bool projectile;
        }

        private readonly SoACharaDataDic _data;
        private readonly GameEvents _events;
        private readonly ActionCatalog _catalog;
        private readonly Dictionary<(int owner, Builtin kind), int> _definitions = new();
        private readonly Dictionary<(int owner, Builtin kind), long> _held = new();
        private readonly Dictionary<int, ActionExecutorController> _controllers = new();
        private readonly Dictionary<long, CombatExecution> _combat = new();
        private readonly Dictionary<long, Vector2> _movementOutputs = new();
        private readonly HashSet<int> _owners = new();
        private readonly List<int> _ownerBuffer = new();
        private readonly List<ActionExecution> _active = new();
        private readonly IDisposable _registered;
        private readonly IDisposable _removed;
        private readonly IDisposable _damage;
        private readonly IDisposable _requested;
        private readonly IDisposable _death;

        public ActionExecutionSystem Execution { get; }

        public GameActionRuntime(SoACharaDataDic data, GameEvents events, ActionExecutionSystem execution)
        {
            _data = data;
            _events = events;
            Execution = execution;
            _catalog = new ActionCatalog(execution);
            Execution.Changed += HandleExecution;
            _requested = events.OnActionRequested.Subscribe(request => Execution.Queue(request));
            _death = events.OnCharacterDeath.Subscribe(death => StopActor(death.deadHash));
            _registered = events.OnCharacterRegistered.Subscribe(hash => _owners.Add(hash));
            _removed = events.OnCharacterRemoved.Subscribe(RemoveOwner);
            _damage = events.OnDamageDealt.Subscribe(hit =>
            {
                if (hit.result.actionInstanceId != 0 && hit.result.totalDamage > 0
                    && !GuardJudgmentLogic.IsGuardSucceeded(hit.result.guardResult))
                {
                    Execution.Signal(hit.result.actionInstanceId, ActionSignalKind.HitConfirmed);
                }
            });
        }

        public void Attach(int owner, ActionExecutorController controller) => _controllers[owner] = controller;
        public int RegisterDefinition(ActionDefinition definition) => _catalog.Register(definition);
        public int RegisterMotion(MotionDefinition motion) => _catalog.Register(motion);
        public void Detach(int owner, ActionExecutorController controller)
        {
            if (_controllers.TryGetValue(owner, out ActionExecutorController current) && current == controller)
            {
                _controllers.Remove(owner);
            }
        }

        public bool Execute(int owner, ActionDefinition definition, int targetHash, Vector2 direction, out long instanceId)
        {
            return Execute(owner, definition, targetHash, direction, 1f, ActionVariant.Default, out instanceId, out _);
        }

        public bool Execute(int owner, ActionDefinition definition, int targetHash, Vector2 direction,
            float chargeMultiplier, ActionVariant variant, out long instanceId, out long groupId, long replaceGroupId = 0)
        {
            instanceId = groupId = 0;
            if (!GameManager.IsCharacterValid(owner) || definition == null)
            {
                return false;
            }
            if (chargeMultiplier <= 0f || float.IsNaN(chargeMultiplier) || float.IsInfinity(chargeMultiplier)
                || !Enum.IsDefined(typeof(ActionVariant), variant)) { return false; }
            int definitionId = _catalog.Register(definition, variant);
            if (_catalog.TryGet(definitionId, out ActionCatalog.Entry captured) && captured.projectile && GameManager.Projectiles == null)
            {
                return false;
            }
            ActorRef ownerRef = default;
            ActorRef targetRef = default;
            GameManager.Actors?.TryGet(owner, out ownerRef);
            if (targetHash != 0 && (GameManager.Actors == null || !GameManager.Actors.TryGet(targetHash, out targetRef))) { return false; }
            if (targetHash != 0 && _data.GetFlags(targetHash).IsVanished) { return false; }
            return Execution.TryStart(new ActionRequest
            {
                ownerHash = owner, definitionId = definitionId, targetHash = targetHash,
                ownerRef = ownerRef, targetRef = targetRef,
                direction = direction, speedMultiplier = 1f, chargeMultiplier = chargeMultiplier, replaceGroupId = replaceGroupId,
            }, out instanceId, out groupId);
        }

        public bool IsGroupActive(long groupId) => Execution.IsGroupActive(groupId);
        public bool TryGetGroupPrimary(long groupId, out ActionExecution execution) => Execution.GetGroupPrimary(groupId, out execution);

        public bool TryGetAttackSettings(long instanceId, out AttackExecutionSettings settings)
        {
            settings = default;
            if (!Execution.TryGetInstance(instanceId, out ActionExecution current)
                || !_catalog.TryGet(current.definitionId, out ActionCatalog.Entry entry) || !entry.isAttack) { return false; }
            settings = entry.attackSettings;
            return true;
        }

        /// <summary>即時完了した技も、受理時に登録済みの設定を取得できる。</summary>
        public bool TryGetAttackSettings(ActionDefinition definition, ActionVariant variant, out AttackExecutionSettings settings)
        {
            settings = default;
            if (!_catalog.TryGetRegistered(definition, variant, out ActionCatalog.Entry entry) || !entry.isAttack) { return false; }
            settings = entry.attackSettings;
            return true;
        }

        /// <summary>接地を共通実行へ反映し、同一groupの着地硬直を占有の切れ目なしで開始する。</summary>
        public bool NotifyGrounded(int owner, long groupId, out long recoveryInstanceId)
        {
            recoveryInstanceId = 0;
            if (!Execution.GetGroupPrimary(groupId, out ActionExecution source) || source.ownerHash != owner
                || !_catalog.TryGet(source.definitionId, out ActionCatalog.Entry entry)
                || entry.variant != ActionVariant.Diving) { return false; }
            return Execution.TryCompleteAndContinue(source.instanceId, new ActionRequest
            {
                ownerHash = owner, ownerRef = source.ownerRef,
                definitionId = _catalog.RegisterLandingRecovery(source.definitionId), sourceInstanceId = source.instanceId,
                groupId = groupId, direction = source.direction, speedMultiplier = 1f,
                chargeMultiplier = source.chargeMultiplier,
            }, out recoveryInstanceId, out _);
        }

        public bool PlayMotion(int owner, MotionDefinition motion, out long instanceId)
        {
            ActorRef ownerRef = default;
            GameManager.Actors?.TryGet(owner, out ownerRef);
            return Execution.TryStart(new ActionRequest
            {
                ownerHash = owner, ownerRef = ownerRef, definitionId = _catalog.Register(motion), speedMultiplier = 1f,
            }, out instanceId);
        }

        public void SetLocomotion(int owner, Vector2 direction, float speed)
        {
            TrySetLocomotion(owner, direction, speed, out _);
        }

        public bool TrySetLocomotion(int owner, Vector2 direction, float speed, out long instanceId, float duration = 0f,
            ActorRef targetRef = default, long replaceGroupId = 0)
        {
            instanceId = 0;
            if (!GameManager.IsCharacterValid(owner)) { return false; }
            if (direction.sqrMagnitude <= 0.0001f || speed <= 0f)
            {
                bool stopped = StartBuiltin(owner, Builtin.Stop, Vector2.zero, replaceGroupId: replaceGroupId);
                if (stopped) { _held.Remove((owner, Builtin.Move)); }
                return stopped;
            }
            if (_held.TryGetValue((owner, Builtin.Move), out long active)
                && duration <= 0f && Execution.TryGetInstance(active, out ActionExecution existing)
                && existing.definition.endMode == ActionEndMode.Held && existing.targetRef == targetRef
                && (replaceGroupId == 0 || existing.groupId == replaceGroupId)
                && Execution.UpdateInput(active, direction.normalized, speed)) { instanceId = active; return true; }
            bool accepted = StartBuiltin(owner, Builtin.Move, direction.normalized, speed,
                duration: duration, targetRef: targetRef, replaceGroupId: replaceGroupId);
            if (accepted) { _held.TryGetValue((owner, Builtin.Move), out instanceId); }
            return accepted;
        }

        public bool TryDodge(int owner, Vector2 direction) => StartBuiltin(owner, Builtin.Dodge, direction.normalized);
        public bool TryDodge(int owner, Vector2 direction, out long instanceId, long replaceGroupId = 0)
        {
            bool accepted = StartBuiltin(owner, Builtin.Dodge, direction.normalized, replaceGroupId: replaceGroupId);
            instanceId = 0;
            if (accepted) { _held.TryGetValue((owner, Builtin.Dodge), out instanceId); }
            return accepted;
        }
        public bool BeginJump(int owner) => StartBuiltin(owner, Builtin.Jump, Vector2.up);
        public void EndJump(int owner) => ReleaseHeld(owner, Builtin.Jump);
        public bool IsDodging(int owner) => IsHeld(owner, Builtin.Dodge);
        public bool TryIdle(int owner, out long instanceId, float duration = 0f, long replaceGroupId = 0)
        {
            bool accepted = StartBuiltin(owner, Builtin.Idle, default, duration: duration, replaceGroupId: replaceGroupId);
            instanceId = 0;
            if (accepted) { _held.TryGetValue((owner, Builtin.Idle), out instanceId); }
            return accepted;
        }

        public void SetGuarding(int owner, bool guarding)
        {
            TrySetGuarding(owner, guarding, out _);
        }

        /// <summary>入力が保持する移動・待機・ガードだけを終了する。受理済み攻撃groupは維持する。</summary>
        public void ReleaseHeldInputs(int owner)
        {
            ReleaseHeld(owner, Builtin.Move);
            ReleaseHeld(owner, Builtin.Idle);
            ReleaseHeld(owner, Builtin.Guard);
            SyncDefense(owner);
        }

        public bool TrySetGuarding(int owner, bool guarding, out long instanceId, float duration = 0f, long replaceGroupId = 0)
        {
            instanceId = 0;
            if (!GameManager.IsCharacterValid(owner)) { return false; }
            bool reuse = duration <= 0f && _held.TryGetValue((owner, Builtin.Guard), out long active)
                && Execution.TryGetInstance(active, out ActionExecution existing) && existing.definition.endMode == ActionEndMode.Held
                && (replaceGroupId == 0 || existing.groupId == replaceGroupId);
            if (guarding && !reuse && !StartBuiltin(owner, Builtin.Guard, default, duration: duration, replaceGroupId: replaceGroupId)) { return false; }
            if (!guarding) { ReleaseHeld(owner, Builtin.Guard); }
            if (guarding) { _held.TryGetValue((owner, Builtin.Guard), out instanceId); }
            SyncDefense(owner);
            return true;
        }

        public void SetActionEffects(int owner, ActionEffect[] effects)
        {
            ReleaseHeld(owner, Builtin.Effects);
            if (effects == null || effects.Length == 0) { SyncDefense(owner); return; }
            int id = Definition(owner, Builtin.Effects);
            ActionDefinitionData definition = BaseDefinition(owner, Builtin.Effects);
            definition.id = id;
            definition.effects = effects;
            definition.resources = ActionResources.None;
            definition.endMode = ActionEndMode.Manual;
            definition.actState = ActState.Neutral;
            Execution.UpdateDefinition(id, definition);
            StartBuiltin(owner, Builtin.Effects, default);
            SyncDefense(owner);
        }

        public bool IsPrimaryExecuting(int owner)
        {
            Execution.CopyActive(owner, _active);
            for (int i = 0; i < _active.Count; i++)
            {
                ActionExecution current = _active[i];
                if (!current.definition.motionOnly && (current.definition.resources & ActionResources.Hands) != 0
                    && (current.definition.gameplay & ActionGameplayFlags.Guard) == 0) { return true; }
            }
            return false;
        }

        public bool IsMovementBlocked(int owner)
        {
            Execution.CopyActive(owner, _active);
            for (int i = 0; i < _active.Count; i++)
            {
                ActionExecution current = _active[i];
                if (!current.definition.motionOnly && (current.definition.resources & ActionResources.HorizontalMovement) != 0
                    && !(current.definition.movementUsesInput && current.definition.endMode == ActionEndMode.Held))
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsPhysicsControlled(int owner)
        {
            return Execution.GetPrimary(owner, out ActionExecution current)
                && (current.definition.gameplay & ActionGameplayFlags.PreserveVelocity) != 0;
        }

        public void ApplyMovement(int owner, Rigidbody2D body)
        {
            if (body == null || !(_data.GetManaged(owner) is BaseCharacter character)
                || character.Rigidbody != body) { return; }
            ApplyMovement(owner, character);
        }

        public void ApplyMovement(int owner, BaseCharacter character)
        {
            if (character == null || character.ObjectHash != owner || _data.GetManaged(owner) != character
                || IsPhysicsControlled(owner)) { return; }
            Vector2 current = character.PhysicsVelocity;
            if (Execution.TryGetMovement(owner, true, out Vector2 velocity))
            {
                current.x = velocity.x;
            }
            Execution.CopyActive(owner, _active);
            for (int i = 0; i < _active.Count; i++)
            {
                ActionExecution action = _active[i];
                if (action.definition.motionOnly) { continue; }
                bool horizontal = (action.definition.resources & ActionResources.HorizontalMovement) != 0;
                bool vertical = (action.definition.resources & ActionResources.VerticalMovement) != 0
                    && (action.definition.gameplay & ActionGameplayFlags.Move) != 0;
                if (vertical && Execution.TryGetMovement(owner, false, out Vector2 verticalVelocity)) { current.y = verticalVelocity.y; }
                if (_movementOutputs.TryGetValue(action.instanceId, out Vector2 output))
                {
                    if (horizontal) { current.x = output.x; }
                    if (vertical) { current.y = output.y; }
                    _movementOutputs.Remove(action.instanceId);
                }
            }
            character.SetPhysicsVelocity(current);
        }

        public bool SetMovementOutput(int owner, long instanceId, Vector2 velocity)
        {
            if (!Execution.TryGetInstance(instanceId, out ActionExecution current) || current.ownerHash != owner
                || current.definition.motionOnly || (current.definition.resources & ActionResources.HorizontalMovement) == 0)
            {
                return false;
            }
            _movementOutputs[instanceId] = velocity;
            return true;
        }

        public void React(int owner, HitReaction reaction)
        {
            if (reaction == HitReaction.None) { return; }
            Builtin kind = reaction == HitReaction.GuardBreak ? Builtin.GuardBreak
                : reaction == HitReaction.Knockback ? Builtin.Knockback : Builtin.Flinch;
            StartBuiltin(owner, kind, default, 1f, true);
        }

        public void StopActor(int owner)
        {
            Execution.RemoveActor(owner);
            if (GameManager.IsCharacterValid(owner)) { Execution.RegisterActor(owner); SyncDefense(owner); }
        }

        public void Tick()
        {
            _ownerBuffer.Clear();
            foreach (int owner in _owners) { _ownerBuffer.Add(owner); }
            for (int i = 0; i < _ownerBuffer.Count; i++)
            {
                int owner = _ownerBuffer[i];
                if (!GameManager.IsCharacterValid(owner)) { continue; }
                if (IsHeld(owner, Builtin.Knockback) && _data.GetManaged(owner) is BaseCharacter actor
                    && actor.IsGrounded && actor.PhysicsVelocity.y <= 0f)
                {
                    ReleaseHeld(owner, Builtin.Knockback);
                    StartBuiltin(owner, Builtin.WakeUp, default, 1f, true);
                }
                SyncDefense(owner);
            }
        }

        public void Present()
        {
            _ownerBuffer.Clear();
            foreach (int owner in _owners) { _ownerBuffer.Add(owner); }
            for (int i = 0; i < _ownerBuffer.Count; i++)
            {
                int owner = _ownerBuffer[i];
                if (!GameManager.IsCharacterValid(owner) || !(_data.GetManaged(owner) is BaseCharacter actor)
                    || actor.AnimationController == null) { continue; }
                Execution.CopyActive(owner, _active);
                for (int j = 0; j < _active.Count; j++)
                {
                    ActionExecution current = _active[j];
                    if (_catalog.TryGet(current.definitionId, out ActionCatalog.Entry entry))
                    {
                        actor.AnimationController.Present(current, entry.presentation);
                    }
                }
                AnimationStateData animation = default;
                if (Execution.GetPrimary(owner, out ActionExecution primary))
                {
                    animation.currentPhase = primary.phase;
                    animation.normalizedTime = primary.normalizedTime;
                    animation.isCancelable = primary.canCancel;
                    animation.isCommitted = !primary.canCancel;
                    animation.currentMoveId = (byte)(primary.definitionId % 256);
                }
                actor.AnimationController.SetExecutionState(animation);
            }
        }

        private void HandleExecution(ActionExecutionEvent notification)
        {
            ActionExecution current = notification.execution;
            int owner = current.ownerHash;
            if (current.ownerRef.IsValid && (GameManager.Actors == null || !GameManager.Actors.IsCurrent(current.ownerRef)))
            {
                _combat.Remove(current.instanceId);
                _movementOutputs.Remove(current.instanceId);
                return;
            }
            if (notification.kind == ActionSignalKind.Started && _catalog.TryGet(current.definitionId, out ActionCatalog.Entry entry)
                && entry.isAttack)
            {
                AttackMotionData motion = entry.CreateAttack();
                motion.actionInstanceId = current.instanceId;
                motion.motionValue *= current.chargeMultiplier;
                motion.equipmentStatusEffects = GameManager.Equipment.GetOnHitEffects(owner, entry.equipmentSource);
                MagicDefinition magic = entry.CreateMagic();
                magic.actionInstanceId = current.instanceId;
                magic.motionValue *= current.chargeMultiplier;
                // 発射時に攻撃力・倍率・装備効果を同じ時点で捕捉する。子弾は捕捉済みcontextを継承する。
                magic.equipmentStatusEffects = null;
                _combat[current.instanceId] = new CombatExecution { attack = motion, magic = magic, projectile = entry.projectile };
                if (_data.GetManaged(owner) is BaseCharacter actor && actor.CollisionController != null)
                {
                    actor.CollisionController.SetCollisionMode(entry.contactType);
                }
            }
            if (_combat.TryGetValue(current.instanceId, out CombatExecution combat))
            {
                if (_controllers.TryGetValue(owner, out ActionExecutorController controller) && controller != null)
                {
                    controller.HandleExecution(notification, combat.attack, combat.projectile);
                }
                if (notification.kind == ActionSignalKind.PhaseChanged && current.phase == AnimationPhase.Active && combat.projectile)
                {
                    Vector2 position = _data.GetVitals(owner).position;
                    Vector2 direction = current.direction.sqrMagnitude > 0f ? current.direction : Vector2.right;
                    int targetHash = current.targetRef.IsValid && GameManager.Actors != null
                        && GameManager.Actors.IsCurrent(current.targetRef) ? current.targetHash : 0;
                    if (GameManager.IsCharacterValid(targetHash))
                    {
                        direction = (_data.GetVitals(targetHash).position - position).normalized;
                    }
                    ActorRef target = targetHash != 0 ? current.targetRef : default;
                    GameManager.Projectiles.SpawnSpreadFromExecution(current, combat.magic, position, direction,
                        combat.magic.bulletCount, EntrySpread(current.definitionId), target);
                }
            }
            if (notification.kind == ActionSignalKind.Completed || notification.kind == ActionSignalKind.Interrupted)
            {
                bool hadCombat = _combat.Remove(current.instanceId);
                _movementOutputs.Remove(current.instanceId);
                if (_data.TryGetValue(owner, out _) && _data.GetManaged(owner) is BaseCharacter actor)
                {
                    if (actor.AnimationController != null) { actor.AnimationController.EndExecution(current.instanceId); }
                    if (hadCombat && actor.CollisionController != null)
                    {
                        actor.CollisionController.ClearCollisionMode();
                    }
                }
            }
            SyncDefense(owner);
            if (GameManager.IsCharacterValid(owner) && _data.GetManaged(owner) is BaseCharacter character
                && character.CollisionController != null)
            {
                character.CollisionController.SetInvincible(Execution.EvaluateEffects(owner).isInvincible);
            }
            _events.FireActionExecution(notification);
        }

        private float EntrySpread(int id) => _catalog.TryGet(id, out ActionCatalog.Entry entry) ? entry.spreadAngle : 0f;

        private void SyncDefense(int owner)
        {
            if (!GameManager.IsCharacterValid(owner)) { return; }
            bool guarding = false;
            float guardTime = 0f;
            Execution.CopyActive(owner, _active);
            for (int i = 0; i < _active.Count; i++)
            {
                ActionExecution current = _active[i];
                if (!current.definition.motionOnly && current.phase == AnimationPhase.Active
                    && (current.definition.gameplay & ActionGameplayFlags.Guard) != 0)
                {
                    guarding = true;
                    guardTime = Math.Max(guardTime, current.phaseElapsed);
                }
            }
            ref CharacterDefenseState defense = ref _data.GetDefenseState(owner);
            defense.guardElapsedTime = guardTime;
            defense.isGuarding = guarding;
            defense.actionArmorConsumed = Execution.GetConsumedArmor(owner);
            defense.actionElapsedTime = Execution.GetPrimary(owner, out ActionExecution primary) ? primary.elapsed : 0f;
        }

        private bool StartBuiltin(int owner, Builtin kind, Vector2 direction, float speedMultiplier = 1f,
            bool forced = false, float duration = 0f, ActorRef targetRef = default, long replaceGroupId = 0)
        {
            if (!GameManager.IsCharacterValid(owner)) { return false; }
            int definitionId = Definition(owner, kind);
            if (kind != Builtin.Effects)
            {
                ActionDefinitionData current = BaseDefinition(owner, kind);
                current.id = definitionId;
                if (duration > 0f) { current.endMode = ActionEndMode.Timed; current.duration = duration; }
                Execution.UpdateDefinition(definitionId, current);
            }
            ActorRef ownerRef = default;
            GameManager.Actors?.TryGet(owner, out ownerRef);
            bool accepted = Execution.TryStart(new ActionRequest
            {
                ownerHash = owner, ownerRef = ownerRef, targetHash = targetRef.Hash, targetRef = targetRef,
                definitionId = definitionId, direction = direction,
                speedMultiplier = speedMultiplier, forced = forced, replaceGroupId = replaceGroupId,
            }, out long id);
            if (accepted && Execution.TryGetInstance(id, out _)) { _held[(owner, kind)] = id; }
            return accepted;
        }

        private bool IsHeld(int owner, Builtin kind) => _held.TryGetValue((owner, kind), out long id)
            && Execution.TryGetInstance(id, out _);

        private void ReleaseHeld(int owner, Builtin kind)
        {
            if (_held.TryGetValue((owner, kind), out long id)) { Execution.Release(id); Execution.Complete(id); _held.Remove((owner, kind)); }
        }

        private int Definition(int owner, Builtin kind)
        {
            if (_definitions.TryGetValue((owner, kind), out int existing)) { return existing; }
            int id = Execution.RegisterDefinition(BaseDefinition(owner, kind));
            _definitions.Add((owner, kind), id);
            return id;
        }

        private ActionDefinitionData BaseDefinition(int owner, Builtin kind)
        {
            MoveParams move = _data.GetMoveParams(owner);
            ActionDefinitionData value = new ActionDefinitionData
            {
                cancelAfter = 0f, cancelPoint = -1f, endMode = ActionEndMode.Held,
                durationMode = ActionDurationMode.Fixed, priority = 1,
                resources = ActionResources.HorizontalMovement, gameplay = ActionGameplayFlags.Move,
                movementUsesInput = true, moveSpeed = 1f, actState = ActState.Running,
            };
            if (kind == Builtin.Move) { return value; }
            value.movementUsesInput = false;
            value.gameplay = ActionGameplayFlags.None;
            value.resources = ActionResources.None;
            value.actState = ActState.Neutral;
            if (kind == Builtin.Stop)
            {
                value.resources = ActionResources.HorizontalMovement;
                value.endMode = ActionEndMode.Timed;
                value.duration = 0f;
                return value;
            }
            if (kind == Builtin.Idle) { value.resources = ActionResources.HorizontalMovement; return value; }
            if (kind == Builtin.Jump) { value.staminaCost = move.jumpStaminaCost; value.endMode = ActionEndMode.Manual; value.actState = ActState.Jumping; return value; }
            if (kind == Builtin.Guard) { value.gameplay = ActionGameplayFlags.Guard; value.resources = ActionResources.Hands; value.priority = 5; value.actState = ActState.Guarding; return value; }
            if (kind == Builtin.Effects) { value.endMode = ActionEndMode.Manual; return value; }
            value.endMode = ActionEndMode.Timed;
            value.resources = ActionResources.All;
            value.priority = kind == Builtin.Dodge ? 20 : 100;
            value.cancelAfter = -1f;
            if (kind == Builtin.Dodge)
            {
                value.duration = move.dodgeDuration > 0f ? move.dodgeDuration : 0.3f;
                value.moveDuration = value.duration;
                value.moveSpeed = (move.dashSpeed > 0f ? move.dashSpeed
                    : move.moveSpeed * (move.dodgeSpeedMultiplier > 0f ? move.dodgeSpeedMultiplier : 2f))
                    * _data.GetStatusEffects(owner).MoveSpeedMultiplier;
                value.resources &= ~ActionResources.VerticalMovement;
                value.gameplay = ActionGameplayFlags.Move;
                value.staminaCost = move.dodgeStaminaCost;
                value.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = value.duration } };
                return value;
            }
            value.duration = kind == Builtin.GuardBreak ? HitReactionLogic.k_GuardBreakStunDuration : HitReactionLogic.k_FlinchDuration;
            value.actState = kind == Builtin.GuardBreak ? ActState.GuardBroken : ActState.Flinch;
            if (kind == Builtin.Knockback) { value.endMode = ActionEndMode.Manual; value.actState = ActState.Knockbacked; value.gameplay = ActionGameplayFlags.PreserveVelocity; }
            if (kind == Builtin.WakeUp)
            {
                value.duration = WakeUpLogic.k_DefaultWakeUpDuration; value.actState = ActState.WakeUp;
                value.effects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = value.duration } };
            }
            return value;
        }

        private void RemoveOwner(int owner)
        {
            _owners.Remove(owner);
            foreach (Builtin kind in Enum.GetValues(typeof(Builtin)))
            {
                _held.Remove((owner, kind));
                if (_definitions.TryGetValue((owner, kind), out int id)) { Execution.UnregisterDefinition(id); }
                _definitions.Remove((owner, kind));
            }
        }

        public void Dispose()
        {
            _registered.Dispose(); _removed.Dispose(); _damage.Dispose(); _requested.Dispose(); _death.Dispose();
            Execution.Changed -= HandleExecution;
            _combat.Clear(); _movementOutputs.Clear(); _controllers.Clear(); _held.Clear(); _definitions.Clear(); _owners.Clear();
        }
    }
}
