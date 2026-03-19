namespace Game.Core
{
    /// <summary>
    /// フェーズ遷移時の雑魚召喚判定ロジック。
    /// BossPhaseDataからスポナーIDを取得する。
    /// 実際のスポーンはEnemySpawner.Activate()で行う。
    /// </summary>
    public class BossAddSpawnLogic
    {
        private static readonly string[] k_EmptyArray = new string[0];

        /// <summary>
        /// 指定フェーズデータから雑魚召喚用のスポナーIDリストを取得する。
        /// spawnAdds=false または addSpawnerIds=null の場合は空配列を返す。
        /// </summary>
        public string[] GetSpawnersForPhase(BossPhaseData phaseData)
        {
            if (!phaseData.spawnAdds || phaseData.addSpawnerIds == null)
            {
                return k_EmptyArray;
            }

            return phaseData.addSpawnerIds;
        }
    }
}
