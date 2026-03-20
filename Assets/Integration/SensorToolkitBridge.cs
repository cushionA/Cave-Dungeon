using System.Collections.Generic;
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

        // 再利用バッファ（GCアロケーション回避）
        private List<int> _hashBuffer = new List<int>();

        /// <summary>
        /// ゲームAIのSensorSystemと接続する。
        /// </summary>
        public void Initialize(int ownerHash, SensorSystem sensorSystem)
        {
            _ownerHash = ownerHash;

            if (_detectionSensor == null)
            {
                _detectionSensor = GetComponent<RangeSensor2D>();
            }
        }

        /// <summary>
        /// SensorToolkitの検出結果をゲーム用ハッシュリストに変換する。
        /// 返却されたリストは次回呼び出しで再利用されるため、保持しないこと。
        /// </summary>
        public List<int> GetDetectedHashes()
        {
            _hashBuffer.Clear();

            if (_detectionSensor == null)
            {
                return _hashBuffer;
            }

            List<GameObject> detected = _detectionSensor.GetDetections();
            for (int i = 0; i < detected.Count; i++)
            {
                CharacterHashHolder holder = detected[i].GetComponent<CharacterHashHolder>();
                if (holder != null)
                {
                    _hashBuffer.Add(holder.Hash);
                }
            }

            return _hashBuffer;
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
}
