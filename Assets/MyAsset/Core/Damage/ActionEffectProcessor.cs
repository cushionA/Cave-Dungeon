namespace Game.Core
{
    /// <summary>
    /// 行動中の特殊効果（アーマー、無敵、スーパーアーマーなど）を評価する。
    /// ActionEffect配列と経過時間から現在有効な効果を判定するpureロジック。
    /// </summary>
    public static class ActionEffectProcessor
    {
        /// <summary>
        /// 現在の行動特殊効果の状態。
        /// </summary>
        public struct EffectState
        {
            public bool isInvincible;
            public bool hasSuperArmor;
            public bool hasGuardPoint;
            public float actionArmorValue;
            public float damageReduction;
        }

        /// <summary>
        /// 指定時間における有効な行動特殊効果を集計する。
        /// 複数の同種効果が同時にアクティブな場合:
        /// - Armor: 合算
        /// - DamageReduction: 最大値
        /// - SuperArmor/Invincible/GuardPoint: OR
        /// </summary>
        public static EffectState Evaluate(ActionEffect[] effects, float elapsedTime)
        {
            EffectState state = default;

            if (effects == null)
            {
                return state;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (!effects[i].IsActive(elapsedTime))
                {
                    continue;
                }

                switch (effects[i].type)
                {
                    case ActionEffectType.Armor:
                        state.actionArmorValue += effects[i].value;
                        break;
                    case ActionEffectType.SuperArmor:
                        state.hasSuperArmor = true;
                        break;
                    case ActionEffectType.Invincible:
                        state.isInvincible = true;
                        break;
                    case ActionEffectType.DamageReduction:
                        if (effects[i].value > state.damageReduction)
                        {
                            state.damageReduction = effects[i].value;
                        }
                        break;
                    case ActionEffectType.GuardPoint:
                        state.hasGuardPoint = true;
                        break;
                }
            }

            return state;
        }

        /// <summary>
        /// 指定の効果タイプが現在アクティブかどうかを判定する。
        /// </summary>
        public static bool HasActiveEffect(ActionEffect[] effects, float elapsedTime,
            ActionEffectType type)
        {
            if (effects == null)
            {
                return false;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].type == type && effects[i].IsActive(elapsedTime))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 指定の効果タイプの現在のvalue合計を取得する。
        /// </summary>
        public static float GetActiveValue(ActionEffect[] effects, float elapsedTime,
            ActionEffectType type)
        {
            float total = 0f;

            if (effects == null)
            {
                return total;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].type == type && effects[i].IsActive(elapsedTime))
                {
                    total += effects[i].value;
                }
            }

            return total;
        }
    }
}
