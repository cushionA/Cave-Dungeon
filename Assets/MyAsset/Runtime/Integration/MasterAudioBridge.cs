using UnityEngine;
using Game.Core;
using DarkTonic.MasterAudio;

namespace Game.Runtime
{
    /// <summary>
    /// Master Audio 2024とGameManagerのイベントシステムを接続するブリッジ。
    /// GameEventsのサウンド関連イベントを購読し、Master AudioのAPIを呼び出す。
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

        private System.IDisposable _damageSubscription;
        private System.IDisposable _deathSubscription;

        private void OnEnable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            _damageSubscription = GameManager.Events.OnDamageDealt
                .Subscribe(e => OnDamageDealt(e.result));

            _deathSubscription = GameManager.Events.OnCharacterDeath
                .Subscribe(e => OnCharacterDeath(e.characterHash));
        }

        private void OnDisable()
        {
            _damageSubscription?.Dispose();
            _damageSubscription = null;
            _deathSubscription?.Dispose();
            _deathSubscription = null;
        }

        private void OnDamageDealt(DamageResult result)
        {
            if (result.wasParried)
            {
                PlaySE(_parryGroup);
                return;
            }

            if (result.wasGuarded)
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

        private void OnCharacterDeath(int characterHash)
        {
            PlaySE(_deathGroup);
        }

        /// <summary>
        /// SE再生。Master AudioのPlaySoundを呼ぶ。
        /// </summary>
        public static void PlaySE(string soundGroupName)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                return;
            }

            MasterAudio.PlaySoundAndForget(soundGroupName);
        }

        /// <summary>
        /// BGM再生。Master AudioのPlaylistを制御する。
        /// </summary>
        public static void PlayBGM(string playlistName)
        {
            if (string.IsNullOrEmpty(playlistName))
            {
                return;
            }

            MasterAudio.StartPlaylist(playlistName);
        }

        /// <summary>
        /// BGM停止。
        /// </summary>
        public static void StopBGM()
        {
            MasterAudio.StopPlaylist();
        }

        /// <summary>
        /// 3D位置でSE再生。
        /// </summary>
        public static void PlaySEAtPosition(string soundGroupName, Vector3 position)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                return;
            }

            MasterAudio.PlaySound3DAtVector3AndForget(soundGroupName, position);
        }

        /// <summary>
        /// 回復SE再生。
        /// </summary>
        public void PlayHealSE()
        {
            PlaySE(_healGroup);
        }

        /// <summary>
        /// レベルアップSE再生。
        /// </summary>
        public void PlayLevelUpSE()
        {
            PlaySE(_levelUpGroup);
        }
    }
}
