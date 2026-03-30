using System;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// 敵タイプIDとプレハブのマッピング。
    /// </summary>
    [Serializable]
    public struct EnemyTypeEntry
    {
        public int enemyTypeId;
        public GameObject prefab;
    }
}
