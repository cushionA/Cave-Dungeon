using UnityEngine;
using Game.Core;
using PixelCrushers.LoveHate;

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
            FactionMember attacker = FindFactionMember(attackerHash);
            FactionMember defender = FindFactionMember(defenderHash);

            if (attacker != null && defender != null)
            {
                attacker.ReportDeed(_attackDeedTag, defender, _attackImpact);
            }
        }

        /// <summary>
        /// 回復行為を好感度に反映する。
        /// </summary>
        public void ReportHeal(int healerHash, int targetHash)
        {
            FactionMember healer = FindFactionMember(healerHash);
            FactionMember target = FindFactionMember(targetHash);

            if (healer != null && target != null)
            {
                healer.ReportDeed(_healDeedTag, target, _healImpact);
            }
        }

        /// <summary>
        /// 援助行為を好感度に反映する。
        /// </summary>
        public void ReportHelp(int helperHash, int targetHash)
        {
            FactionMember helper = FindFactionMember(helperHash);
            FactionMember target = FindFactionMember(targetHash);

            if (helper != null && target != null)
            {
                helper.ReportDeed(_helpDeedTag, target, _helpImpact);
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

            return from.GetAffinity(to);
        }

        /// <summary>
        /// 陣営の好感度を直接設定する（イベントシーン等で使用）。
        /// </summary>
        public void SetAffinity(int fromHash, int toHash, float value)
        {
            FactionMember from = FindFactionMember(fromHash);
            FactionMember to = FindFactionMember(toHash);

            if (from != null && to != null)
            {
                from.SetPersonalAffinity(to, value);
            }
        }

        private FactionMember FindFactionMember(int characterHash)
        {
            // CharacterRegistryからGameObjectを探し、FactionMemberを取得
            // 本実装ではMonoBehaviour統合レイヤーで紐付ける
            // 暫定: FindでFactionMember付きオブジェクトを検索
            FactionMember[] members = FindObjectsByType<FactionMember>(FindObjectsSortMode.None);
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].gameObject.GetHashCode() == characterHash)
                {
                    return members[i];
                }
            }

            return null;
        }
    }
}
