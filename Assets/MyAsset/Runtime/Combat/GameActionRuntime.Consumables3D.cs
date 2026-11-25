using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        private sealed class ConsumableUse
        {
            public readonly PlayerItemUse3D presenter;
            public readonly ActorRef owner;
            public readonly int actionId;
            public readonly InventoryManager inventory;
            public readonly SaveManager saves;
            public readonly int slot;
            public readonly ulong loadRevision;
            public readonly int itemId;
            public readonly bool consume;
            public readonly int mpCost;
            public readonly bool isAttack;
            public readonly int basePower;
            public readonly StatType scalingStat;
            public readonly float statScale;
            public readonly MotionInfo motion;
            public readonly GameObject effect;
            public readonly Vector3 effectOffset;
            public readonly float effectLifetime;
            public bool resolved;

            public ConsumableUse(PlayerItemUse3D presenter, ActorRef owner, ActionCatalog.Entry action,
                InventoryManager inventory, SaveManager saves, ConsumableDefinition item)
            {
                this.presenter = presenter;
                this.owner = owner;
                actionId = action.definition.id;
                this.inventory = inventory;
                this.saves = saves;
                slot = saves.ActiveSlotIndex;
                loadRevision = saves.LoadRevision;
                itemId = item.ItemId;
                consume = item.ConsumeOnUse;
                mpCost = item.MpCost;
                isAttack = action.isAttack;
                basePower = item.BasePower;
                scalingStat = item.ScalingStat;
                statScale = item.StatScale;
                motion = action.presentation.Motion;
                effect = item.EffectPrefab;
                effectOffset = item.EffectOffset;
                effectLifetime = item.EffectLifetime;
            }
        }

        private readonly Dictionary<long, ConsumableUse> _consumableUses = new Dictionary<long, ConsumableUse>();
        private ConsumableUse _startingConsumable;

        internal bool TryUseConsumable3D(PlayerItemUse3D presenter, CharacterActor3D owner,
            ConsumableDefinition item, out long instanceId)
        {
            instanceId = 0;
            InventoryManager inventory = GameManager.Inventory;
            SaveManager saves = GameManager.Saves;
            if (_startingConsumable != null || presenter == null || !presenter.isActiveAndEnabled
                || owner == null || !owner.IsAlive || !owner.Body.enabled || item == null || item.UseAction == null
                || inventory == null || saves == null || inventory.GetConsumableCount(item.ItemId) <= 0
                || item.MpCost < 0 || item.BasePower <= 0 || !Finite(item.StatScale) || item.StatScale < 0f) { return false; }

            ActorRef actor = owner.Actor;
            if ((_data.GetFlags(actor.Hash).Feature & CharacterFeature.Player) == 0
                || !HasConsumableMp(_data.GetVitals(actor.Hash).currentMp, item.MpCost)) { return false; }

            int definitionId;
            try { definitionId = _catalog.Register(item.UseAction); }
            catch (ArgumentException) { return false; }
            // アクションの開始コストを使わず、アイテムの個数とMPを効果発揮時にまとめて確定する。
            if (!_catalog.TryGet(definitionId, out ActionCatalog.Entry entry)
                || entry.definition.endMode != ActionEndMode.Timed || entry.definition.durationMode != ActionDurationMode.Motion
                || entry.definition.activeDuration <= 0f || entry.definition.transitions.Count != 0
                || (entry.definition.resources & ActionResources.Hands) == 0
                || entry.definition.mpCost != 0 || entry.definition.staminaCost != 0f || entry.definition.staminaPerSecond != 0f
                || (entry.isAttack && !CanReleaseItemAttack(owner, entry.attack3D.shape, entry.AttackElement)))
            {
                return false;
            }

            _startingConsumable = new ConsumableUse(presenter, actor, entry, inventory, saves, item);
            try
            {
                return Execution.TryStart(new ActionRequest
                {
                    ownerHash = actor.Hash, ownerRef = actor, definitionId = definitionId,
                    chargeMultiplier = 1f, speedMultiplier = 1f,
                }, out instanceId);
            }
            finally { _startingConsumable = null; }
        }

        private static bool CanReleaseItemAttack(CharacterActor3D owner, AttackShape3D shape, Element element)
            => shape != AttackShape3D.None && ItemAttackStats(element, 1).Total > 0
            && (shape != AttackShape3D.GroundArea || owner.Motor.IsGrounded)
            && (shape != AttackShape3D.Projectile || GameManager.Projectiles3D != null);

        private static bool HasConsumableMp(float currentMp, int mpCost)
            => mpCost == 0 || (Finite(currentMp) && currentMp >= mpCost);

        private void TrackConsumableExecution(in ActionExecutionEvent notification)
        {
            ref readonly ActionExecution current = ref notification.execution;
            ConsumableUse starting = _startingConsumable;
            if (notification.kind == ActionSignalKind.Started && starting != null && !current.forced
                && current.definitionId == starting.actionId && current.ownerRef == starting.owner)
            {
                _consumableUses.Add(current.instanceId, starting);
            }
            else if ((notification.kind == ActionSignalKind.Completed || notification.kind == ActionSignalKind.Interrupted)
                && _consumableUses.Remove(current.instanceId, out ConsumableUse ended) && ended.presenter != null)
            {
                ended.presenter.EndPresentation(current.instanceId);
            }
        }

        private bool TryCommitConsumable(ConsumableUse use, CharacterActor3D owner, in ActionExecution current,
            CombatExecution combat, out AttackContext attackContext)
        {
            attackContext = default;
            if (use.resolved) { return false; }
            use.resolved = true;
            Scene scene = owner.gameObject.scene;
            if (!Execution.CanApplyExecutionEffects(current.instanceId) || use.presenter == null || !use.presenter.isActiveAndEnabled
                || owner.Actor != use.owner || !owner.Body.enabled || _data.GetFlags(use.owner.Hash).IsVanished
                || !scene.isLoaded || !ReferenceEquals(GameManager.Saves, use.saves)
                || use.saves.ActiveSlotIndex != use.slot || use.saves.LoadRevision != use.loadRevision
                || use.inventory.GetConsumableCount(use.itemId) <= 0
                || use.isAttack != (combat != null)
                || (combat != null && !CanReleaseItemAttack(owner, combat.attack3D.shape, combat.attack.attackElement))
                || (combat != null && combat.attack3D.shape == AttackShape3D.GroundArea
                    && !combat.groundAttack3D.BelongsTo(scene.GetPhysicsScene()))) { return false; }

            ref CharacterVitals vitals = ref _data.GetVitals(use.owner.Hash);
            if (!HasConsumableMp(vitals.currentMp, use.mpCost)) { return false; }

            int power = ConsumableDefinition.CalculatePower(use.basePower, use.scalingStat, use.statScale,
                _data.GetAttributes(use.owner.Hash));
            if (use.consume && !ItemUsageLogic.TryUseConsumable(use.inventory, use.itemId)) { return false; }
            if (use.mpCost > 0) { vitals.currentMp -= use.mpCost; }

            if (combat == null)
            {
                vitals.currentHp = (int)Math.Min(vitals.maxHp, (long)vitals.currentHp + power);
            }
            else
            {
                attackContext = AttackContext.Capture(use.owner, ItemAttackStats(combat.attack.attackElement, power), 1f,
                    _data.GetFlags(use.owner.Hash).Belong, null, combat.attack.statusEffects, combat.attack.targetPolicy.Resolve());
            }
            return true;
        }

        private static ElementalStatus ItemAttackStats(Element element, int power) => element switch
        {
            Element.Slash => new ElementalStatus { slash = power },
            Element.Strike => new ElementalStatus { strike = power },
            Element.Pierce => new ElementalStatus { pierce = power },
            Element.Fire => new ElementalStatus { fire = power },
            Element.Thunder => new ElementalStatus { thunder = power },
            Element.Light => new ElementalStatus { light = power },
            Element.Dark => new ElementalStatus { dark = power },
            _ => default,
        };

        private static void PresentConsumablePhase(ConsumableUse use, in ActionExecution current)
        {
            if (use != null && use.presenter != null && use.presenter.isActiveAndEnabled)
            {
                use.presenter.PresentPhase(current.instanceId, use.motion, current.phase, current.phaseElapsed);
            }
        }

        private static void ShowConsumableEffect(ConsumableUse use, CharacterActor3D owner, CombatExecution combat)
        {
            if (use.effect == null || owner == null) { return; }
            bool ground = combat != null && combat.attack3D.shape == AttackShape3D.GroundArea && combat.groundAttack3D.IsValid;
            Scene scene = ground ? combat.groundAttack3D.Surface.gameObject.scene : owner.gameObject.scene;
            if (!scene.isLoaded) { return; }
            Vector3 position = ground ? combat.groundAttack3D.Center + owner.transform.TransformVector(use.effectOffset)
                : owner.transform.TransformPoint(use.effectOffset);
            GameObject effect = UnityEngine.Object.Instantiate(use.effect, position, owner.transform.rotation);
            if (effect != null)
            {
                if (scene.isLoaded && effect.scene != scene) { SceneManager.MoveGameObjectToScene(effect, scene); }
                float seconds = Finite(use.effectLifetime) && use.effectLifetime > 0f ? use.effectLifetime : 1.25f;
                UnityEngine.Object.Destroy(effect, seconds);
            }
        }

        internal void CancelConsumableUses(PlayerItemUse3D presenter)
        {
            if (_consumableUses.Count == 0) { return; }
            List<long> held = new List<long>();
            foreach (KeyValuePair<long, ConsumableUse> pending in _consumableUses)
            {
                if (pending.Value.presenter == presenter) { held.Add(pending.Key); }
            }
            foreach (long id in held) { Execution.Cancel(id, force: true); }
        }
    }
}
