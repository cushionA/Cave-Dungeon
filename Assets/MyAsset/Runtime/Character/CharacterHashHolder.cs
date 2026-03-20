using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// GameObjectにキャラクターハッシュを保持するコンポーネント。
    /// SensorToolkitがGameObject単位で検出するため、ハッシュとの紐付けに使用。
    /// LoveHateBridgeのキャッシュにも自動登録する。
    /// </summary>
    public class CharacterHashHolder : MonoBehaviour
    {
        [SerializeField] private int _hash;
        public int Hash => _hash;

        public void SetHash(int hash)
        {
            if (_hash != 0)
            {
                LoveHateBridge.UnregisterHolder(this);
            }
            _hash = hash;
            if (_hash != 0)
            {
                LoveHateBridge.RegisterHolder(this);
            }
        }

        private void OnEnable()
        {
            if (_hash != 0)
            {
                LoveHateBridge.RegisterHolder(this);
            }
        }

        private void OnDisable()
        {
            LoveHateBridge.UnregisterHolder(this);
        }
    }
}
