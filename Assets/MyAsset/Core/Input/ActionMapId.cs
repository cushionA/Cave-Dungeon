namespace Game.Core
{
    /// <summary>
    /// アクションマップ切替用ID。
    /// Gameplay: 通常プレイ中、UI: メニュー操作中、Cutscene: カットシーン再生中。
    /// </summary>
    public enum ActionMapId : byte
    {
        Gameplay,
        UI,
        Cutscene,
    }
}
