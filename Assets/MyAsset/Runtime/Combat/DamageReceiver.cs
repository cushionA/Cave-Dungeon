using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    public class DamageReceiver : MonoBehaviour, IDamageable
    {
        private BaseCharacter _character;
        private int _objectHash;
        private SituationalBonusConfig _bonusConfig = SituationalBonusConfig.Default;

        public int ObjectHash => _objectHash;
        public bool IsAlive => _character != null && _character.IsAlive;

        private void Awake()
        {
            _objectHash = gameObject.GetHashCode();
            _character = GetComponent<BaseCharacter>();
        }

        private void Start()
        {
            if (_character == null)
            {
                _character = GetComponent<BaseCharacter>();
            }
            if (_character != null) { _character.BindDamageReceiver(this); }
        }

        public void SetGuarding(bool isGuarding)
        {
            GameManager.Actions?.SetGuarding(ObjectHash, isGuarding);
        }

        public void SetActionEffects(ActionEffect[] effects)
        {
            GameManager.Actions?.SetActionEffects(ObjectHash, effects);
        }

        public void ClearActionEffects()
        {
            GameManager.Actions?.SetActionEffects(ObjectHash, null);
        }

        public void SetArmorRecoveryParams(float recoveryRate, float recoveryDelay)
        {
            if (!GameManager.IsCharacterValid(ObjectHash))
            {
                return;
            }
            ref CharacterDefenseState state = ref GameManager.Data.GetDefenseState(ObjectHash);
            state.armorRecoveryRate = recoveryRate;
            state.armorRecoveryDelay = recoveryDelay;
        }

        public void ResetInternalState()
        {
            GameManager.Actions?.StopActor(ObjectHash);
            if (!GameManager.IsCharacterValid(ObjectHash))
            {
                return;
            }
            ref CharacterDefenseState state = ref GameManager.Data.GetDefenseState(ObjectHash);
            state = new CharacterDefenseState
            {
                armorRecoveryRate = state.armorRecoveryRate,
                armorRecoveryDelay = state.armorRecoveryDelay,
                continuousJustGuardUntil = -1f,
                previousActState = ActState.Neutral,
            };
        }

        public void SetBonusConfig(SituationalBonusConfig config)
        {
            _bonusConfig = config;
        }

        public void TickRecovery(float deltaTime)
        {
            int hash = ObjectHash;
            if (!GameManager.IsCharacterValid(hash))
            {
                return;
            }
            HitResolver.TickRecovery(ref GameManager.Data.GetVitals(hash),
                GameManager.Data.GetFlags(hash).ActState,
                ref GameManager.Data.GetDefenseState(hash), deltaTime);
        }

        public DamageResult ReceiveDamage(DamageData attack)
        {
            int hash = ObjectHash;
            if (_character == null || !GameManager.IsCharacterValid(hash))
            {
                return default;
            }
            ActorRegistry actors = GameManager.Actors;
            ActorRef targetRef = default;
            if (actors != null) { actors.TryGet(hash, out targetRef); }
            if (attack.defenderRef.IsValid && (actors == null || attack.defenderRef != targetRef)) { return default; }
            if (attack.attackerRef.IsValid && !attack.attackContext.IsCaptured
                && (actors == null || !actors.IsCurrent(attack.attackerRef))) { return default; }
            if (attack.attackContext.IsCaptured)
            {
                if (!CombatDataHelper.CanAffectTarget(attack.attackContext, targetRef,
                    GameManager.Data, actors)) { return default; }
                attack.attackContext.ApplyTo(ref attack, targetRef);
            }
            else if (attack.targetPolicy.overrideTargets)
            {
                // Explicit conditions on the legacy direct API must not bypass the shared rule.
                if (actors == null || !actors.TryGet(attack.attackerHash, out ActorRef source)
                    || !GameManager.IsCharacterValid(source.Hash)
                    || !AttackTargetRules.CanAffect(source, GameManager.Data.GetFlags(source.Hash).Belong,
                        targetRef, GameManager.Data.GetFlags(hash).Belong, attack.targetPolicy.Resolve()))
                { return default; }
            }

            SoACharaDataDic data = GameManager.Data;
            CharacterDefenseState defense = data.GetDefenseState(hash);
            defense.actionArmorConsumed = 0f;
            ActState previousState = data.GetFlags(hash).ActState;
            HitContext context = new HitContext
            {
                actionEffects = GameManager.Actions.Execution.EvaluateEffects(hash),
                targetEffects = data.GetStatusEffects(hash),
                attackerAttackMultiplier = attack.attackContext.IsCaptured ? attack.attackContext.AttackMultiplier
                    : GameManager.IsCharacterValid(attack.attackerHash)
                    ? data.GetStatusEffects(attack.attackerHash).AttackMultiplier : 1f,
                isAttackFromFront = IsAttackFromFront(attack),
                currentTime = Time.time,
                bonusConfig = _bonusConfig,
            };

            float guardStatusCut;
            HitResolution resolution;
            {
                ref readonly CombatStats combat = ref data.GetCombatStats(hash);
                guardStatusCut = combat.guardStats.statusCut;
                resolution = HitResolver.Resolve(attack,
                    ref data.GetVitals(hash), in combat, ref data.GetFlags(hash),
                    ref defense, context);
            }
            if (resolution.ignored)
            {
                return resolution.result;
            }

            DamageResult result = resolution.result;
            result.attackerRef = attack.attackContext.IsCaptured ? attack.attackContext.Owner : attack.attackerRef;
            if (!result.attackerRef.IsValid && actors != null) { actors.TryGet(attack.attackerHash, out result.attackerRef); }
            result.defenderRef = targetRef;
            GameManager.Actions.Execution.ConsumeArmor(hash, result.actionArmorDamage);
            defense.actionArmorConsumed = GameManager.Actions.Execution.GetConsumedArmor(hash);
            data.GetDefenseState(hash) = defense;
            if (!result.isKill && !(previousState == ActState.Flinch && result.hitReaction == HitReaction.Flinch))
            {
                GameManager.Actions.React(hash, result.hitReaction);
            }
            bool sameTarget = !targetRef.IsValid || (actors != null && actors.IsCurrent(targetRef));
            if (sameTarget && attack.attackContext.IsCaptured)
            {
                result.appliedEffect = GameManager.StatusEffects.ApplyApplications(
                    attack.attackContext, hash, result.guardResult, guardStatusCut);
            }
            else if (sameTarget)
            {
                result.appliedEffect = GameManager.StatusEffects.ApplyApplications(
                    attack.statusEffects, attack.attackerHash, hash, result.guardResult,
                    guardStatusCut, attack.equipmentStatusEffects);
            }

            GameEvents events = GameManager.Events;
            if (events != null)
            {
                events.FireDamageDealt(result, attack.attackerHash, hash);
                if (result.guardResult != GuardResult.NoGuard
                    && (actors == null || actors.IsCurrent(targetRef)))
                {
                    int liveAttackerHash = actors == null ? attack.attackerHash
                        : actors.IsCurrent(result.attackerRef) ? result.attackerRef.Hash : 0;
                    events.FireGuardEvent(hash, liveAttackerHash, result.guardResult);
                }
                if (result.isKill)
                {
                    if (targetRef.IsValid) { events.FireCharacterDeath(targetRef, result.attackerRef); }
                    else { events.FireCharacterDeath(hash, attack.attackerHash); }
                }
            }
            if (_character != null && resolution.knockback.sqrMagnitude > 0f
                && (!targetRef.IsValid || (actors != null && actors.IsCurrent(targetRef))))
            {
                _character.ApplyPhysicsImpulse(resolution.knockback);
            }
            return result;
        }

        private bool IsAttackFromFront(in DamageData attack)
        {
            if (attack.knockbackForce.sqrMagnitude < 0.001f)
            {
                return true;
            }
            float facingX = _character.transform.localScale.x >= 0f ? 1f : -1f;
            return facingX > 0f ? attack.knockbackForce.x < 0f : attack.knockbackForce.x > 0f;
        }
    }
}
