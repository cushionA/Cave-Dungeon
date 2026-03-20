#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Game.Core;

namespace Game.Editor
{
    [CustomEditor(typeof(ChallengeDefinition))]
    public class ChallengeDefinitionEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            ChallengeDefinition def = (ChallengeDefinition)target;

            // タイトル
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("チャレンジ定義", titleStyle);
            EditorGUILayout.Space(4);

            // サマリー
            string typeName = def.ChallengeTypeValue.ToString();
            int bossCount = def.BossIds != null ? def.BossIds.Length : 0;
            EditorGUILayout.HelpBox(
                $"【{def.ChallengeName}】 タイプ: {typeName}\n" +
                $"制限時間: {def.TimeLimit}s / 最大デス: {def.MaxDeathCount}\n" +
                $"ボス数: {bossCount} / ウェーブ: {def.WaveCount}（{def.EnemiesPerWave}体/wave）\n" +
                $"報酬: {def.CurrencyReward}G" +
                (string.IsNullOrEmpty(def.ItemRewardId) ? "" : $" + {def.ItemRewardId}"),
                MessageType.Info);

            EditorGUILayout.Space(4);
            base.OnInspectorGUI();

            // バリデーション
            EditorGUILayout.Space(8);
            ValidateChallengeDefinition(def);
        }

        private void ValidateChallengeDefinition(ChallengeDefinition def)
        {
            if (string.IsNullOrEmpty(def.ChallengeId))
            {
                EditorGUILayout.HelpBox("challengeId が未設定です。", MessageType.Error);
            }

            if (string.IsNullOrEmpty(def.ChallengeName))
            {
                EditorGUILayout.HelpBox("challengeName が未設定です。", MessageType.Error);
            }

            if (def.ChallengeTypeValue == ChallengeType.BossRush)
            {
                if (def.BossIds == null || def.BossIds.Length == 0)
                {
                    EditorGUILayout.HelpBox("BossRush ですが bossIds が空です。", MessageType.Error);
                }
            }

            if (def.ChallengeTypeValue == ChallengeType.Survival)
            {
                if (def.WaveCount <= 0)
                {
                    EditorGUILayout.HelpBox("Survival ですが waveCount が0以下です。", MessageType.Error);
                }
                if (def.EnemiesPerWave <= 0)
                {
                    EditorGUILayout.HelpBox("Survival ですが enemiesPerWave が0以下です。", MessageType.Error);
                }
            }

            if (def.TimeLimit <= 0 && def.ChallengeTypeValue == ChallengeType.TimeAttack)
            {
                EditorGUILayout.HelpBox("TimeAttack ですが timeLimit が0以下です。", MessageType.Error);
            }

            // ランク閾値の整合性
            if (def.GoldTimeThreshold > 0 && def.SilverTimeThreshold > 0
                && def.GoldTimeThreshold >= def.SilverTimeThreshold)
            {
                EditorGUILayout.HelpBox(
                    "Gold タイム閾値が Silver 以上です（Gold < Silver であるべき）。",
                    MessageType.Warning);
            }

            if (def.PlatinumScoreThreshold > 0 && def.GoldScoreThreshold > 0
                && def.PlatinumScoreThreshold <= def.GoldScoreThreshold)
            {
                EditorGUILayout.HelpBox(
                    "Platinum スコア閾値が Gold 以下です（Platinum > Gold であるべき）。",
                    MessageType.Warning);
            }
        }
    }
}
#endif
