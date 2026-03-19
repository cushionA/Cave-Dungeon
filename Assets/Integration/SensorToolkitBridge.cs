using UnityEngine;
using Game.Core;
using SensorToolkit;

namespace Game.Runtime
{
    /// <summary>
    /// SensorToolkit 2とゲームのAI SensorSystemを接続するブリッジ。
    /// SensorToolkitの物理ベースセンサーをAI判定のソースとして利用する。
    /// 自前SensorSystemのvision/hearing判定をSensorToolkitのRange/RaySensorで代替可能。
    /// </summary>
    [RequireComponent(typeof(RangeSensor))]
    public class SensorToolkitBridge : MonoBehaviour
    {
        [Header("Sensor References")]
        [SerializeField] private RangeSensor _detectionSensor;
        [SerializeField] private RaySensor _losSensor;

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
                _detectionSensor = GetComponent<RangeSensor>();
            }
        }

        /// <summary>
        /// SensorToolkitの検出結果をゲーム用ハッシュリストに変換する。
        /// AIのJudgmentLoopから呼ばれる。
        /// </summary>
        public int[] GetDetectedHashes()
        {
            if (_detectionSensor == null)
            {
                return System.Array.Empty<int>();
            }

            System.Collections.Generic.List<GameObject> detected = _detectionSensor.Detections;
            int[] hashes = new int[detected.Count];

            for (int i = 0; i < detected.Count; i++)
            {
                // CharacterHashコンポーネントからハッシュを取得
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
            if (_detectionSensor == null || _detectionSensor.Detections.Count == 0)
            {
                return null;
            }

            return _detectionSensor.GetNearest();
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
