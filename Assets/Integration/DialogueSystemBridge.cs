using UnityEngine;
using Game.Core;
using PixelCrushers.DialogueSystem;

namespace Game.Runtime
{
    /// <summary>
    /// Pixel Crushers Dialogue Systemとゲームシステムのブリッジ。
    /// Dialogue Systemのイベントをゲーム内フラグ・ステートに接続する。
    /// Lua登録でゲームデータをDialogue Systemから参照可能にする。
    /// </summary>
    public class DialogueSystemBridge : MonoBehaviour
    {
        private void OnEnable()
        {
            // Lua関数登録: Dialogue Systemの会話条件からゲームデータを参照
            Lua.RegisterFunction("GetPlayerHP", this,
                SymbolExtensions.GetMethodInfo(() => GetPlayerHP()));
            Lua.RegisterFunction("GetPlayerMP", this,
                SymbolExtensions.GetMethodInfo(() => GetPlayerMP()));
            Lua.RegisterFunction("IsCharacterAlive", this,
                SymbolExtensions.GetMethodInfo(() => IsCharacterAlive(string.Empty)));
            Lua.RegisterFunction("GetFlagValue", this,
                SymbolExtensions.GetMethodInfo(() => GetFlagValue(string.Empty)));
            Lua.RegisterFunction("SetFlagValue", this,
                SymbolExtensions.GetMethodInfo(() => SetFlagValue(string.Empty, 0)));

            // 会話イベントコールバック
            DialogueManager.instance.conversationStarted += OnConversationStarted;
            DialogueManager.instance.conversationEnded += OnConversationEnded;
        }

        private void OnDisable()
        {
            // Lua関数解除
            Lua.UnregisterFunction("GetPlayerHP");
            Lua.UnregisterFunction("GetPlayerMP");
            Lua.UnregisterFunction("IsCharacterAlive");
            Lua.UnregisterFunction("GetFlagValue");
            Lua.UnregisterFunction("SetFlagValue");

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.conversationStarted -= OnConversationStarted;
                DialogueManager.instance.conversationEnded -= OnConversationEnded;
            }
        }

        private void OnConversationStarted(Transform actor)
        {
            // 会話中はゲームを一時停止
            Time.timeScale = 0f;
            AILogger.Log($"[Dialogue] Conversation started with {actor?.name}");
        }

        private void OnConversationEnded(Transform actor)
        {
            Time.timeScale = 1f;
            AILogger.Log($"[Dialogue] Conversation ended with {actor?.name}");
        }

        // === Lua関数 ===

        public double GetPlayerHP()
        {
            if (GameManager.Data == null)
            {
                return 0;
            }

            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.Data.TryGetValue(playerHash, out int _))
            {
                return 0;
            }

            return GameManager.Data.GetVitals(playerHash).currentHp;
        }

        public double GetPlayerMP()
        {
            if (GameManager.Data == null)
            {
                return 0;
            }

            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.Data.TryGetValue(playerHash, out int _))
            {
                return 0;
            }

            return GameManager.Data.GetVitals(playerHash).currentMp;
        }

        public bool IsCharacterAlive(string characterName)
        {
            if (GameManager.Data == null)
            {
                return false;
            }

            int hash = characterName.GetHashCode();
            if (!GameManager.Data.TryGetValue(hash, out int _))
            {
                return false;
            }

            return GameManager.Data.GetVitals(hash).currentHp > 0;
        }

        public double GetFlagValue(string flagName)
        {
            // フラグシステム実装時に接続
            return DialogueLua.GetVariable(flagName).asFloat;
        }

        public void SetFlagValue(string flagName, double value)
        {
            // Dialogue System側とゲーム側の両方に設定
            DialogueLua.SetVariable(flagName, value);
        }
    }
}
