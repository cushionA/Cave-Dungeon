using System;
using Com.LuisPedroFonseca.ProCamera2D;
using Game.Core;
using R3;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>
    /// <see cref="GameEvents.OnAreaTransition"/> を購読し、
    /// 同シーン上の <see cref="ProCamera2DRooms"/> に対して toAreaId と一致する Room ID で
    /// <c>EnterRoom</c> を呼び出す動的 Bounds 切替アダプタ。
    /// </summary>
    /// <remarks>
    /// 配置先: ProCamera2DRooms と同じ GameObject (Inspector で <see cref="_rooms"/> をアサイン)。
    ///
    /// Room ID 命名規則:
    /// <list type="bullet">
    ///   <item>Room.ID = エリアシーン名 (例: <c>Stage1_1</c>) で <see cref="LevelStreamingOrchestrator"/> の sceneName と揃える</item>
    ///   <item>該当 ID の Room が登録されていなければ no-op (LastAreaTransitionWasNoop=true)。
    ///         <see cref="ProCamera2DRooms.EnterRoom(string,bool,bool)"/> は未登録 ID で例外を投げるため
    ///         事前に <see cref="ProCamera2DRooms.GetRoom"/> で存在確認してから呼ぶ</item>
    /// </list>
    ///
    /// 設計判断:
    /// <list type="bullet">
    ///   <item>ProCamera2DRooms 未割当でも例外を出さない (テスト・最小シーン用)</item>
    ///   <item>R3 購読は <see cref="OnEnable"/> / <see cref="OnDisable"/> で対称管理</item>
    /// </list>
    /// </remarks>
    public class ProCamera2DRoomsAdapter : MonoBehaviour
    {
        [SerializeField] private ProCamera2DRooms _rooms;

        private IDisposable _subscription;

        /// <summary>
        /// 直近の <see cref="HandleAreaTransition"/> が ProCamera2DRooms 未割当 / 空 ID /
        /// 未登録 ID で no-op に終わったか。テスト・デバッグ向けのフラグ。
        /// </summary>
        public bool LastAreaTransitionWasNoop { get; private set; }

        private void OnEnable()
        {
            if (GameManager.Events != null)
            {
                _subscription = GameManager.Events.OnAreaTransition
                    .Subscribe(transition => HandleAreaTransition(transition.toAreaId));
            }
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>テスト専用: <see cref="LastAreaTransitionWasNoop"/> を初期状態に戻す。</summary>
        public void ResetLastTransitionFlagForTest()
        {
            LastAreaTransitionWasNoop = false;
        }

        private void HandleAreaTransition(string toAreaId)
        {
            if (_rooms == null)
            {
                LastAreaTransitionWasNoop = true;
                return;
            }

            if (string.IsNullOrEmpty(toAreaId))
            {
                LastAreaTransitionWasNoop = true;
                return;
            }

            Room found = _rooms.GetRoom(toAreaId);
            if (found == null)
            {
                LastAreaTransitionWasNoop = true;
                return;
            }

            _rooms.EnterRoom(toAreaId);
            LastAreaTransitionWasNoop = false;
        }
    }
}
