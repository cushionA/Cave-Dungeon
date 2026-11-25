using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>3Dの操作イベントから所持品に受け取れる分だけ加える。</summary>
    [DisallowMultipleComponent]
    public sealed class ConsumablePickup3D : MonoBehaviour
    {
        [SerializeField] private ConsumableDefinition item;
        [SerializeField, Min(1)] private int count = 1;
        private int _remaining;

        private void Awake() => _remaining = count;

        public void Collect()
        {
            if (!isActiveAndEnabled || item == null || _remaining <= 0 || item.ItemId < 0 || item.MaxStack <= 0) { return; }
            InventoryManager inventory = GameManager.Inventory;
            if (inventory == null) { return; }
            _remaining -= inventory.Add(item.ItemId, ItemCategory.Consumable, _remaining, item.MaxStack);
            if (_remaining == 0) { gameObject.SetActive(false); }
        }
    }
}
