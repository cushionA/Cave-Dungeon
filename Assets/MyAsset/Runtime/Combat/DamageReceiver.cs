using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// ダメージ受付MonoBehaviour。IDamageable実装。
    /// ActionEffectProcessor → ガード判定(スタミナ削り+連続JG窓) → DamageCalculator(属性別カット) → HpArmorLogic → SoA更新 → Events発火。
    /// </summary>
    public class DamageReceiver : MonoBehaviour, IDamageable
    {
        private BaseCharacter _character;
        private Rigidbody2D _rb;

        // 行動中断用の ActionExecutorController 参照 (任意)。
        // Task A: 行動アーマーが「削り切った瞬間」に現在の行動をキャンセルして Flinch 遷移させる。
        // テスト環境等で未設定の場合は CancelAction 呼び出しをスキップする。
        private ActionExecutorController _actionExecutorController;

        // 状態異常管理
        private StatusEffectManager _statusEffectManager;

        // ガード状態（外部から設定される）
        private bool _isGuarding;
        private float _guardTimeSinceStart;

        // 行動特殊効果（外部から設定される）
        private ActionEffect[] _currentActionEffects;
        private float _actionElapsedTime;

        // 行動アーマー消費量（被弾ごとに累積し、行動終了/切替時にリセット）。
        // ActionEffectProcessor.Evaluate は毎回フル値を返すため、
        // 過去被弾で削られた量をここで差し引いて正味の残量を算出する。
        //
        // ⚠ 前提: 1 行動内の ActionEffect.Armor は「単一の Armor 値」を想定している。
        //   ActionEffect が時間差で切り替わる場合（例: 0-1s Armor50 → 1-2s Armor30）、
        //   切替時点で累積消費量が新しい Effect にも繰り越されるため、残量計算が誤る可能性がある。
        //   Section 2 までの仕様では 1 行動 = 1 Armor Effect のみを採用しており、
        //   時間差で複数 Armor Effect を重ねる要件が出た際は
        //   切替タイミングで _actionArmorConsumed をリセットする仕組みの追加が必要。
        private float _actionArmorConsumed;

        // アーマー回復管理
        private float _armorRecoveryTimer;
        private float _armorRecoveryDelay;
        private float _armorRecoveryRate;
        private float _maxArmor;

        // スタン解除瞬間に currentArmor=maxArmor へ満タン復帰させるための前フレーム ActState 保持。
        // 対象スタン: Flinch / GuardBroken / Stunned / Knockbacked (HitReactionLogic.IsArmorResetStun)。
        // Flinch 中は ForceZeroArmorIfInFlinch で 0 強制、Knockbacked は armor 削り切りが前提条件、
        // GuardBroken はスタミナ削り切り後の無防備状態、Stunned は状態異常蓄積の気絶状態。
        // いずれも HitReactionLogic.ShouldResetArmorOnStunExit が true を返した瞬間に復帰させる。
        private ActState _previousActState;

        // 連続ジャストガード窓: 直前にJustGuardが成立した場合、
        // k_ContinuousJustGuardWindow 秒以内の次ガードは即ジャスガ扱いになる。
        private float _continuousJustGuardExpireTime = -1f;

        // 多段ヒット保護: 1 フレーム内で行動アーマーが削り切られた瞬間の検出後、
        // 同フレーム後続のヒットを skip する (Issue #78 M2)。
        // ActState=Flinch 書き込みは「次フレーム」判定にしか効かないため、同フレーム 2 発目が
        // ForceZeroArmorIfInFlinch 経由で raw damage 直撃するのを防ぐ。
        //
        // リセットタイミング: Update() 冒頭で false に reset する。Unity の 1 render frame は
        // [複数 FixedUpdate → 各々の後に OnTriggerEnter2D] → Update → LateUpdate の順で進むため、
        // 同 render frame 内で複数 FixedUpdate を跨いでも Update に到達するまで skip が継続する。
        // 次 render frame の最初の FixedUpdate より前に Update が reset を完了している保証はないが、
        // 次フレームでは ActState=Flinch が既に有効なため、Flinch 専用ロジック側で raw 直撃を防げる。
        private bool _armorBrokenThisFrame;

        // 状況ボーナス設定（外部から注入可能）
        private SituationalBonusConfig _bonusConfig = SituationalBonusConfig.Default;

        public int ObjectHash => _character != null ? _character.ObjectHash : 0;
        public bool IsAlive => _character != null && _character.IsAlive;

        private void Awake()
        {
            _character = GetComponent<BaseCharacter>();
            _rb = GetComponent<Rigidbody2D>();
            _actionExecutorController = GetComponent<ActionExecutorController>();
        }

        private void Start()
        {
            // テスト環境等で AddComponent の順序により Awake 時点で BaseCharacter がまだ
            // 追加されていなかった場合に備えて、Start で再取得する（本番プレハブでは既に取得済みで no-op）。
            if (_character == null)
            {
                _character = GetComponent<BaseCharacter>();
            }
            if (_actionExecutorController == null)
            {
                _actionExecutorController = GetComponent<ActionExecutorController>();
            }
        }

        /// <summary>
        /// テスト専用: ActionExecutorController を手動注入する。
        /// 本番は Awake の GetComponent で取得されるが、テストで GameObject に付ける前に
        /// DamageReceiver のみ単体で生成するケースに備える。
        /// </summary>
        public void SetActionExecutorControllerForTest(ActionExecutorController controller)
        {
            _actionExecutorController = controller;
        }

        /// <summary>
        /// ガード状態を設定する。GuardAbilityから呼ばれる。
        /// </summary>
        public void SetGuarding(bool isGuarding)
        {
            if (isGuarding && !_isGuarding)
            {
                _guardTimeSinceStart = 0f;
            }
            _isGuarding = isGuarding;
        }

        /// <summary>
        /// 現在の行動特殊効果を設定する。ActionExecutorから呼ばれる。
        /// 行動開始時はアーマー消費量もリセットする。
        /// </summary>
        public void SetActionEffects(ActionEffect[] effects)
        {
            _currentActionEffects = effects;
            _actionElapsedTime = 0f;
            _actionArmorConsumed = 0f;
        }

        /// <summary>
        /// 行動特殊効果をクリアする。行動終了時に呼ばれる。
        /// アーマー消費量も0に戻すことで、次の行動開始まで残骸が残らないようにする。
        /// </summary>
        public void ClearActionEffects()
        {
            _currentActionEffects = null;
            _actionElapsedTime = 0f;
            _actionArmorConsumed = 0f;
        }

        /// <summary>
        /// アーマー回復パラメータを設定する。初期化時にCharacterInfoから呼ばれる。
        /// </summary>
        public void SetArmorRecoveryParams(float maxArmor, float recoveryRate, float recoveryDelay)
        {
            _maxArmor = maxArmor;
            _armorRecoveryRate = recoveryRate;
            _armorRecoveryDelay = recoveryDelay;
        }

        /// <summary>
        /// プール再利用時に呼ばれる内部状態リセット。
        /// ガード経過時間/連続JG窓/行動特殊効果/アーマー回復タイマーなど
        /// 前キャラの残存状態が新キャラに漏れ込まないようクリアする。
        /// </summary>
        public void ResetInternalState()
        {
            _isGuarding = false;
            _guardTimeSinceStart = 0f;
            _currentActionEffects = null;
            _actionElapsedTime = 0f;
            _actionArmorConsumed = 0f;
            _armorRecoveryTimer = 0f;
            _continuousJustGuardExpireTime = -1f;
            _previousActState = ActState.Neutral;
        }

        /// <summary>
        /// 状況ダメージボーナス設定を注入する。
        /// </summary>
        public void SetBonusConfig(SituationalBonusConfig config)
        {
            _bonusConfig = config;
        }

        /// <summary>
        /// StatusEffectManagerを設定する。初期化時にCharacterSetupから呼ばれる。
        /// </summary>
        public void SetStatusEffectManager(StatusEffectManager manager)
        {
            _statusEffectManager = manager;
        }

        private void Update()
        {
            // フレーム境界で armor break ガードフラグをリセット (Issue #78 M2)。
            // Unity のライフサイクル順 (Physics → Update → LateUpdate) により、
            // 次フレームの OnTriggerEnter2D 経由 ReceiveDamage より先に false に戻る。
            _armorBrokenThisFrame = false;

            float dt = Time.deltaTime;

            if (_isGuarding)
            {
                _guardTimeSinceStart += dt;
            }

            if (_currentActionEffects != null)
            {
                _actionElapsedTime += dt;
            }

            // アーマー自然回復
            if (_armorRecoveryRate > 0f && _character != null && _character.IsAlive)
            {
                UpdateArmorRecovery(dt);
            }

            // スタン解除瞬間のアーマー満タンリセット (Flinch/GuardBroken/Stunned/Knockbacked)
            UpdateStunExitArmorReset();
        }

        /// <summary>
        /// 前フレームと現フレームの ActState を比較し、スタン状態から非スタン状態への遷移を
        /// 検出して currentArmor を maxArmor へ満タン復帰させる。
        /// 対象スタン: Flinch / GuardBroken / Stunned / Knockbacked (HitReactionLogic.IsArmorResetStun)。
        /// いずれも armor=0 が前提または強制された無防備状態のため、抜けた瞬間に再び armor 持ちへ復帰。
        /// スタン同士の遷移 (例: Flinch → Knockbacked) はリセットなし: 連続無防備として扱う。
        /// </summary>
        private void UpdateStunExitArmorReset()
        {
            if (_character == null)
            {
                return;
            }

            int hash = _character.ObjectHash;
            if (!GameManager.IsCharacterValid(hash))
            {
                return;
            }

            ActState currentActState = GameManager.Data.GetFlags(hash).ActState;
            if (HitReactionLogic.ShouldResetArmorOnStunExit(_previousActState, currentActState))
            {
                ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
                vitals.currentArmor = vitals.maxArmor;
            }

            _previousActState = currentActState;
        }

        private void UpdateArmorRecovery(float deltaTime)
        {
            if (_armorRecoveryTimer > 0f)
            {
                _armorRecoveryTimer -= deltaTime;
                return;
            }

            int hash = _character.ObjectHash;
            if (!GameManager.IsCharacterValid(hash))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            HpArmorLogic.RecoverArmor(ref vitals.currentArmor, _maxArmor,
                _armorRecoveryRate, deltaTime);
        }

        public DamageResult ReceiveDamage(DamageData data)
        {
            if (_character == null)
            {
                return default;
            }

            int hash = _character.ObjectHash;
            if (!GameManager.IsCharacterValid(hash))
            {
                return default;
            }

            // Issue #78 M2: 同フレーム内で行動アーマーを既に削り切っていれば、後続ヒットを skip する。
            // ActState=Flinch 書き込みは次フレームから効くため、同フレーム 2 発目を許すと
            // ForceZeroArmorIfInFlinch 経由で raw damage 直撃する隙ができる。
            // フラグは Update() 冒頭で reset されるため、次フレーム以降は通常 Flinch ロジックに戻る。
            if (_armorBrokenThisFrame)
            {
                return CreateInvincibleResult();
            }

            // Step 0: 行動特殊効果を評価
            ActionEffectProcessor.EffectState effectState =
                ActionEffectProcessor.Evaluate(_currentActionEffects, _actionElapsedTime);

            // Step 1: 無敵チェック（ActionEffect無敵 or 起き上がり無敵）
            ActState currentActState = GameManager.Data.GetFlags(hash).ActState;
            if (effectState.isInvincible || WakeUpLogic.IsWakeUpState(currentActState))
            {
                return CreateInvincibleResult();
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            ref CombatStats combat = ref GameManager.Data.GetCombatStats(hash);

            // Task B: Flinch 中は完全に無防備 → アーマーを強制的に 0 にする。
            bool wasInFlinch = ForceZeroArmorIfInFlinch(currentActState, ref effectState, ref vitals);

            // Step 2: ガード判定（連続JG窓・スタミナ削り判定を含む）
            bool isAttackFromFront = IsAttackFromFront(data);
            bool inContinuousJustGuardWindow = IsInContinuousJustGuardWindow();
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                _isGuarding,
                _guardTimeSinceStart,
                inContinuousJustGuardWindow,
                data.feature,
                combat.guardStats.guardDirection,
                isAttackFromFront,
                effectState.hasGuardAttackEffect,
                vitals.currentStamina,
                combat.guardStats.guardStrength,
                data.armorBreakValue);

            // Step 3: ジャストガード回復 + 連続JG窓更新
            ApplyJustGuardRecovery(guardResult, ref vitals);

            // Step 3.5: 通常ガード/ブレイクのスタミナ削りを適用
            ApplyGuardStaminaDrain(data, guardResult, combat, ref vitals);

            // Step 4: ダメージ計算（属性別カット + 状況ボーナス）
            int reducedDamage = CalculateDamage(
                data, guardResult, combat, currentActState,
                isAttackFromFront, effectState,
                out SituationalBonus situationalBonus);

            // Step 5: アーマー削り + HP適用
            (int actualDamage, bool isKill, float armorBefore, float actionArmorBefore,
             bool actionArmorJustBroken) = ApplyDamageToVitals(
                data, guardResult, effectState, reducedDamage, ref vitals);

            // Step 5.5: Task A — 行動アーマーを今回のヒットで削り切った瞬間の検出。
            // SuperArmor フラグが立っている間は完全保護するため行動中断もしない。
            // ガード成功 (Guarded/JustGuard) 時はアーマー消費が発生しても行動中断は発動しない。
            bool shouldInterruptForArmorBreak =
                actionArmorJustBroken
                && !effectState.hasSuperArmor
                && !GuardJudgmentLogic.IsGuardSucceeded(guardResult);

            // Step 6: 被弾リアクション判定
            float totalArmorBefore = armorBefore + actionArmorBefore;
            bool hasKnockbackForce = data.knockbackForce.sqrMagnitude > 0.01f;
            HitReaction hitReaction = HitReactionLogic.Determine(
                effectState.hasSuperArmor,
                effectState.hasKnockbackImmunity,
                totalArmorBefore,
                hasKnockbackForce,
                guardResult,
                currentActState);

            // Task A: アーマー削り切りかつ SuperArmor 非適用 → Flinch へ強制遷移 (Knockback は維持)。
            // HitReactionLogic.Determine は totalArmorBefore > 0 なら None を返すので、
            // ここでアーマーブレイク瞬間を上書きする必要がある。
            if (shouldInterruptForArmorBreak && hitReaction == HitReaction.None)
            {
                hitReaction = hasKnockbackForce && !effectState.hasKnockbackImmunity
                    ? HitReaction.Knockback
                    : HitReaction.Flinch;
            }

            // Step 6.1: 被弾リアクション結果を ActState に反映
            // HitReactionLogic.Determine が Flinch/Knockback/GuardBreak を返した場合、
            // 次ヒット判定で isInHitstun が正しく評価されるよう SoA に書き戻す。
            // Task B: 既に Flinch 中の場合は Flinch 上書きしない (タイマー延長なし)。
            //        Knockback / GuardBreak / Dead は許可 (Flinch → Knockback 遷移等)。
            ApplyHitReactionToActState(hitReaction, isKill, hash, wasInFlinch);

            // Task A: アーマー削り切り+非SuperArmorの場合、現在実行中の行動をキャンセル。
            // ApplyHitReactionToActState が ActState を Flinch/Knockback に書き戻した後に呼ぶ
            // (CancelAction → ClearActionEffects で _currentActionEffects が null 化されるが、
            //  ActState 書き戻しは既に完了しているので順序は安全)。
            if (shouldInterruptForArmorBreak && !isKill && _actionExecutorController != null)
            {
                _actionExecutorController.CancelAction();
            }

            // Issue #78 M2: armor 削り切ったヒットなら同フレーム後続を skip するためフラグを立てる。
            // 次フレーム以降は ActState=Flinch ロジックで処理されるため、Update() で false に戻す。
            if (shouldInterruptForArmorBreak && !isKill)
            {
                _armorBrokenThisFrame = true;
            }

            // Step 6.5: 状態異常蓄積
            StatusEffectId appliedEffect = ApplyStatusEffect(
                data.statusEffectInfo, guardResult, combat.guardStats.statusCut, hash);

            // Step 7: 結果構築 + イベント + ノックバック
            DamageResult result = BuildResult(
                actualDamage, guardResult, hitReaction, situationalBonus,
                isKill, armorBefore - vitals.currentArmor, appliedEffect);

            FireEvents(result, hash, data);
            ApplyKnockback(data, hitReaction, hasKnockbackForce, combat.knockbackResistance);

            return result;
        }

        // ===== ステップ別メソッド =====

        /// <summary>
        /// Flinch 中の被弾は完全無防備扱いにするため、effectState.actionArmorValue と
        /// SoA の currentArmor、累積消費量 _actionArmorConsumed をすべて 0 にクランプする。
        /// これで Step 5 (ApplyDamageToVitals) のアーマー減衰経路が無効化され、
        /// reducedDamage がそのまま HP に通る。
        ///
        /// ⚠ 副作用: <paramref name="vitals"/>.currentArmor = 0 は Flinch 解除後もそのまま残る。
        ///   既存の <see cref="UpdateArmorRecovery"/> が armorRecoveryDelay 経過後に自然回復させるが、
        ///   「Flinch 解除直後に元の残量まで即時復元する」仕様が必要になった場合は、
        ///   Flinch 遷移時に前値を保存して解除時に復元する機構を別途追加すること (docs/FUTURE_TASKS.md 参照)。
        /// </summary>
        /// <returns>呼び出し時点で Flinch 状態であったかどうか。ApplyHitReactionToActState で上書き抑制に使用。</returns>
        private bool ForceZeroArmorIfInFlinch(
            ActState currentActState,
            ref ActionEffectProcessor.EffectState effectState,
            ref CharacterVitals vitals)
        {
            if (currentActState != ActState.Flinch)
            {
                return false;
            }

            effectState.actionArmorValue = 0f;
            _actionArmorConsumed = 0f;
            vitals.currentArmor = 0f;
            return true;
        }

        private static DamageResult CreateInvincibleResult()
        {
            return new DamageResult
            {
                totalDamage = 0,
                guardResult = GuardResult.NoGuard,
                hitReaction = HitReaction.None,
                isKill = false
            };
        }

        /// <summary>
        /// 直前のJustGuard成立から連続ジャスガ窓内かを判定する。
        /// 初期値 -1f と ResetInternalState のリセットにより、
        /// JustGuard未成立時は常に Time.time (≥0) より小さく false を返す。
        /// </summary>
        private bool IsInContinuousJustGuardWindow()
        {
            return Time.time < _continuousJustGuardExpireTime;
        }

        private void ApplyJustGuardRecovery(GuardResult guardResult, ref CharacterVitals vitals)
        {
            if (guardResult == GuardResult.JustGuard)
            {
                GuardJudgmentLogic.ApplyJustGuardRecovery(
                    ref vitals.currentStamina, vitals.maxStamina,
                    ref vitals.currentArmor, vitals.maxArmor);

                // JustGuard成立直後は次の1ヒットも連続ジャスガ窓内とする
                _continuousJustGuardExpireTime = Time.time + GuardJudgmentLogic.k_ContinuousJustGuardWindow;
            }
        }

        /// <summary>
        /// 通常ガード/ブレイク時のスタミナ削りを適用する。JustGuard/NoGuard時は削らない。
        /// 削り量 = max(0, armorBreakValue - guardStrength)。スタミナは0にクランプ。
        /// </summary>
        private static void ApplyGuardStaminaDrain(
            DamageData data, GuardResult guardResult, CombatStats combat, ref CharacterVitals vitals)
        {
            if (guardResult != GuardResult.Guarded && guardResult != GuardResult.GuardBreak)
            {
                return;
            }

            float drain = GuardJudgmentLogic.CalculateStaminaDrain(
                data.armorBreakValue, combat.guardStats.guardStrength);
            if (drain <= 0f)
            {
                return;
            }

            vitals.currentStamina = Mathf.Max(0f, vitals.currentStamina - drain);
        }

        private int CalculateDamage(
            DamageData data, GuardResult guardResult, CombatStats combat,
            ActState currentActState, bool isAttackFromFront,
            ActionEffectProcessor.EffectState effectState,
            out SituationalBonus situationalBonus)
        {
            situationalBonus = SituationalBonus.None;

            // JustGuardは軽減率100%相当（ダメージ完全0）
            if (guardResult == GuardResult.JustGuard)
            {
                return 0;
            }

            bool guardSucceeded = GuardJudgmentLogic.IsGuardSucceeded(guardResult);

            // 属性別カット率はガード成功時のみ適用。
            // 弱点は defense の属性別低さで表現（DamageCalculator 側で弱点倍率は非搭載）。
            int rawDamage = DamageCalculator.CalculateTotalDamageWithElementalCut(
                data.damage, data.motionValue, combat.defense,
                combat.guardStats,
                applyCuts: guardSucceeded);

            // 状況ダメージボーナス（ガード成功時は適用しない）
            if (!guardSucceeded)
            {
                bool isFromBehind = !isAttackFromFront;
                bool isTargetAttacking = SituationalBonusLogic.IsTargetAttacking(currentActState);
                bool isInHitstun = HitReactionLogic.IsInHitstun(currentActState);

                (float bonusMult, SituationalBonus bonus) =
                    SituationalBonusLogic.Evaluate(isTargetAttacking, isFromBehind, isInHitstun, _bonusConfig);

                if (bonusMult > 1.0f)
                {
                    rawDamage = Mathf.FloorToInt(rawDamage * bonusMult);
                    situationalBonus = bonus;
                }
            }

            // ActionEffect のダメージ軽減適用
            int reducedDamage = rawDamage;
            if (effectState.damageReduction > 0f)
            {
                reducedDamage = Mathf.FloorToInt(reducedDamage * (1f - effectState.damageReduction));
            }

            return reducedDamage;
        }

        private (int actualDamage, bool isKill, float armorBefore, float actionArmorBefore,
                 bool actionArmorJustBroken) ApplyDamageToVitals(
            DamageData data, GuardResult guardResult,
            ActionEffectProcessor.EffectState effectState,
            int reducedDamage, ref CharacterVitals vitals)
        {
            // ActionEffectProcessor.Evaluate は「現在アクティブな Armor 効果の合計」を返す。
            // 過去被弾で削られた分 (_actionArmorConsumed) を差し引いた正味の残量を使う。
            float actionArmor = Mathf.Max(0f, effectState.actionArmorValue - _actionArmorConsumed);
            float actionArmorBefore = actionArmor;
            float armorBefore = vitals.currentArmor;

            // ジャストガード時のアーマー削り: 飛翔体は削り0固定、近接は justGuardResistance で軽減
            float effectiveArmorBreak = data.armorBreakValue;
            if (guardResult == GuardResult.JustGuard)
            {
                if (data.isProjectile)
                {
                    effectiveArmorBreak = 0f;
                }
                else
                {
                    effectiveArmorBreak *= (1f - data.justGuardResistance / 100f);
                }
            }

            (int actualDamage, bool isKill, bool armorBroken) hpResult =
                HpArmorLogic.ApplyDamage(
                    ref vitals.currentHp, ref vitals.currentArmor,
                    reducedDamage, effectiveArmorBreak, ref actionArmor);

            // ref 経由で削られた分を累積消費量に書き戻す。
            // 次ヒット時に effectState.actionArmorValue - _actionArmorConsumed が正しい残量になる。
            float consumedThisHit = actionArmorBefore - actionArmor;
            if (consumedThisHit > 0f)
            {
                _actionArmorConsumed += consumedThisHit;
            }

            // Task A: 今回のヒットで行動アーマーを「削り切った瞬間」かを判定する。
            // - 削る前 (actionArmorBefore) が残っていた
            // - 今回のヒットで消費が発生した
            // - 削った結果 0 以下になった
            // いずれも満たす場合 true。既に 0 だった状態でのヒットは false (再 Flinch 防止)。
            bool actionArmorJustBroken = actionArmorBefore > 0f
                && consumedThisHit > 0f
                && actionArmor <= 0f;

            // 被弾したらアーマー回復タイマーリセット
            if (reducedDamage > 0)
            {
                _armorRecoveryTimer = _armorRecoveryDelay;
            }

            // HP率キャッシュ更新
            vitals.hpRatio = vitals.maxHp > 0
                ? (byte)(100 * vitals.currentHp / vitals.maxHp)
                : (byte)0;

            return (hpResult.actualDamage, hpResult.isKill, armorBefore, actionArmorBefore, actionArmorJustBroken);
        }

        /// <summary>
        /// HitReaction の結果を SoA の ActState に書き戻す。
        /// - Knockback → ActState.Knockbacked（着地→起き上がり完了まで持続）
        /// - Flinch    → ActState.Flinch（HitReactionLogic.k_FlinchDuration 相当の短い硬直）
        /// - GuardBreak → ActState.GuardBroken（k_GuardBreakStunDuration の硬直）
        /// - None      → 現状維持（SuperArmor やガード成功時は上書きしない）
        ///
        /// Death 判定時は ActState.Dead を優先する。
        /// ActState の自動復帰（硬直時間経過後 Neutral へ）は呼び出し側の ActState ティッカーで行う前提。
        ///
        /// Task B: <paramref name="wasInFlinch"/> が true の場合、Flinch による上書きは行わない
        /// (タイマー延長・再発動なし)。ただし Knockback / GuardBreak / Dead は許可する
        /// (Flinch → Knockback 遷移等、より重いリアクションへの昇格は許容)。
        /// </summary>
        private static void ApplyHitReactionToActState(
            HitReaction hitReaction, bool isKill, int hash, bool wasInFlinch)
        {
            if (!GameManager.IsCharacterValid(hash))
            {
                return;
            }

            ref CharacterFlags flags = ref GameManager.Data.GetFlags(hash);

            // 死亡時は必ず Dead に遷移（HitReaction より優先）
            if (isKill)
            {
                flags.ActState = ActState.Dead;
                return;
            }

            switch (hitReaction)
            {
                case HitReaction.Knockback:
                    // Flinch中でも Knockback は許容 (吹き飛ばし攻撃で Knockbacked へ昇格)
                    flags.ActState = ActState.Knockbacked;
                    break;
                case HitReaction.Flinch:
                    // Task B: 既に Flinch 中なら上書きしない (タイマー延長なし)
                    if (!wasInFlinch)
                    {
                        flags.ActState = ActState.Flinch;
                    }
                    break;
                case HitReaction.GuardBreak:
                    flags.ActState = ActState.GuardBroken;
                    break;
                case HitReaction.None:
                default:
                    // ガード成功/SuperArmor/アーマー残り時は現状の ActState を維持
                    break;
            }
        }

        private static DamageResult BuildResult(
            int actualDamage, GuardResult guardResult, HitReaction hitReaction,
            SituationalBonus situationalBonus, bool isKill,
            float armorDamage, StatusEffectId appliedEffect)
        {
            return new DamageResult
            {
                totalDamage = actualDamage,
                guardResult = guardResult,
                hitReaction = hitReaction,
                situationalBonus = situationalBonus,
                isCritical = false,  // クリティカル機構は仕様外として廃止済み。フィールド自体は互換性のため残置
                isKill = isKill,
                armorDamage = armorDamage,
                appliedEffect = appliedEffect
            };
        }

        /// <summary>
        /// 状態異常蓄積を適用する。ガード成功時はstatusEffectを無効化する。
        /// </summary>
        private StatusEffectId ApplyStatusEffect(
            StatusEffectInfo info, GuardResult guardResult, float statusCut, int targetHash)
        {
            if (_statusEffectManager == null)
            {
                return StatusEffectId.None;
            }

            if (info.effect == StatusEffectId.None)
            {
                return StatusEffectId.None;
            }

            // ガード成功時は状態異常蓄積しない
            if (GuardJudgmentLogic.IsGuardSucceeded(guardResult))
            {
                return StatusEffectId.None;
            }

            bool triggered = _statusEffectManager.Accumulate(info, statusCut);
            if (triggered)
            {
                if (GameManager.Events != null)
                {
                    GameManager.Events.FireStatusEffectApplied(targetHash, info.effect);
                }
                return info.effect;
            }

            return StatusEffectId.None;
        }

        private static void FireEvents(DamageResult result, int hash, DamageData data)
        {
            if (GameManager.Events != null)
            {
                GameManager.Events.FireDamageDealt(result, data.attackerHash, data.defenderHash);

                if (result.isKill)
                {
                    GameManager.Events.FireCharacterDeath(hash, data.attackerHash);
                }
            }
        }

        private void ApplyKnockback(DamageData data, HitReaction hitReaction, bool hasKnockbackForce, float knockbackResistance)
        {
            if (_rb != null && hasKnockbackForce && hitReaction == HitReaction.Knockback)
            {
                Vector2 knockback = HpArmorLogic.CalculateKnockback(
                    data.knockbackForce, knockbackResistance);
                _rb.AddForce(knockback, ForceMode2D.Impulse);
            }
        }

        /// <summary>
        /// 攻撃が前方からかどうかを判定する。
        /// knockbackForce の向きとキャラクターの向きを比較。
        /// </summary>
        private bool IsAttackFromFront(DamageData data)
        {
            if (data.knockbackForce.sqrMagnitude < 0.001f)
            {
                return true;
            }

            float facingX = _character.transform.localScale.x >= 0 ? 1f : -1f;
            float attackDirX = data.knockbackForce.x;

            // 攻撃方向とキャラの向きが反対（攻撃が正面から来ている）
            return (facingX > 0 && attackDirX < 0) || (facingX < 0 && attackDirX > 0);
        }
    }
}
