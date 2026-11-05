using System;

namespace Game.Core
{

    /// <summary>
    /// ボスの1フェーズ分のデータ定義。BossDefinitionが配列で保持する。
    /// </summary>
    [Serializable]
    public struct BossPhaseData
    {
        public string phaseName;               // "第1形態" 等
        public AIMode[] modes;                 // このフェーズのAIモード配列
        public PhaseCondition exitCondition;   // このフェーズから次への遷移条件
        public float transitionInvincibleTime; // 遷移時無敵秒数
        public bool spawnAdds;                 // 雑魚召喚フラグ
        public string[] addSpawnerIds;         // 召喚する雑魚のスポナーID
    }
}
