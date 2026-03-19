using UnityEngine;
using Game.Core;
using PixelCrushers.LoveHate;
using R3;

namespace Game.Runtime
{
    /// <summary>
    /// Love/HateとGameManagerを接続するブリッジ。
    /// ゲーム内の戦闘・会話イベントを好感度・陣営システムに反映する。
    /// </summary>
    public class LoveHateBridge : MonoBehaviour
    {
        [Header("Deed Templates")]
        [SerializeField] private string _attackDeedTag = "Attack";
        [SerializeField] private string _helpDeedTag = "Help";
        [SerializeField] private string _healDeedTag = "Heal";

        [Header("Impact Values")]
        [SerializeField] private float _attackImpact = -10f;
        [SerializeField] private float _helpImpact = 5f;
        [SerializeField] private float _healImpact = 15f;

        private FactionManager _factionManager;
        private System.IDisposable _damageSubscription;

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

            _damageSubscription = GameManager.Events.OnDamageDealt
                .Subscribe(e => OnDamageDealt(e.attackerHash, e.defenderHash, e.result));
        }

        private void OnDisable()
        {
            _damageSubscription?.Dispose();
            _damageSubscription = null;
        }

        private void OnDamageDealt(int attackerHash, int defenderHash, DamageResult result)
        {
            if (_factionManager == null)
            {
                return;
            }

            // 攻撃行為としてLove/Hateに記録
            // DeedReporterコンポーネント経由でReportDeedを呼ぶ
            GameObject attackerGo = FindGameObjectByHash(attackerHash);
            FactionMember defender = FindFactionMember(defenderHash);

            if (attackerGo != null && defender != null)
            {
                DeedReporter reporter = attackerGo.GetComponent<DeedReporter>();
                if (reporter != null)
                {
                    reporter.ReportDeed(_attackDeedTag, defender);
                }
            }
        }

        /// <summary>
        /// 回復行為を好感度に反映する。
        /// </summary>
        public void ReportHeal(int healerHash, int targetHash)
        {
            GameObject healerGo = FindGameObjectByHash(healerHash);
            FactionMember target = FindFactionMember(targetHash);

            if (healerGo != null && target != null)
            {
                DeedReporter reporter = healerGo.GetComponent<DeedReporter>();
                if (reporter != null)
                {
                    reporter.ReportDeed(_healDeedTag, target);
                }
            }
        }

        /// <summary>
        /// 援助行為を好感度に反映する。
        /// </summary>
        public void ReportHelp(int helperHash, int targetHash)
        {
            GameObject helperGo = FindGameObjectByHash(helperHash);
            FactionMember target = FindFactionMember(targetHash);

            if (helperGo != null && target != null)
            {
                DeedReporter reporter = helperGo.GetComponent<DeedReporter>();
                if (reporter != null)
                {
                    reporter.ReportDeed(_helpDeedTag, target);
                }
            }
        }

        /// <summary>
        /// 2キャラ間の好感度を取得する。
        /// </summary>
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

        /// <summary>
        /// 陣営の好感度を直接設定する（イベントシーン等で使用）。
        /// </summary>
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

        private GameObject FindGameObjectByHash(int characterHash)
        {
            // CharacterHashHolderコンポーネントでハッシュとGameObjectを紐付ける
            CharacterHashHolder[] holders = FindObjectsByType<CharacterHashHolder>(FindObjectsSortMode.None);
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i].Hash == characterHash)
                {
                    return holders[i].gameObject;
                }
            }

            return null;
        }
    }
}
