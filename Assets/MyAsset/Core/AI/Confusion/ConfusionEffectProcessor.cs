using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 混乱状態異常の蓄積・発症・解除を管理する純ロジッククラス。
    /// StatusEffectManagerの蓄積モデルを参考に独自実装。
    /// </summary>
    public class ConfusionEffectProcessor
    {
        public const float k_DefaultThreshold = 100f;

        private readonly Dictionary<int, float> _accumulations;
        private readonly Dictionary<int, ConfusionState> _confusedEntities;

        public int ConfusedCount => _confusedEntities.Count;

        public event Action<int, int> OnConfusionApplied;   // (targetHash, controllerHash)
        public event Action<int> OnConfusionCleared;         // (targetHash)

        public ConfusionEffectProcessor()
        {
            _accumulations = new Dictionary<int, float>();
            _confusedEntities = new Dictionary<int, ConfusionState>();
        }

        /// <summary>
        /// 混乱蓄積値を加算する。耐性で軽減。閾値超過で発症。
        /// </summary>
        /// <param name="targetHash">対象キャラのハッシュ</param>
        /// <param name="value">蓄積量</param>
        /// <param name="resistance">耐性（0.0〜1.0）。1.0で完全耐性</param>
        /// <param name="controllerHash">混乱をかけたキャラのハッシュ</param>
        /// <param name="duration">発症時の持続時間</param>
        /// <returns>発症したらtrue</returns>
        public bool Accumulate(int targetHash, float value, float resistance, int controllerHash, float duration)
        {
            // 完全耐性チェック
            if (resistance >= 1.0f)
            {
                return false;
            }

            // 既に混乱中なら蓄積しない
            if (_confusedEntities.ContainsKey(targetHash))
            {
                return false;
            }

            float reducedValue = value * (1f - resistance);

            if (!_accumulations.ContainsKey(targetHash))
            {
                _accumulations[targetHash] = 0f;
            }

            _accumulations[targetHash] += reducedValue;

            if (_accumulations[targetHash] >= k_DefaultThreshold)
            {
                ApplyConfusion(targetHash, duration, controllerHash);
                _accumulations[targetHash] = 0f;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 混乱を直接適用する。蓄積を介さない場合に使用。
        /// </summary>
        public void ApplyConfusion(int targetHash, float duration, int controllerHash)
        {
            if (_confusedEntities.ContainsKey(targetHash))
            {
                return;
            }

            ConfusionState state = new ConfusionState
            {
                targetHash = targetHash,
                controllerHash = controllerHash,
                remainingDuration = duration,
                originalBelong = CharacterBelong.Enemy,
                accumulatedDamage = 0f
            };

            _confusedEntities[targetHash] = state;
            OnConfusionApplied?.Invoke(targetHash, controllerHash);
        }

        /// <summary>
        /// 混乱を解除する。
        /// </summary>
        public void ClearConfusion(int targetHash)
        {
            if (_confusedEntities.Remove(targetHash))
            {
                OnConfusionCleared?.Invoke(targetHash);
            }
        }

        /// <summary>
        /// 毎フレーム更新。残時間を減算し、期限切れを解除する。
        /// </summary>
        public void Tick(float deltaTime)
        {
            // Pass 1: 残時間を減算し、期限切れキーを収集
            List<int> expiredKeys = null;
            List<int> allKeys = new List<int>(_confusedEntities.Keys);

            for (int i = 0; i < allKeys.Count; i++)
            {
                int key = allKeys[i];
                ConfusionState state = _confusedEntities[key];
                state.remainingDuration -= deltaTime;
                _confusedEntities[key] = state;

                if (state.remainingDuration <= 0f)
                {
                    if (expiredKeys == null)
                    {
                        expiredKeys = new List<int>();
                    }
                    expiredKeys.Add(key);
                }
            }

            // Pass 2: 期限切れを解除
            if (expiredKeys != null)
            {
                for (int i = 0; i < expiredKeys.Count; i++)
                {
                    ClearConfusion(expiredKeys[i]);
                }
            }
        }

        /// <summary>
        /// 指定キャラが混乱中か判定する。
        /// </summary>
        public bool IsConfused(int hash)
        {
            return _confusedEntities.ContainsKey(hash);
        }

        /// <summary>
        /// 混乱中の敵をさらに追加可能か（最大同時数チェック）。
        /// </summary>
        public bool CanConfuseMore()
        {
            return _confusedEntities.Count < PartyManager.k_MaxConfusedEnemies;
        }

        /// <summary>
        /// 指定キャラの混乱状態を取得する。
        /// </summary>
        public bool TryGetConfusionState(int hash, out ConfusionState state)
        {
            return _confusedEntities.TryGetValue(hash, out state);
        }

        /// <summary>
        /// 混乱中のダメージを蓄積し、閾値超過で混乱解除する。
        /// </summary>
        public bool AccumulateDamageBreak(int targetHash, float damage, float breakThreshold)
        {
            if (!_confusedEntities.TryGetValue(targetHash, out ConfusionState state))
            {
                return false;
            }

            state.accumulatedDamage += damage;

            if (breakThreshold > 0f && state.accumulatedDamage >= breakThreshold)
            {
                ClearConfusion(targetHash);
                return true;
            }

            _confusedEntities[targetHash] = state;
            return false;
        }

        /// <summary>
        /// 全混乱をクリアする。
        /// </summary>
        public void ClearAll()
        {
            List<int> keys = new List<int>(_confusedEntities.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                ClearConfusion(keys[i]);
            }
            _accumulations.Clear();
        }
    }
}
