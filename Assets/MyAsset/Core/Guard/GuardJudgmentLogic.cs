using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// ガード判定ロジック。
    /// タイミングベースのジャストガード、スタミナ削り、GuardAttack効果による保護を処理する。
    /// ダメージ軽減は GuardStats の属性別カット率に一本化し、ここでは軽減率を返さない。
    /// </summary>
    public static class GuardJudgmentLogic
    {
        /// <summary>ジャストガード受付時間（秒）</summary>
        public const float k_JustGuardWindow = 0.1f;

        /// <summary>ジャストガード成立後、次のガードで即ジャスガ成立となる猶予時間（秒）。連続ジャスガ対応。</summary>
        public const float k_ContinuousJustGuardWindow = 6.0f;

        /// <summary>ジャストガード時のスタミナ回復量</summary>
        public const float k_JustGuardStaminaRecovery = 15f;

        /// <summary>ジャストガード時のアーマー回復量</summary>
        public const float k_JustGuardArmorRecovery = 10f;

        /// <summary>
        /// ガード結果を判定する。
        /// 判定優先順:
        ///   1. isGuarding=false => NoGuard
        ///   2. ガード方向不一致 => NoGuard
        ///   3. ジャスガタイミング窓内 (guardTimeSinceStart &lt;= JustGuardWindow OR 連続JG窓中):
        ///      - !JustGuardImmune => JustGuard (Unguardable でもジャスガは可能)
        ///      - JustGuardImmune:
        ///        - Unguardable => NoGuard (両方立つと完全防御不能)
        ///        - else => Guarded (通常ガード成立、スタミナ判定へ)
        ///   4. ジャスガタイミング窓外:
        ///      - Unguardable => NoGuard (通常ガード不成立)
        ///      - else => Guarded (スタミナ判定へ)
        ///   5. スタミナ削り(=max(0, armorBreakValue - guardStrength)) &gt; currentStamina
        ///      &amp;&amp; !GuardAttack効果 => GuardBreak
        /// </summary>
        /// <param name="isGuarding">ガードボタン押下中か</param>
        /// <param name="guardTimeSinceStart">ガード開始からの経過秒数</param>
        /// <param name="inContinuousJustGuardWindow">直前のJustGuardから k_ContinuousJustGuardWindow 秒以内か</param>
        /// <param name="attackFeature">攻撃フィーチャー(Unguardable/JustGuardImmune)</param>
        /// <param name="guardDirection">ガード方向(Front/Back/Both)</param>
        /// <param name="isAttackFromFront">攻撃が前方から来ているか</param>
        /// <param name="hasGuardAttackEffect">GuardAttack行動特殊効果が有効か(スタミナ枯渇でもブレイクしない)</param>
        /// <param name="currentStamina">被弾側の現在スタミナ</param>
        /// <param name="guardStrength">ガード側の受け値(スタミナ削り抵抗)</param>
        /// <param name="armorBreakValue">攻撃側のアーマー削り値</param>
        public static GuardResult Judge(
            bool isGuarding,
            float guardTimeSinceStart,
            bool inContinuousJustGuardWindow,
            AttackFeature attackFeature,
            GuardDirection guardDirection,
            bool isAttackFromFront,
            bool hasGuardAttackEffect,
            float currentStamina,
            float guardStrength,
            float armorBreakValue)
        {
            if (!isGuarding)
            {
                return GuardResult.NoGuard;
            }

            // ガード方向チェック
            if (!IsGuardDirectionValid(guardDirection, isAttackFromFront))
            {
                return GuardResult.NoGuard;
            }

            bool isUnguardable = (attackFeature & AttackFeature.Unguardable) != 0;
            bool isJustGuardImmune = (attackFeature & AttackFeature.JustGuardImmune) != 0;
            bool inJustGuardWindow = guardTimeSinceStart <= k_JustGuardWindow
                || inContinuousJustGuardWindow;

            // ジャスガタイミング窓内: ジャスガが成立できるなら最優先
            if (inJustGuardWindow && !isJustGuardImmune)
            {
                return GuardResult.JustGuard;
            }

            // ジャスガ不成立 (タイミング外 or JustGuardImmune): 通常ガードを試みる
            if (isUnguardable)
            {
                return GuardResult.NoGuard;
            }

            // スタミナ削り判定: 削り量がスタミナ残量を超える場合、GuardAttack効果が無ければブレイク
            float drain = CalculateStaminaDrain(armorBreakValue, guardStrength);
            if (drain > 0f && currentStamina < drain && !hasGuardAttackEffect)
            {
                return GuardResult.GuardBreak;
            }

            return GuardResult.Guarded;
        }

        /// <summary>
        /// スタミナ削り量を計算する。
        /// 削り = max(0, armorBreakValue - guardStrength)
        /// guardStrength が armorBreakValue 以上なら完全受け流し(削りなし)。
        /// </summary>
        public static float CalculateStaminaDrain(float armorBreakValue, float guardStrength)
        {
            float drain = armorBreakValue - guardStrength;
            return drain > 0f ? drain : 0f;
        }

        /// <summary>
        /// ガード方向が攻撃方向に対して有効か判定する。
        /// </summary>
        public static bool IsGuardDirectionValid(GuardDirection guardDirection, bool isAttackFromFront)
        {
            switch (guardDirection)
            {
                case GuardDirection.Front:
                    return isAttackFromFront;
                case GuardDirection.Back:
                    return !isAttackFromFront;
                case GuardDirection.Both:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// ガードが成功したかどうかを判定する（Guarded / JustGuard）。
        /// NoGuard / GuardBreak は false を返す。
        /// </summary>
        public static bool IsGuardSucceeded(GuardResult result)
        {
            return result == GuardResult.Guarded
                || result == GuardResult.JustGuard;
        }

        /// <summary>
        /// ジャストガード成功時のスタミナ・アーマー回復を適用する。
        /// </summary>
        public static void ApplyJustGuardRecovery(
            ref float currentStamina, float maxStamina,
            ref float currentArmor, float maxArmor)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + k_JustGuardStaminaRecovery);
            currentArmor = Mathf.Min(maxArmor, currentArmor + k_JustGuardArmorRecovery);
        }
    }
}
