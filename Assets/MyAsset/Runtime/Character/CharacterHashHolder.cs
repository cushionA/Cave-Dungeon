using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// GameObjectにキャラクターハッシュを保持するコンポーネント。
    /// SensorToolkitがGameObject単位で検出するため、ハッシュとの紐付けに使用。
    /// </summary>
    public class CharacterHashHolder : MonoBehaviour
    {
        [SerializeField] private int _hash;
        public int Hash => _hash;

        public void SetHash(int hash)
        {
            _hash = hash;
        }
    }
}
