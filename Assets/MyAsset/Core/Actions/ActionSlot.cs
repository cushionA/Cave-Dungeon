using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct ActionSlot
    {
        public ActionExecType execType;
        /// <summary>攻撃/魔法の永続定義ID。空文字の旧データだけparamIdを配列indexとして解決する。</summary>
        public string actionDefinitionId;
        public int paramId;
        public float paramValue;
        public string displayName;
    }
}
