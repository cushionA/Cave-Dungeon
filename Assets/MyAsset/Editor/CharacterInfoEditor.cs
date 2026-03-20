#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Game.Core;

namespace Game.Editor
{
    [CustomEditor(typeof(CharacterInfo))]
    public class CharacterInfoEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            CharacterInfo info = (CharacterInfo)target;

            // タイトル
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("キャラクター情報", titleStyle);
            EditorGUILayout.Space(4);

            // サマリー
            string featureText = info.feature.ToString();
            string belongText = info.belong.ToString();
            EditorGUILayout.HelpBox(
                $"【{featureText}】 陣営: {belongText} / ランク: {info.rank}\n" +
                $"HP: {info.maxHp} / MP: {info.maxMp} / スタミナ: {info.maxStamina:F0}\n" +
                $"攻撃力合計: {info.baseAttack.Total} / 防御力合計: {info.baseDefense.Total}",
                MessageType.Info);

            EditorGUILayout.Space(4);
            base.OnInspectorGUI();

            // バリデーション
            EditorGUILayout.Space(8);
            if (info.maxHp <= 0)
            {
                EditorGUILayout.HelpBox("maxHp が0以下です。", MessageType.Error);
            }
            if (info.moveSpeed <= 0)
            {
                EditorGUILayout.HelpBox("moveSpeed が0以下です。", MessageType.Warning);
            }
            if (info.staminaRecoveryRate <= 0 && info.maxStamina > 0)
            {
                EditorGUILayout.HelpBox("スタミナ回復速度が0です。", MessageType.Warning);
            }
        }
    }
}
#endif
