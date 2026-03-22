using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Core;
using PixelCrushers.LoveHate;

namespace Game.Runtime
{
    /// <summary>
    /// Love/HateとGameManagerを接続するブリッジ。
    /// C# standard eventで購読（R3不要）。
    /// </summary>
    public class LoveHateBridge : MonoBehaviour
    {
        [Header("Deed Templates")]
        [SerializeField] private string _attackDeedTag = "Attack";
        [SerializeField] private string _helpDeedTag = "Help";
        [SerializeField] private string _healDeedTag = "Heal";

        private FactionManager _factionManager;

        // hash → GameObject キャッシュ（FindObjectsByType毎回呼び出し回避）
        private static Dictionary<int, CharacterHashHolder> _holderCache = new Dictionary<int, CharacterHashHolder>();
        private static Dictionary<int, DeedReporter> _reporterCache = new Dictionary<int, DeedReporter>();
        private static bool _sceneCallbackRegistered;

        private void Awake()
        {
            _factionManager = FindAnyObjectByType<FactionManager>();
        }

        private void OnEnable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnDamageDealtEvent += OnDamageDealt;
            if (!_sceneCallbackRegistered)
            {
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _sceneCallbackRegistered = true;
            }
        }

        private void OnDisable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnDamageDealtEvent -= OnDamageDealt;
            // SceneManager.sceneUnloadedはstaticコールバックのため、
            // 最後のインスタンス破棄時のみ解除
            // （複数インスタンスからの重複購読を_sceneCallbackRegisteredで防止済み）
        }

        /// <summary>
        /// CharacterHashHolderの登録。BaseCharacterなどから呼ぶ。
        /// </summary>
        public static void RegisterHolder(CharacterHashHolder holder)
        {
            _holderCache[holder.Hash] = holder;
        }

        /// <summary>
        /// CharacterHashHolderの登録解除。
        /// </summary>
        public static void UnregisterHolder(CharacterHashHolder holder)
        {
            _holderCache.Remove(holder.Hash);
        }

        /// <summary>
        /// キャッシュをクリアする（シーン遷移時など）。
        /// </summary>
        public static void ClearCache()
        {
            _holderCache.Clear();
            _reporterCache.Clear();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            ClearCache();
        }

        private void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            ReportDeedInternal(attackerHash, defenderHash, _attackDeedTag);
        }

        public void ReportHeal(int healerHash, int targetHash)
        {
            ReportDeedInternal(healerHash, targetHash, _healDeedTag);
        }

        public void ReportHelp(int helperHash, int targetHash)
        {
            ReportDeedInternal(helperHash, targetHash, _helpDeedTag);
        }

        private void ReportDeedInternal(int actorHash, int targetHash, string deedTag)
        {
            if (_factionManager == null)
            {
                return;
            }

            GameObject actorGo = FindGameObjectByHash(actorHash);
            FactionMember target = FindFactionMember(targetHash);

            if (actorGo != null && target != null)
            {
                // DeedReporterもholderCacheと同様にキャッシュ
                    if (!_reporterCache.TryGetValue(actorHash, out DeedReporter reporter))
                    {
                        reporter = actorGo.GetComponent<DeedReporter>();
                        if (reporter != null)
                        {
                            _reporterCache[actorHash] = reporter;
                        }
                    }
                    if (reporter != null)
                    {
                        reporter.ReportDeed(deedTag, target);
                    }
            }
        }

        public float GetAffinity(int fromHash, int toHash)
        {
            FactionMember from = FindFactionMember(fromHash);
            FactionMember to = FindFactionMember(toHash);

            if (from == null || to == null)
            {
                return 0f;
            }

            return from.GetAffinity(to.factionID);
        }

        public void SetAffinity(int fromHash, int toHash, float value)
        {
            FactionMember from = FindFactionMember(fromHash);
            FactionMember to = FindFactionMember(toHash);

            if (from != null && to != null && _factionManager != null)
            {
                _factionManager.SetPersonalAffinity(from.factionID, to.factionID, value);
            }
        }

        private FactionMember FindFactionMember(int characterHash)
        {
            GameObject go = FindGameObjectByHash(characterHash);
            if (go != null)
            {
                return go.GetComponent<FactionMember>();
            }

            return null;
        }

        private static GameObject FindGameObjectByHash(int characterHash)
        {
            if (_holderCache.TryGetValue(characterHash, out CharacterHashHolder holder) && holder != null)
            {
                return holder.gameObject;
            }

            // キャッシュミス時のフォールバック
            CharacterHashHolder[] holders = FindObjectsByType<CharacterHashHolder>(FindObjectsSortMode.None);
            for (int i = 0; i < holders.Length; i++)
            {
                _holderCache[holders[i].Hash] = holders[i];
                if (holders[i].Hash == characterHash)
                {
                    return holders[i].gameObject;
                }
            }

            return null;
        }
    }
}
