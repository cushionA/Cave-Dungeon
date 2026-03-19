using UnityEngine;
using Game.Core;
using DarkTonic.MasterAudio;

namespace Game.Runtime
{
    /// <summary>
    /// Master Audio 2024とGameManagerのイベントシステムを接続するブリッジ。
    /// C# standard eventで購読（R3不要）。
    /// </summary>
    public class MasterAudioBridge : MonoBehaviour
    {
        [Header("Sound Group Names")]
        [SerializeField] private string _attackHitGroup = "SFX_AttackHit";
        [SerializeField] private string _criticalHitGroup = "SFX_CriticalHit";
        [SerializeField] private string _guardGroup = "SFX_Guard";
        [SerializeField] private string _parryGroup = "SFX_Parry";
        [SerializeField] private string _healGroup = "SFX_Heal";
        [SerializeField] private string _deathGroup = "SFX_Death";
        [SerializeField] private string _levelUpGroup = "SFX_LevelUp";

        private void OnEnable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnDamageDealtEvent += OnDamageDealt;
            GameManager.Events.OnCharacterDeathEvent += OnCharacterDeath;
        }

        private void OnDisable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnDamageDealtEvent -= OnDamageDealt;
            GameManager.Events.OnCharacterDeathEvent -= OnCharacterDeath;
        }

        private void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            if (result.guardResult == GuardResult.JustGuard)
            {
                PlaySE(_parryGroup);
                return;
            }

            if (result.guardResult == GuardResult.Guarded)
            {
                PlaySE(_guardGroup);
                return;
            }

            if (result.isCritical)
            {
                PlaySE(_criticalHitGroup);
            }
            else if (result.totalDamage > 0)
            {
                PlaySE(_attackHitGroup);
            }
        }

        private void OnCharacterDeath(int deadHash, int killerHash)
        {
            PlaySE(_deathGroup);
        }

        public static void PlaySE(string soundGroupName)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                return;
            }

            MasterAudio.PlaySoundAndForget(soundGroupName);
        }

        public static void PlayBGM(string playlistName)
        {
            if (string.IsNullOrEmpty(playlistName))
            {
                return;
            }

            MasterAudio.StartPlaylist(playlistName);
        }

        public static void StopBGM()
        {
            MasterAudio.StopPlaylist();
        }

        public static void PlaySEAtPosition(string soundGroupName, Vector3 position)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                return;
            }

            MasterAudio.PlaySound3DAtVector3AndForget(soundGroupName, position);
        }

        public void PlayHealSE()
        {
            PlaySE(_healGroup);
        }

        public void PlayLevelUpSE()
        {
            PlaySE(_levelUpGroup);
        }
    }
}
