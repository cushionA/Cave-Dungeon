#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Game.Core;

namespace Game.Editor
{
    [CustomEditor(typeof(AIInfo))]
    public class AIInfoEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            AIInfo info = (AIInfo)target;

            // タイトル
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("AI行動データ設定", titleStyle);
            EditorGUILayout.Space(4);

            // サマリー
            int modeCount = info.modes != null ? info.modes.Length : 0;
            int actCount = info.actDataList != null ? info.actDataList.Length : 0;
            EditorGUILayout.HelpBox(
                $"モード数: {modeCount} / 行動数: {actCount}\n" +
                $"グローバルCT: {info.coolTimeData.globalCoolTime}s\n" +
                $"仲間設定: 追従距離{info.companionSetting.followDistance} / " +
                $"リーシュ{info.companionSetting.maxLeashDistance}",
                MessageType.Info);

            EditorGUILayout.Space(4);
            base.OnInspectorGUI();

            // バリデーション
            EditorGUILayout.Space(8);
            ValidateAIInfo(info);
        }

        private void ValidateAIInfo(AIInfo info)
        {
            if (info.modes == null || info.modes.Length == 0)
            {
                EditorGUILayout.HelpBox("モードが未設定です。最低1つのモードが必要です。", MessageType.Error);
                return;
            }

            for (int i = 0; i < info.modes.Length; i++)
            {
                CharacterModeData mode = info.modes[i];

                if (mode.detectionRange <= 0)
                {
                    EditorGUILayout.HelpBox($"モード[{i}] {mode.mode}: detectionRange が0以下です。", MessageType.Warning);
                }

                if (mode.availableActIndices != null)
                {
                    foreach (int actIndex in mode.availableActIndices)
                    {
                        if (info.actDataList == null || actIndex < 0 || actIndex >= info.actDataList.Length)
                        {
                            EditorGUILayout.HelpBox(
                                $"モード[{i}] {mode.mode}: actIndex {actIndex} が範囲外です（行動数: {(info.actDataList != null ? info.actDataList.Length : 0)}）。",
                                MessageType.Error);
                        }
                    }
                }
            }

            if (info.actDataList != null)
            {
                for (int i = 0; i < info.actDataList.Length; i++)
                {
                    ActData act = info.actDataList[i];
                    if (string.IsNullOrEmpty(act.actName))
                    {
                        EditorGUILayout.HelpBox($"行動[{i}]: actName が未設定です。", MessageType.Warning);
                    }
                    if (act.weight <= 0)
                    {
                        EditorGUILayout.HelpBox($"行動[{i}] {act.actName}: weight が0以下です。", MessageType.Warning);
                    }
                }
            }
        }
    }
}
#endif
