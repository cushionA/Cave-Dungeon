using UnityEngine;
using Game.Core;
using Micosmo.SensorToolkit;

namespace Game.Runtime
{
    /// <summary>
    /// SensorToolkit 2とゲームのAI SensorSystemを接続するブリッジ。
    /// 2Dゲーム用にRangeSensor2D/RaySensor2Dを使用。
    /// </summary>
    [RequireComponent(typeof(RangeSensor2D))]
    public class SensorToolkitBridge : MonoBehaviour
    {
        [Header("Sensor References")]
        [SerializeField] private RangeSensor2D _detectionSensor;
        [SerializeField] private RaySensor2D _losSensor;

        [Header("Game Settings")]
        [SerializeField] private int _ownerHash;

        private SensorSystem _sensorSystem;

        /// <summary>
        /// ゲームAIのSensorSystemと接続する。
        /// </summary>
        public void Initialize(int ownerHash, SensorSystem sensorSystem)
        {
            _ownerHash = ownerHash;
            _sensorSystem = sensorSystem;

            if (_detectionSensor == null)
            {
                _detectionSensor = GetComponent<RangeSensor2D>();
            }
        }

        /// <summary>
        /// SensorToolkitの検出結果をゲーム用ハッシュリストに変換する。
        /// </summary>
        public int[] GetDetectedHashes()
        {
            if (_detectionSensor == null)
            {
                return System.Array.Empty<int>();
            }

            System.Collections.Generic.List<GameObject> detected = _detectionSensor.GetDetections();
            int[] hashes = new int[detected.Count];

            for (int i = 0; i < detected.Count; i++)
            {
                CharacterHashHolder holder = detected[i].GetComponent<CharacterHashHolder>();
                if (holder != null)
                {
                    hashes[i] = holder.Hash;
                }
            }

            return hashes;
        }

        /// <summary>
        /// ターゲットへの視線が通っているか（Line of Sight）。
        /// </summary>
        public bool HasLineOfSight(GameObject target)
        {
            if (_losSensor == null)
            {
                return false;
            }

            return _losSensor.IsDetected(target);
        }

        /// <summary>
        /// 最も近い検出対象を取得する。
        /// </summary>
        public GameObject GetNearestDetected()
        {
            if (_detectionSensor == null)
            {
                return null;
            }

            return _detectionSensor.GetNearestDetection();
        }
    }

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
