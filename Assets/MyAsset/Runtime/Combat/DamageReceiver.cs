using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    public class DamageReceiver : MonoBehaviour, IDamageable
    {
        private BaseCharacter _character;
        private int _objectHash;
        private Rigidbody2D _rb;
        private SituationalBonusConfig _bonusConfig = SituationalBonusConfig.Default;

        public int ObjectHash => _objectHash;
        public bool IsAlive => _character != null && _character.IsAlive;

        private void Awake()
        {
            _objectHash = gameObject.GetHashCode();
            _character = GetComponent<BaseCharacter>();
            _rb = GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            if (_character == null)
            {
                _character = GetComponent<BaseCharacter>();
            }
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

        public void SetArmorRecoveryParams(float maxArmor, float recoveryRate, float recoveryDelay)
        {
            if (!GameManager.IsCharacterValid(ObjectHash))
            {
                return;
            }
            ref CharacterDefenseState state = ref GameManager.Data.GetDefenseState(ObjectHash);
            state.maxArmor = maxArmor;
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
                maxArmor = state.maxArmor,
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

        private void Update()
        {
            int hash = ObjectHash;
            if (!GameManager.IsCharacterValid(hash))
            {
                return;
            }
            HitResolver.TickRecovery(ref GameManager.Data.GetVitals(hash),
                GameManager.Data.GetFlags(hash).ActState,
                ref GameManager.Data.GetDefenseState(hash), Time.deltaTime);
        }

        public DamageResult ReceiveDamage(DamageData attack)
        {
            int hash = ObjectHash;
            if (_character == null || !GameManager.IsCharacterValid(hash))
            {
                return default;
            }

            SoACharaDataDic data = GameManager.Data;
            CharacterDefenseState defense = data.GetDefenseState(hash);
            defense.actionArmorConsumed = 0f;
            CombatStats combat = data.GetCombatStats(hash);
            ActState previousState = data.GetFlags(hash).ActState;
            HitContext context = new HitContext
            {
                actionEffects = GameManager.Actions.Execution.EvaluateEffects(hash),
                targetEffects = data.GetStatusEffects(hash),
                attackerAttackMultiplier = GameManager.IsCharacterValid(attack.attackerHash)
                    ? data.GetStatusEffects(attack.attackerHash).AttackMultiplier : 1f,
                isAttackFromFront = IsAttackFromFront(attack),
                currentTime = Time.time,
                bonusConfig = _bonusConfig,
            };

            HitResolution resolution = HitResolver.Resolve(attack,
                ref data.GetVitals(hash), combat, ref data.GetFlags(hash),
                ref defense, context);
            if (resolution.ignored)
            {
                return resolution.result;
            }

            DamageResult result = resolution.result;
            GameManager.Actions.Execution.ConsumeArmor(hash, result.actionArmorDamage);
            defense.actionArmorConsumed = GameManager.Actions.Execution.GetConsumedArmor(hash);
            data.GetDefenseState(hash) = defense;
            if (!result.isKill && !(previousState == ActState.Flinch && result.hitReaction == HitReaction.Flinch))
            {
                GameManager.Actions.React(hash, result.hitReaction);
            }
            result.appliedEffect = GameManager.StatusEffects.ApplyApplications(
                attack.statusEffects, attack.attackerHash, hash, result.guardResult,
                combat.guardStats.statusCut, attack.equipmentStatusEffects);

            GameEvents events = GameManager.Events;
            if (events != null)
            {
                events.FireDamageDealt(result, attack.attackerHash, hash);
                if (result.guardResult != GuardResult.NoGuard)
                {
                    events.FireGuardEvent(hash, attack.attackerHash, result.guardResult);
                }
                if (result.isKill)
                {
                    events.FireCharacterDeath(hash, attack.attackerHash);
                }
            }
            if (_rb != null && resolution.knockback.sqrMagnitude > 0f)
            {
                _rb.AddForce(resolution.knockback, ForceMode2D.Impulse);
            }
            return result;
        }

        private bool IsAttackFromFront(DamageData attack)
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
