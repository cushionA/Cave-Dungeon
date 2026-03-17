using System;

namespace Game.Core
{
    /// <summary>
    /// ゲーム全体のイベントハブ。
    /// 各システムはこのクラスのイベントを購読・発火する。
    /// </summary>
    public class GameEvents
    {
        // キャラクター登録・削除
        public event Action<int> OnCharacterRegistered;  // hash
        public event Action<int> OnCharacterRemoved;     // hash

        // ゲーム状態
        public event Action OnGamePaused;
        public event Action OnGameResumed;

        // シーン
        public event Action<string> OnSceneLoadStarted;   // sceneName
        public event Action<string> OnSceneLoadCompleted;  // sceneName

        public void FireCharacterRegistered(int hash)
        {
            OnCharacterRegistered?.Invoke(hash);
        }

        public void FireCharacterRemoved(int hash)
        {
            OnCharacterRemoved?.Invoke(hash);
        }

        public void FireGamePaused()
        {
            OnGamePaused?.Invoke();
        }

        public void FireGameResumed()
        {
            OnGameResumed?.Invoke();
        }

        public void FireSceneLoadStarted(string sceneName)
        {
            OnSceneLoadStarted?.Invoke(sceneName);
        }

        public void FireSceneLoadCompleted(string sceneName)
        {
            OnSceneLoadCompleted?.Invoke(sceneName);
        }

        public void Clear()
        {
            OnCharacterRegistered = null;
            OnCharacterRemoved = null;
            OnGamePaused = null;
            OnGameResumed = null;
            OnSceneLoadStarted = null;
            OnSceneLoadCompleted = null;
        }
    }
}
