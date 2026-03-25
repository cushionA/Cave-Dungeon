using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Detects nearby characters using vision cone and hearing range.
    /// Vision: distance + angle check against facing direction.
    /// Hearing: distance-only check regardless of facing direction.
    /// Supports both O(n) linear scan and O(1) spatial hash query via SpatialTargetRegistry.
    /// </summary>
    public class SensorSystem
    {
        private const int k_MaxNeighborResults = 64;

        private float _visionRange;
        private float _visionAngle;
        private float _hearingRange;
        private float _visionRangeSq;
        private float _hearingRangeSq;

        private List<int> _detectedHashes;

        public IReadOnlyList<int> DetectedHashes => _detectedHashes;

        public SensorSystem(float visionRange, float visionAngle, float hearingRange)
        {
            _visionRange = visionRange;
            _visionAngle = visionAngle;
            _hearingRange = hearingRange;
            _visionRangeSq = visionRange * visionRange;
            _hearingRangeSq = hearingRange * hearingRange;
            _detectedHashes = new List<int>();
        }

        /// <summary>
        /// Updates detection by scanning all hashes against vision and hearing ranges.
        /// O(n) linear scan version. Results are stored in DetectedHashes.
        /// </summary>
        public void UpdateDetection(int ownerHash, List<int> allHashes,
            SoACharaDataDic data, Vector2 facingDirection)
        {
            _detectedHashes.Clear();

            if (!data.TryGetValue(ownerHash, out int _))
            {
                return;
            }

            ref CharacterVitals ownerVitals = ref data.GetVitals(ownerHash);
            Vector2 ownerPos = ownerVitals.position;

            for (int i = 0; i < allHashes.Count; i++)
            {
                int hash = allHashes[i];
                if (hash != ownerHash)
                {
                    EvaluateCandidate(hash, ownerPos, data, facingDirection);
                }
            }
        }

        /// <summary>
        /// Updates detection using SpatialTargetRegistry for O(1) neighbor lookup.
        /// Only nearby candidates are checked for vision/hearing, reducing cost from O(n) to O(k).
        /// </summary>
        public void UpdateDetection(int ownerHash, SpatialTargetRegistry registry,
            SoACharaDataDic data, Vector2 facingDirection)
        {
            _detectedHashes.Clear();

            if (!data.TryGetValue(ownerHash, out int _))
            {
                return;
            }

            ref CharacterVitals ownerVitals = ref data.GetVitals(ownerHash);
            Vector2 ownerPos = ownerVitals.position;

            float maxRange = Mathf.Max(_visionRange, _hearingRange);
            Span<int> neighbors = stackalloc int[k_MaxNeighborResults];
            int neighborCount = registry.QueryNeighbors(ownerPos, maxRange, neighbors);

#if UNITY_EDITOR
            if (neighborCount >= k_MaxNeighborResults)
            {
                Debug.LogWarning(
                    $"[SensorSystem] QueryNeighbors returned {neighborCount} results (max={k_MaxNeighborResults}). Some targets may be missed.");
            }
#endif

            for (int i = 0; i < neighborCount; i++)
            {
                int hash = neighbors[i];
                if (hash != ownerHash)
                {
                    EvaluateCandidate(hash, ownerPos, data, facingDirection);
                }
            }
        }

        /// <summary>
        /// Evaluates a single candidate against vision and hearing ranges.
        /// Uses sqrMagnitude to avoid sqrt where possible.
        /// </summary>
        private void EvaluateCandidate(int hash, Vector2 ownerPos,
            SoACharaDataDic data, Vector2 facingDirection)
        {
            if (!data.TryGetValue(hash, out int _))
            {
                return;
            }

            ref CharacterVitals targetVitals = ref data.GetVitals(hash);
            Vector2 toTarget = targetVitals.position - ownerPos;
            float sqrDist = toTarget.sqrMagnitude;

            if (IsInVisionSq(sqrDist, toTarget, facingDirection))
            {
                _detectedHashes.Add(hash);
                return;
            }

            if (sqrDist <= _hearingRangeSq)
            {
                _detectedHashes.Add(hash);
            }
        }

        /// <summary>
        /// Returns true if a target at the given distance and direction is within the vision cone.
        /// </summary>
        public bool IsInVision(float distance, Vector2 toTarget, Vector2 facingDirection)
        {
            return IsInVisionSq(distance * distance, toTarget, facingDirection);
        }

        /// <summary>
        /// Returns true if a target at the given squared distance and direction is within the vision cone.
        /// Uses sqrMagnitude to avoid sqrt.
        /// </summary>
        private bool IsInVisionSq(float sqrDist, Vector2 toTarget, Vector2 facingDirection)
        {
            if (sqrDist > _visionRangeSq)
            {
                return false;
            }

            if (sqrDist < 0.0001f)
            {
                return true;
            }

            float angle = Vector2.Angle(facingDirection, toTarget);
            return angle <= _visionAngle * 0.5f;
        }

        /// <summary>
        /// Returns true if a target at the given distance is within hearing range.
        /// </summary>
        public bool IsInHearingRange(float distance)
        {
            return distance <= _hearingRange;
        }
    }
}
