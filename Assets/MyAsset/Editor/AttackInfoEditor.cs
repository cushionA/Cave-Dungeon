#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Game.Core;

namespace Game.Editor
{
    [CustomEditor(typeof(AttackInfo))]
    public class AttackInfoEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            AttackInfo info = (AttackInfo)target;
            MotionInfo motion = info.GetMotion();

            // タイトル
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("攻撃データ設定", titleStyle);
            EditorGUILayout.Space(4);

            // サマリー
            float totalDuration = motion.preMotionDuration
                + motion.activeMotionDuration
                + motion.recoveryDuration;
            string projText = info.projectileInfo.hasProjectile ? "あり" : "なし";
            string knockText = info.knockbackInfo.hasKnockback ? "あり" : "なし";

            EditorGUILayout.HelpBox(
                $"【{info.attackName}】 カテゴリ: {info.category} / 属性: {info.attackElement}\n" +
                $"ダメージ合計: {info.baseDamage.Total} × {info.damageMultiplier:F1}\n" +
                $"モーション: {totalDuration:F2}s（予備{motion.preMotionDuration:F2} + " +
                $"攻撃{motion.activeMotionDuration:F2} + 回復{motion.recoveryDuration:F2}）\n" +
                $"コスト: MP{info.mpCost} / スタミナ{info.staminaCost}\n" +
                $"飛翔体: {projText} / ノックバック: {knockText}",
                MessageType.Info);

            EditorGUILayout.Space(4);
            base.OnInspectorGUI();

            // バリデーション
            EditorGUILayout.Space(8);
            ValidateAttackInfo(info);
        }

        private void ValidateAttackInfo(AttackInfo info)
        {
            MotionInfo motion = info.GetMotion();
            if (string.IsNullOrEmpty(info.attackName))
            {
                EditorGUILayout.HelpBox("attackName が未設定です。", MessageType.Error);
            }

            if (info.baseDamage.Total <= 0 && info.category != AttackCategory.Support)
            {
                EditorGUILayout.HelpBox("baseDamage の合計が0です（Support以外では異常）。", MessageType.Warning);
            }

            if (info.damageMultiplier <= 0)
            {
                EditorGUILayout.HelpBox("damageMultiplier が0以下です。", MessageType.Error);
            }

            if (motion.activeMotionDuration <= 0)
            {
                EditorGUILayout.HelpBox("activeMotionDuration が0以下です。", MessageType.Warning);
            }

            if (info.projectileInfo.hasProjectile)
            {
                if (info.projectileInfo.projectilePrefab == null)
                {
                    EditorGUILayout.HelpBox("飛翔体が有効ですが projectilePrefab が未設定です。", MessageType.Error);
                }
                if (info.projectileInfo.speed <= 0)
                {
                    EditorGUILayout.HelpBox("飛翔体の speed が0以下です。", MessageType.Warning);
                }
                if (info.projectileInfo.lifetime <= 0)
                {
                    EditorGUILayout.HelpBox("飛翔体の lifetime が0以下です。", MessageType.Warning);
                }
            }

            if (info.knockbackInfo.hasKnockback && info.knockbackInfo.force == Vector2.zero)
            {
                EditorGUILayout.HelpBox("ノックバックが有効ですが force が(0,0)です。", MessageType.Warning);
            }

            if (info.isAutoChain && info.isChainEndPoint)
            {
                EditorGUILayout.HelpBox("isAutoChain と isChainEndPoint が両方trueです。矛盾している可能性があります。", MessageType.Warning);
            }

            if (info.inputWindow < 0)
            {
                EditorGUILayout.HelpBox("inputWindow が負の値です。", MessageType.Error);
            }

            ValidateStatusEffects(info.statusEffects);
        }

        private static void ValidateStatusEffects(StatusEffectApplication[] applications)
        {
            if (applications == null)
            {
                return;
            }

            for (int i = 0; i < applications.Length; i++)
            {
                StatusEffectApplication application = applications[i];
                string label = $"状態効果 [{i + 1}]";
                StatusEffectDefinition definition = application.definition;
                if (definition == null)
                {
                    EditorGUILayout.HelpBox($"{label}: 効果定義が未設定です。", MessageType.Error);
                    continue;
                }

                if (!CharacterStatusEffects.IsValidId(definition.effect))
                {
                    EditorGUILayout.HelpBox($"{label}: 効果IDが未設定または無効です。", MessageType.Error);
                }
                if (!IsFinite(definition.duration) || definition.duration <= 0f)
                {
                    EditorGUILayout.HelpBox($"{label}: 効果定義の持続時間は0より大きい有限値にしてください。", MessageType.Error);
                }
                if (!IsFinite(definition.tickDamage) || definition.tickDamage < 0f)
                {
                    EditorGUILayout.HelpBox($"{label}: 効果定義の周期ダメージは0以上の有限値にしてください。", MessageType.Error);
                }
                if (!IsFinite(definition.tickInterval) || definition.tickInterval < 0f
                    || definition.tickDamage > 0f && definition.tickInterval <= 0f)
                {
                    EditorGUILayout.HelpBox($"{label}: 周期は0以上、周期ダメージがある場合は0より大きい有限値にしてください。", MessageType.Error);
                }
                if (!IsFinite(definition.modifier) || definition.modifier < 0f || definition.modifier > 1f)
                {
                    EditorGUILayout.HelpBox($"{label}: 効果定義の割合は0〜1の有限値にしてください。", MessageType.Error);
                }
                if (!IsFinite(application.accumulateValue) || application.accumulateValue < 0f
                    || application.mode == StatusEffectApplicationMode.Accumulate && application.accumulateValue <= 0f)
                {
                    EditorGUILayout.HelpBox($"{label}: 蓄積付与では蓄積量を0より大きい有限値にしてください。", MessageType.Error);
                }
                if (!System.Enum.IsDefined(typeof(StatusEffectApplicationMode), application.mode)
                    || !System.Enum.IsDefined(typeof(StatusEffectTarget), application.target)
                    || !System.Enum.IsDefined(typeof(StatusEffectGuardRule), application.guardRule))
                {
                    EditorGUILayout.HelpBox($"{label}: 付与方式・対象・ガード条件の設定が無効です。", MessageType.Error);
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
#endif
