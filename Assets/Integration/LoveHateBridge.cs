using UnityEngine;
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
        }

        private void OnDisable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnDamageDealtEvent -= OnDamageDealt;
        }

        private void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            if (_factionManager == null)
            {
                return;
            }

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

        private GameObject FindGameObjectByHash(int characterHash)
        {
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
