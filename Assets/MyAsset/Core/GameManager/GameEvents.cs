using System;

namespace Game.Core
{
    /// <summary>
    /// ゲーム全体のイベントハブ。
    /// 各システムはこのクラスのイベントを購読・発火する。
    /// </summary>
    public class GameEvents
    {
        // ===== キャラクター登録・削除 =====
        public event Action<int> OnCharacterRegistered;  // hash
        public event Action<int> OnCharacterRemoved;     // hash

        // ===== ゲーム状態 =====
        public event Action OnGamePaused;
        public event Action OnGameResumed;

        // ===== シーン =====
        public event Action<string> OnSceneLoadStarted;   // sceneName
        public event Action<string> OnSceneLoadCompleted;  // sceneName

        // ===== 戦闘 =====
        public event Action<DamageResult, int, int> OnDamageDealt;           // result, attackerHash, defenderHash
        public event Action<int, int> OnCharacterDeath;                       // deadHash, killerHash
        public event Action<int, int, GuardResult> OnGuardEvent;              // defenderHash, attackerHash, result
        public event Action<int, StatusEffectId> OnStatusEffectApplied;       // targetHash, effectId

        // ===== 成長・経済 =====
        public event Action<int, int> OnExpGained;                            // characterHash, amount
        public event Action<int, int> OnLevelUp;                              // characterHash, newLevel
        public event Action<int, int> OnCurrencyChanged;                      // amount, newTotal

        // ===== 装備・アビリティ =====
        public event Action<int, EquipSlot> OnEquipmentChanged;               // ownerHash, slot
        public event Action<int, AbilityFlag> OnAbilityFlagsChanged;          // ownerHash, newFlags

        // ===== ワールド =====
        public event Action<string, string> OnAreaTransition;                 // fromAreaId, toAreaId
        public event Action<string> OnSavePointUsed;                          // savePointId
        public event Action<int, string, int> OnItemAcquired;                 // characterHash, itemId, count

        // ===== Section 2: 敵・仲間・連携 =====
        public event Action<int, int> OnEnemyDefeated;                        // enemyHash, killerHash
        public event Action OnCustomRulesChanged;
        public event Action<int> OnCoopActivated;                             // companionHash
        public event Action<string> OnGateOpened;                             // gateId
        public event Action<int> OnCooldownReady;                             // actionId
        public event Action OnFreeCoopActivated;
        public event Action OnRest;

        // ===== Fire methods =====

        public void FireCharacterRegistered(int hash) => OnCharacterRegistered?.Invoke(hash);
        public void FireCharacterRemoved(int hash) => OnCharacterRemoved?.Invoke(hash);
        public void FireGamePaused() => OnGamePaused?.Invoke();
        public void FireGameResumed() => OnGameResumed?.Invoke();
        public void FireSceneLoadStarted(string sceneName) => OnSceneLoadStarted?.Invoke(sceneName);
        public void FireSceneLoadCompleted(string sceneName) => OnSceneLoadCompleted?.Invoke(sceneName);

        public void FireDamageDealt(DamageResult result, int attackerHash, int defenderHash)
            => OnDamageDealt?.Invoke(result, attackerHash, defenderHash);
        public void FireCharacterDeath(int deadHash, int killerHash)
            => OnCharacterDeath?.Invoke(deadHash, killerHash);
        public void FireGuardEvent(int defenderHash, int attackerHash, GuardResult result)
            => OnGuardEvent?.Invoke(defenderHash, attackerHash, result);
        public void FireStatusEffectApplied(int targetHash, StatusEffectId effectId)
            => OnStatusEffectApplied?.Invoke(targetHash, effectId);

        public void FireExpGained(int characterHash, int amount) => OnExpGained?.Invoke(characterHash, amount);
        public void FireLevelUp(int characterHash, int newLevel) => OnLevelUp?.Invoke(characterHash, newLevel);
        public void FireCurrencyChanged(int amount, int newTotal) => OnCurrencyChanged?.Invoke(amount, newTotal);

        public void FireEquipmentChanged(int ownerHash, EquipSlot slot) => OnEquipmentChanged?.Invoke(ownerHash, slot);
        public void FireAbilityFlagsChanged(int ownerHash, AbilityFlag newFlags)
            => OnAbilityFlagsChanged?.Invoke(ownerHash, newFlags);

        public void FireAreaTransition(string fromAreaId, string toAreaId) => OnAreaTransition?.Invoke(fromAreaId, toAreaId);
        public void FireSavePointUsed(string savePointId) => OnSavePointUsed?.Invoke(savePointId);
        public void FireItemAcquired(int characterHash, string itemId, int count)
            => OnItemAcquired?.Invoke(characterHash, itemId, count);

        public void FireEnemyDefeated(int enemyHash, int killerHash) => OnEnemyDefeated?.Invoke(enemyHash, killerHash);
        public void FireCustomRulesChanged() => OnCustomRulesChanged?.Invoke();
        public void FireCoopActivated(int companionHash) => OnCoopActivated?.Invoke(companionHash);
        public void FireGateOpened(string gateId) => OnGateOpened?.Invoke(gateId);
        public void FireCooldownReady(int actionId) => OnCooldownReady?.Invoke(actionId);
        public void FireFreeCoopActivated() => OnFreeCoopActivated?.Invoke();
        public void FireRest() => OnRest?.Invoke();

        public void Clear()
        {
            OnCharacterRegistered = null;
            OnCharacterRemoved = null;
            OnGamePaused = null;
            OnGameResumed = null;
            OnSceneLoadStarted = null;
            OnSceneLoadCompleted = null;
            OnDamageDealt = null;
            OnCharacterDeath = null;
            OnGuardEvent = null;
            OnStatusEffectApplied = null;
            OnExpGained = null;
            OnLevelUp = null;
            OnCurrencyChanged = null;
            OnEquipmentChanged = null;
            OnAbilityFlagsChanged = null;
            OnAreaTransition = null;
            OnSavePointUsed = null;
            OnItemAcquired = null;
            OnEnemyDefeated = null;
            OnCustomRulesChanged = null;
            OnCoopActivated = null;
            OnGateOpened = null;
            OnCooldownReady = null;
            OnFreeCoopActivated = null;
            OnRest = null;
        }
    }
}
