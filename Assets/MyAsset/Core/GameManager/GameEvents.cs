using System;
using R3;

namespace Game.Core
{
    /// <summary>
    /// ゲーム全体のイベントハブ。
    /// 各システムはこのクラスのObservableを購読し、Fireメソッドで発火する。
    /// R3 Subject<T>ベースでゼロアロケーション通知を実現。
    /// </summary>
    public class GameEvents : IDisposable
    {
        // ===== キャラクター登録・削除 =====
        private readonly Subject<int> _onCharacterRegistered = new();
        private readonly Subject<int> _onCharacterRemoved = new();
        public Observable<int> OnCharacterRegistered => _onCharacterRegistered;
        public Observable<int> OnCharacterRemoved => _onCharacterRemoved;

        // ===== ゲーム状態 =====
        private readonly Subject<Unit> _onGamePaused = new();
        private readonly Subject<Unit> _onGameResumed = new();
        public Observable<Unit> OnGamePaused => _onGamePaused;
        public Observable<Unit> OnGameResumed => _onGameResumed;

        // ===== シーン =====
        private readonly Subject<string> _onSceneLoadStarted = new();
        private readonly Subject<string> _onSceneLoadCompleted = new();
        public Observable<string> OnSceneLoadStarted => _onSceneLoadStarted;
        public Observable<string> OnSceneLoadCompleted => _onSceneLoadCompleted;

        // ===== 戦闘 =====
        private readonly Subject<(DamageResult result, int attackerHash, int defenderHash)> _onDamageDealt = new();
        private readonly Subject<(int deadHash, int killerHash)> _onCharacterDeath = new();
        private readonly Subject<(int defenderHash, int attackerHash, GuardResult result)> _onGuardEvent = new();
        private readonly Subject<(int targetHash, StatusEffectId effectId)> _onStatusEffectApplied = new();
        private readonly Subject<int> _onConfusionCleared = new();
        public Observable<(DamageResult result, int attackerHash, int defenderHash)> OnDamageDealt => _onDamageDealt;
        public Observable<(int deadHash, int killerHash)> OnCharacterDeath => _onCharacterDeath;
        public Observable<(int defenderHash, int attackerHash, GuardResult result)> OnGuardEvent => _onGuardEvent;
        public Observable<(int targetHash, StatusEffectId effectId)> OnStatusEffectApplied => _onStatusEffectApplied;
        /// <summary>
        /// 混乱状態が解除された時。AI が保持しているターゲットが無効化されるため、
        /// 受け手は JudgmentLoop.ForceEvaluate() でターゲット再選択を促すべき。
        /// </summary>
        public Observable<int> OnConfusionCleared => _onConfusionCleared;

        // ===== 成長・経済 =====
        private readonly Subject<(int characterHash, int amount)> _onExpGained = new();
        private readonly Subject<(int characterHash, int newLevel)> _onLevelUp = new();
        private readonly Subject<(int amount, int newTotal)> _onCurrencyChanged = new();
        public Observable<(int characterHash, int amount)> OnExpGained => _onExpGained;
        public Observable<(int characterHash, int newLevel)> OnLevelUp => _onLevelUp;
        public Observable<(int amount, int newTotal)> OnCurrencyChanged => _onCurrencyChanged;

        // ===== 装備・アビリティ =====
        private readonly Subject<(int ownerHash, EquipSlot slot)> _onEquipmentChanged = new();
        private readonly Subject<(int ownerHash, AbilityFlag newFlags)> _onAbilityFlagsChanged = new();
        public Observable<(int ownerHash, EquipSlot slot)> OnEquipmentChanged => _onEquipmentChanged;
        public Observable<(int ownerHash, AbilityFlag newFlags)> OnAbilityFlagsChanged => _onAbilityFlagsChanged;

        // ===== ワールド =====
        private readonly Subject<(string fromAreaId, string toAreaId)> _onAreaTransition = new();
        private readonly Subject<string> _onSavePointUsed = new();
        private readonly Subject<(int characterHash, string itemId, int count)> _onItemAcquired = new();
        public Observable<(string fromAreaId, string toAreaId)> OnAreaTransition => _onAreaTransition;
        public Observable<string> OnSavePointUsed => _onSavePointUsed;
        public Observable<(int characterHash, string itemId, int count)> OnItemAcquired => _onItemAcquired;

        // ===== Section 2: 敵・仲間・連携 =====
        private readonly Subject<(int enemyHash, int killerHash)> _onEnemyDefeated = new();
        private readonly Subject<Unit> _onCustomRulesChanged = new();
        private readonly Subject<int> _onCoopActivated = new();
        private readonly Subject<string> _onGateOpened = new();
        private readonly Subject<int> _onCooldownReady = new();
        private readonly Subject<Unit> _onFreeCoopActivated = new();
        private readonly Subject<Unit> _onRest = new();
        private readonly Subject<int> _onCompanionVanish = new();
        private readonly Subject<int> _onCompanionReturn = new();
        private readonly Subject<int> _onCompanionStanceChanged = new();
        public Observable<(int enemyHash, int killerHash)> OnEnemyDefeated => _onEnemyDefeated;
        public Observable<Unit> OnCustomRulesChanged => _onCustomRulesChanged;
        public Observable<int> OnCoopActivated => _onCoopActivated;
        public Observable<string> OnGateOpened => _onGateOpened;
        public Observable<int> OnCooldownReady => _onCooldownReady;
        public Observable<Unit> OnFreeCoopActivated => _onFreeCoopActivated;
        public Observable<Unit> OnRest => _onRest;
        public Observable<int> OnCompanionVanish => _onCompanionVanish;
        public Observable<int> OnCompanionReturn => _onCompanionReturn;
        public Observable<int> OnCompanionStanceChanged => _onCompanionStanceChanged;

        // ===== C# Standard Events (Integration層専用) =====
        // 使い分けルール:
        //   - Game.Core asmdef 内: R3 Observable を使用（OnDamageDealt, OnCharacterDeath等）
        //   - Integration層 (LoveHateBridge, MasterAudioBridge等): C# event を使用
        //     理由: Integration層はGame.Coreのasmdefに含まれず、R3依存を避けるため
        // 新規購読時は上記ルールに従うこと。
        public event Action<DamageResult, int, int> OnDamageDealtEvent;
        public event Action<int, int> OnCharacterDeathEvent;

        // ===== Fire methods =====

        public void FireCharacterRegistered(int hash) => _onCharacterRegistered.OnNext(hash);
        public void FireCharacterRemoved(int hash) => _onCharacterRemoved.OnNext(hash);
        public void FireGamePaused() => _onGamePaused.OnNext(Unit.Default);
        public void FireGameResumed() => _onGameResumed.OnNext(Unit.Default);
        public void FireSceneLoadStarted(string sceneName) => _onSceneLoadStarted.OnNext(sceneName);
        public void FireSceneLoadCompleted(string sceneName) => _onSceneLoadCompleted.OnNext(sceneName);

        public void FireDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            _onDamageDealt.OnNext((result, attackerHash, defenderHash));
            OnDamageDealtEvent?.Invoke(result, attackerHash, defenderHash);
        }
        public void FireCharacterDeath(int deadHash, int killerHash)
        {
            _onCharacterDeath.OnNext((deadHash, killerHash));
            OnCharacterDeathEvent?.Invoke(deadHash, killerHash);
        }
        public void FireGuardEvent(int defenderHash, int attackerHash, GuardResult result)
            => _onGuardEvent.OnNext((defenderHash, attackerHash, result));
        public void FireStatusEffectApplied(int targetHash, StatusEffectId effectId)
            => _onStatusEffectApplied.OnNext((targetHash, effectId));
        public void FireConfusionCleared(int targetHash)
            => _onConfusionCleared.OnNext(targetHash);

        public void FireExpGained(int characterHash, int amount) => _onExpGained.OnNext((characterHash, amount));
        public void FireLevelUp(int characterHash, int newLevel) => _onLevelUp.OnNext((characterHash, newLevel));
        public void FireCurrencyChanged(int amount, int newTotal) => _onCurrencyChanged.OnNext((amount, newTotal));

        public void FireEquipmentChanged(int ownerHash, EquipSlot slot) => _onEquipmentChanged.OnNext((ownerHash, slot));
        public void FireAbilityFlagsChanged(int ownerHash, AbilityFlag newFlags)
            => _onAbilityFlagsChanged.OnNext((ownerHash, newFlags));

        public void FireAreaTransition(string fromAreaId, string toAreaId) => _onAreaTransition.OnNext((fromAreaId, toAreaId));
        public void FireSavePointUsed(string savePointId) => _onSavePointUsed.OnNext(savePointId);
        public void FireItemAcquired(int characterHash, string itemId, int count)
            => _onItemAcquired.OnNext((characterHash, itemId, count));

        public void FireEnemyDefeated(int enemyHash, int killerHash) => _onEnemyDefeated.OnNext((enemyHash, killerHash));
        public void FireCustomRulesChanged() => _onCustomRulesChanged.OnNext(Unit.Default);
        public void FireCoopActivated(int companionHash) => _onCoopActivated.OnNext(companionHash);
        public void FireGateOpened(string gateId) => _onGateOpened.OnNext(gateId);
        public void FireCooldownReady(int actionId) => _onCooldownReady.OnNext(actionId);
        public void FireFreeCoopActivated() => _onFreeCoopActivated.OnNext(Unit.Default);
        public void FireRest() => _onRest.OnNext(Unit.Default);
        public void FireCompanionVanish(int companionHash) => _onCompanionVanish.OnNext(companionHash);
        public void FireCompanionReturn(int companionHash) => _onCompanionReturn.OnNext(companionHash);
        public void FireCompanionStanceChanged(int companionHash) => _onCompanionStanceChanged.OnNext(companionHash);

        public void Dispose()
        {
            _onCharacterRegistered.Dispose();
            _onCharacterRemoved.Dispose();
            _onGamePaused.Dispose();
            _onGameResumed.Dispose();
            _onSceneLoadStarted.Dispose();
            _onSceneLoadCompleted.Dispose();
            _onDamageDealt.Dispose();
            _onCharacterDeath.Dispose();
            _onGuardEvent.Dispose();
            _onStatusEffectApplied.Dispose();
            _onConfusionCleared.Dispose();
            _onExpGained.Dispose();
            _onLevelUp.Dispose();
            _onCurrencyChanged.Dispose();
            _onEquipmentChanged.Dispose();
            _onAbilityFlagsChanged.Dispose();
            _onAreaTransition.Dispose();
            _onSavePointUsed.Dispose();
            _onItemAcquired.Dispose();
            _onEnemyDefeated.Dispose();
            _onCustomRulesChanged.Dispose();
            _onCoopActivated.Dispose();
            _onGateOpened.Dispose();
            _onCooldownReady.Dispose();
            _onFreeCoopActivated.Dispose();
            _onRest.Dispose();
            _onCompanionVanish.Dispose();
            _onCompanionReturn.Dispose();
            _onCompanionStanceChanged.Dispose();
        }
    }
}
