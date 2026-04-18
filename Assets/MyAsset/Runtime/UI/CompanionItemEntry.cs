using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// 仲間 AI のアイテム使用行動で選択可能なアイテムを表す UI 用プレースホルダー。
    /// 将来 ItemInfo ScriptableObject が定義されたら差し替える想定。
    /// ActionSlot にセットするときは paramId = (int)InstantAction.UseItem、paramValue = itemId として格納する。
    /// </summary>
    [System.Serializable]
    public class CompanionItemEntry
    {
        [Tooltip("ゲーム内アイテムID（将来のアイテムテーブルのインデックス想定）")]
        public int itemId;

        [Tooltip("UI 上の表示名（例: HP回復薬、MPポーション）")]
        public string itemName;

        [Tooltip("ツールチップで表示する説明文")]
        [TextArea(1, 3)]
        public string description;

        [Tooltip("任意: UI アイコン")]
        public Sprite icon;
    }
}
