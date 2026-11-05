using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct AIMode
    {
        public string modeName;

        /// <summary>
        /// モード単体プリセットへの参照ID（GUID）。
        /// ModePresetRegistry で管理され、カスケード更新の対象を特定するのに使う。
        /// 空文字列/null の場合はプリセットに紐付かない独立モード。
        /// </summary>
        public string modeId;

        public AIRule[] targetRules;
        public AIRule[] actionRules;
        public AITargetSelect[] targetSelects;
        public ActionSlot[] actions;
        public int defaultActionIndex;
        public UnityEngine.Vector2 judgeInterval;
    }
}
