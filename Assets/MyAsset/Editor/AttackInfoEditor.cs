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

            // タイトル
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("攻撃データ設定", titleStyle);
            EditorGUILayout.Space(4);

            // サマリー
            float totalDuration = info.motionInfo.preMotionDuration
                + info.motionInfo.activeMotionDuration
                + info.motionInfo.recoveryDuration;
            string projText = info.projectileInfo.hasProjectile ? "あり" : "なし";
            string knockText = info.knockbackInfo.hasKnockback ? "あり" : "なし";

            EditorGUILayout.HelpBox(
                $"【{info.attackName}】 カテゴリ: {info.category} / 属性: {info.attackElement}\n" +
                $"ダメージ合計: {info.baseDamage.Total} × {info.damageMultiplier:F1}\n" +
                $"モーション: {totalDuration:F2}s（予備{info.motionInfo.preMotionDuration:F2} + " +
                $"攻撃{info.motionInfo.activeMotionDuration:F2} + 回復{info.motionInfo.recoveryDuration:F2}）\n" +
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

            if (info.motionInfo.activeMotionDuration <= 0)
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
        }
    }
}
#endif
