using UnityEngine;
using Game.Core;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Game.Runtime
{
    /// <summary>
    /// Play Mode で自動入力シーケンスを送信してメカニクスを検証するデバッグコンポーネント。
    /// PlayerInputHandler.SetOverrideInput() を使い、スクリプト化された入力を送る。
    /// 各テストステップの結果をコンソールに出力する。
    ///
    /// TestSceneBuilder がGameManagerに追加する。enableOnStart = true で自動開始。
    /// </summary>
    public class AutoInputTester : MonoBehaviour
    {
        [SerializeField] private bool _enableOnStart = true;
        [SerializeField] private int _loopCount = 3;

        [Header("テスト選択（falseで無効化）")]
        [SerializeField] private bool _testMove = true;
        [SerializeField] private bool _testJump = true;
        [SerializeField] private bool _testLightAttack = true;
        [SerializeField] private bool _testHeavyAttack = true;
        [SerializeField] private bool _testSkill = true;
        [SerializeField] private bool _testDodge = true;
        [SerializeField] private bool _testSprint = true;
        [SerializeField] private bool _testGuard = true;
        [SerializeField] private bool _testButtons = true;
        [SerializeField] private bool _testStamina = true;
        [SerializeField] private bool _testAerialAttack = true;
        [SerializeField] private bool _testComposite = true;

#if UNITY_EDITOR
        public bool EnableOnStart { get => _enableOnStart; set => _enableOnStart = value; }
        public int LoopCount { get => _loopCount; set => _loopCount = value; }
        public bool TestMove { get => _testMove; set => _testMove = value; }
        public bool TestJump { get => _testJump; set => _testJump = value; }
        public bool TestLightAttack { get => _testLightAttack; set => _testLightAttack = value; }
        public bool TestHeavyAttack { get => _testHeavyAttack; set => _testHeavyAttack = value; }
        public bool TestSkill { get => _testSkill; set => _testSkill = value; }
        public bool TestDodge { get => _testDodge; set => _testDodge = value; }
        public bool TestSprint { get => _testSprint; set => _testSprint = value; }
        public bool TestGuard { get => _testGuard; set => _testGuard = value; }
        public bool TestButtons { get => _testButtons; set => _testButtons = value; }
        public bool TestStamina { get => _testStamina; set => _testStamina = value; }
        public bool TestAerialAttack { get => _testAerialAttack; set => _testAerialAttack = value; }
        public bool TestComposite { get => _testComposite; set => _testComposite = value; }
#endif

        private static readonly string k_LogPath = Path.Combine(Application.dataPath, "../auto-input-test-log.txt");
        private StreamWriter _logWriter;

        private PlayerCharacter _player;
        private PlayerInputHandler _inputHandler;
        private BaseCharacter _baseCharacter;
        private ActionExecutorController _actionExecutor;
        private Rigidbody2D _rb;

        private bool _isRunning;
        private int _testIndex;
        private float _stepTimer;
        private float _stepDuration;
        private string _currentTestName;
        private System.Action _stepValidation;

        // テストステップ定義
        private List<TestStep> _steps;

        // 検証用スナップショット
        private float _snapshotStamina;
        private Vector2 _snapshotPosition;
        private bool _snapshotGrounded;
        private int _passCount;
        private int _failCount;

        // 周回管理
        private int _currentLoop;
        private int _totalPassCount;
        private int _totalFailCount;
        private int _fixedUpdateCount;
        private bool _suppressErrors;

        private struct TestStep
        {
            public string name;
            public float duration;
            public MovementInfo input;
            public System.Action onStart;
            public System.Action onValidate;
        }

        private void OnDestroy()
        {
            _suppressErrors = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUnpause;
#endif
            _logWriter?.Close();
            _logWriter = null;
        }


#if UNITY_EDITOR
        private void EditorUnpause()
        {
            if (_suppressErrors && UnityEditor.EditorApplication.isPaused
                && UnityEditor.EditorApplication.isPlaying)
            {
                UnityEditor.EditorApplication.isPaused = false;
            }
        }
#endif

        private void Awake()
        {
            // Error Pause 対策: MCP例外等でポーズされるのを即座に防ぐ
            _suppressErrors = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += EditorUnpause;
#endif
        }

        private void Start()
        {
            if (!_enableOnStart)
            {
                return;
            }

            StartCoroutine(DelayedStart());
        }

        private IEnumerator DelayedStart()
        {
            // GameManager初期化とキャラクター登録を待つ
            yield return new WaitForSeconds(0.5f);

            _player = FindFirstObjectByType<PlayerCharacter>();
            if (_player == null)
            {
                Debug.LogError("[AutoInputTester] PlayerCharacter が見つかりません");
                yield break;
            }

            _inputHandler = _player.GetComponent<PlayerInputHandler>();
            _baseCharacter = _player.GetComponent<BaseCharacter>();
            _actionExecutor = _player.GetComponent<ActionExecutorController>();
            _rb = _player.GetComponent<Rigidbody2D>();

            if (_inputHandler == null)
            {
                Debug.LogError("[AutoInputTester] PlayerInputHandler が見つかりません");
                yield break;
            }

            // Error Pause 対策は Awake() で登録済み

            _logWriter = new StreamWriter(k_LogPath, false, System.Text.Encoding.UTF8);
            _logWriter.AutoFlush = true;

            WriteLog("========================================");
            WriteLog($"[AutoInputTester] 自動入力テスト開始 ({_loopCount}周)");
            WriteLog("========================================");

            _currentLoop = 0;
            _totalPassCount = 0;
            _totalFailCount = 0;

            StartLoop();
        }

        private void StartLoop()
        {
            _currentLoop++;
            _passCount = 0;
            _failCount = 0;

            WriteLog($"[AutoInputTester] ====== 周回 {_currentLoop}/{_loopCount} 開始 ======");

            BuildTestSteps();
            _testIndex = 0;
            _isRunning = true;
            BeginStep();
        }

        private void FixedUpdate()
        {
            if (!_isRunning)
            {
                return;
            }

            _stepTimer -= Time.fixedDeltaTime;

            if (_stepTimer <= 0f)
            {
                // 現在のステップ検証
                ValidateStep();

                // 次のステップへ
                _testIndex++;
                if (_testIndex >= _steps.Count)
                {
                    FinishLoop();
                    return;
                }

                BeginStep();
            }
        }

        private void BeginStep()
        {
            TestStep step = _steps[_testIndex];
            _currentTestName = step.name;
            _stepDuration = step.duration;
            _stepTimer = step.duration;
            _stepValidation = step.onValidate;

            // スナップショット取得
            TakeSnapshot();

            // コールバック
            if (step.onStart != null)
            {
                step.onStart();
            }

            // 入力オーバーライド設定
            _inputHandler.SetOverrideInput(step.input);

            WriteLog($"[AutoInputTester] --- テスト開始: {step.name} (duration={step.duration:F2}s) ---");
        }

        private void ValidateStep()
        {
            if (_stepValidation != null)
            {
                _stepValidation();
            }
            else
            {
                LogPass("(検証なし - 入力送信のみ)");
            }

            // 入力クリア
            _inputHandler.SetOverrideInput(default);
        }

        private void FinishLoop()
        {
            _isRunning = false;
            _inputHandler.ClearOverrideInput();
            _totalPassCount += _passCount;
            _totalFailCount += _failCount;

            WriteLog($"[AutoInputTester] 周回 {_currentLoop}/{_loopCount} 完了: PASS={_passCount} FAIL={_failCount}");

            if (_currentLoop < _loopCount)
            {
                // プレイヤーをスタート位置に戻す
                ResetPlayerPosition();
                // 次の周回を少し遅延して開始（位置リセットを反映させる）
                StartCoroutine(DelayedNextLoop());
            }
            else
            {
                WriteLog("========================================");
                WriteLog($"[AutoInputTester] 全{_loopCount}周完了: TOTAL PASS={_totalPassCount} TOTAL FAIL={_totalFailCount}");
                WriteLog("========================================");
                _logWriter?.Close();
                _logWriter = null;
            }
        }

        private IEnumerator DelayedNextLoop()
        {
            // 物理エンジンの安定化 + Rigidbody位置反映を待つ
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(1.0f);

            // リセット後の状態をログに出力（デバッグ用）
            LogResetState();

            StartLoop();
        }

        private void ResetPlayerPosition()
        {
            if (_player != null)
            {
                _player.transform.position = new Vector3(-6f, 2f, 0f);
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;

                // ActionExecutorの実行状態をクリア
                if (_actionExecutor != null && _actionExecutor.IsExecuting)
                {
                    _actionExecutor.Executor?.CancelCurrent();
                }

                // PlayerCharacterの内部フラグを全リセット
                // （落下攻撃、枯渇ペナルティ、コンボ等）
                _player.ResetInternalState();

                // 入力をクリア（前周回の残留入力を防止）
                _inputHandler.ClearOverrideInput();

                // HP・スタミナを全回復（前周回のダメージ・枯渇を完全リセット）
                int hash = _baseCharacter.ObjectHash;
                if (GameManager.IsCharacterValid(hash))
                {
                    ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
                    vitals.currentHp = vitals.maxHp;
                    vitals.currentMp = vitals.maxMp;
                    vitals.currentStamina = vitals.maxStamina * 0.5f;
                    vitals.currentArmor = vitals.maxArmor;
                }
            }
        }

        private void TakeSnapshot()
        {
            if (_player == null)
            {
                return;
            }

            int hash = _baseCharacter.ObjectHash;
            if (GameManager.IsCharacterValid(hash))
            {
                ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
                _snapshotStamina = vitals.currentStamina;
            }

            _snapshotPosition = _player.transform.position;
            _snapshotGrounded = _baseCharacter.IsGrounded;
        }

        private float GetCurrentStamina()
        {
            int hash = _baseCharacter.ObjectHash;
            if (GameManager.IsCharacterValid(hash))
            {
                ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
                return vitals.currentStamina;
            }
            return -1f;
        }

        private void WriteLog(string msg)
        {
            Debug.Log(msg);
            if (_logWriter != null)
            {
                _logWriter.WriteLine(msg);
            }
        }

        private void LogPass(string detail)
        {
            _passCount++;
            WriteLog($"[AutoInputTester] PASS: {_currentTestName} - {detail}");
        }

        private void LogResetState()
        {
            int hash = _baseCharacter.ObjectHash;
            if (GameManager.IsCharacterValid(hash))
            {
                ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
                WriteLog($"[AutoInputTester] 周回リセット確認: pos={_player.transform.position} " +
                         $"grounded={_baseCharacter.IsGrounded} hp={vitals.currentHp}/{vitals.maxHp} " +
                         $"stamina={vitals.currentStamina:F1}/{vitals.maxStamina:F1} " +
                         $"vel={_rb.linearVelocity}");
            }
        }

        private void LogFail(string detail)
        {
            _failCount++;
            WriteLog($"[AutoInputTester] FAIL: {_currentTestName} - {detail}");
        }

        // ============================================================
        //  テストステップ定義
        // ============================================================

        private void BuildTestSteps()
        {
            _steps = new List<TestStep>();

            // 0. 初期待機（安定化）— 常に実行
            AddStep("00_初期待機", 0.3f, default, null, () =>
            {
                LogPass($"初期位置: {_player.transform.position}, Grounded={_baseCharacter.IsGrounded}");
            });

            // ===== 移動系 =====
            if (_testMove)
            {
                AddStep("01_右移動", 0.5f,
                    new MovementInfo { moveDirection = new Vector2(1f, 0f) },
                    null,
                    () =>
                    {
                        float dx = _player.transform.position.x - _snapshotPosition.x;
                        if (dx > 0.1f) { LogPass($"右に {dx:F2} 移動"); }
                        else { LogFail($"右移動不足: dx={dx:F2}"); }
                    });

                AddStep("02_左移動", 0.5f,
                    new MovementInfo { moveDirection = new Vector2(-1f, 0f) },
                    null,
                    () =>
                    {
                        float dx = _player.transform.position.x - _snapshotPosition.x;
                        if (dx < -0.1f) { LogPass($"左に {Mathf.Abs(dx):F2} 移動"); }
                        else { LogFail($"左移動不足: dx={dx:F2}"); }
                    });
            }

            // ===== ジャンプ =====
            if (_testJump)
            {
                AddStep("03_ジャンプ", 0.4f,
                    new MovementInfo { jumpPressed = true, jumpHeld = true },
                    null,
                    () =>
                    {
                        float dy = _player.transform.position.y - _snapshotPosition.y;
                        if (dy > 0.1f) { LogPass($"上に {dy:F2} 上昇"); }
                        else { LogFail($"ジャンプ不足: dy={dy:F2}"); }
                    });

                AddStep("03b_着地待機", 0.8f, default, null, () =>
                {
                    if (_baseCharacter.IsGrounded) { LogPass("着地確認"); }
                    else { LogFail("未着地"); }
                });
            }

            // ===== 弱攻撃（コンボ） =====
            if (_testLightAttack)
            {
                AddStep("04_弱攻撃1段", 0.1f,
                    new MovementInfo { attackInput = AttackInputType.LightAttack, chargeMultiplier = 1f },
                    null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (stDiff > 0f || busy) { LogPass($"攻撃発動 executing={busy} staminaCost={stDiff:F1}"); }
                        else { LogFail($"攻撃未発動 executing={busy} stamina変化={stDiff:F1}"); }
                    });

                AddStep("04b_攻撃完了待機", 0.4f, default, null, () => { LogPass("攻撃完了待機"); });

                AddStep("05_弱攻撃コンボ2段", 0.1f,
                    new MovementInfo { attackInput = AttackInputType.LightAttack, chargeMultiplier = 1f },
                    null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (busy || stDiff > 0f) { LogPass($"コンボ2段 executing={busy} staminaCost={stDiff:F1}"); }
                        else { LogFail($"コンボ2段未発動 executing={busy} staminaCost={stDiff:F1}"); }
                    });

                AddStep("05b_コンボ完了待機", 0.4f, default, null, () => { LogPass("コンボ2段待機完了"); });

                AddStep("06_弱攻撃コンボ3段", 0.1f,
                    new MovementInfo { attackInput = AttackInputType.LightAttack, chargeMultiplier = 1f },
                    null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (busy || stDiff > 0f) { LogPass($"コンボ3段 executing={busy} staminaCost={stDiff:F1}"); }
                        else { LogFail($"コンボ3段未発動 executing={busy} staminaCost={stDiff:F1}"); }
                    });

                AddStep("06b_全コンボ完了待機", 1.0f, default, null, () => { LogPass("全コンボ完了待機終了"); });
            }

            // ===== 強攻撃 =====
            if (_testHeavyAttack)
            {
                AddStep("07_強攻撃", 0.1f,
                    new MovementInfo { attackInput = AttackInputType.HeavyAttack, chargeMultiplier = 1f },
                    null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (stDiff > 0f || busy) { LogPass($"強攻撃発動 executing={busy} staminaCost={stDiff:F1}"); }
                        else { LogFail($"強攻撃未発動 executing={busy} stamina変化={stDiff:F1}"); }
                    });

                AddStep("07b_強攻撃完了待機", 1.0f, default, null, () => { LogPass("強攻撃完了待機終了"); });
            }

            // ===== スキル =====
            if (_testSkill)
            {
                AddStep("08_スキル攻撃", 0.1f,
                    new MovementInfo { attackInput = AttackInputType.Skill, chargeMultiplier = 1f },
                    null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        LogPass($"スキル入力送信 executing={busy}");
                    });

                AddStep("08b_スキル完了待機", 0.5f, default, null, () => { LogPass("スキル完了待機"); });
            }

            // ===== 回避 =====
            if (_testDodge)
            {
                AddStep("09_回避", 0.1f,
                    new MovementInfo { dodgePressed = true, moveDirection = new Vector2(1f, 0f) },
                    null,
                    () =>
                    {
                        float dx = _player.transform.position.x - _snapshotPosition.x;
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (dx > 0.1f || stDiff > 0f) { LogPass($"回避発動 移動={dx:F2} staminaCost={stDiff:F1}"); }
                        else { LogFail($"回避未発動 移動={dx:F2} stamina変化={stDiff:F1}"); }
                    });

                AddStep("09b_回避後待機", 0.5f, default, null, () => { LogPass("回避後待機完了"); });
            }

            // ===== スプリント =====
            if (_testSprint)
            {
                AddStep("10_スプリント", 0.5f,
                    new MovementInfo { moveDirection = new Vector2(1f, 0f), sprintHeld = true },
                    null,
                    () =>
                    {
                        float dx = _player.transform.position.x - _snapshotPosition.x;
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (dx > 0.5f) { LogPass($"スプリント移動={dx:F2} staminaCost={stDiff:F1}"); }
                        else { LogFail($"スプリント不足 移動={dx:F2}"); }
                    });

                AddStep("10b_静止スプリント", 0.5f,
                    new MovementInfo { sprintHeld = true },
                    null,
                    () =>
                    {
                        float stDiff = _snapshotStamina - GetCurrentStamina();
                        if (stDiff <= 0.1f) { LogPass($"静止スプリントでスタミナ消費なし: diff={stDiff:F2}"); }
                        else { LogFail($"静止スプリントでスタミナ消費あり: diff={stDiff:F2}"); }
                    });
            }

            // ===== ガード =====
            if (_testGuard)
            {
                AddStep("11_ガード", 0.3f,
                    new MovementInfo { guardHeld = true },
                    null,
                    () => { LogPass("ガード入力送信完了（クラッシュなし）"); });
            }

            // ===== ボタン入力系 =====
            if (_testButtons)
            {
                AddStep("12_インタラクト", 0.1f,
                    new MovementInfo { interactPressed = true }, null,
                    () => { LogPass("インタラクト入力送信（クラッシュなし）"); });

                AddStep("13_連携", 0.1f,
                    new MovementInfo { cooperationPressed = true }, null,
                    () => { LogPass("連携入力送信（クラッシュなし）"); });

                AddStep("14_武器切替", 0.1f,
                    new MovementInfo { weaponSwitchPressed = true }, null,
                    () => { LogPass("武器切替入力送信（クラッシュなし）"); });

                AddStep("15_グリップ切替", 0.1f,
                    new MovementInfo { gripSwitchPressed = true }, null,
                    () => { LogPass("グリップ切替入力送信（クラッシュなし）"); });

                AddStep("16_メニュー", 0.1f,
                    new MovementInfo { menuPressed = true }, null,
                    () => { LogPass("メニュー入力送信（クラッシュなし）"); });

                AddStep("17_マップ", 0.1f,
                    new MovementInfo { mapPressed = true }, null,
                    () => { LogPass("マップ入力送信（クラッシュなし）"); });
            }

            // ===== スタミナ回復 =====
            if (_testStamina)
            {
                AddStep("18_スタミナ回復待機", 2.0f, default, null, () =>
                {
                    float stNow = GetCurrentStamina();
                    float recovery = stNow - _snapshotStamina;
                    if (recovery > 0f) { LogPass($"スタミナ回復確認: +{recovery:F1} (現在={stNow:F1})"); }
                    else if (_snapshotStamina >= 99f) { LogPass($"スタミナ既に満タン: 現在={stNow:F1}（回復不要）"); }
                    else { LogFail($"スタミナ未回復: 変化={recovery:F1} (現在={stNow:F1}, snapshot={_snapshotStamina:F1})"); }
                });

                AddStep("21_スタミナ枯渇開始", 0.15f, default,
                    () =>
                    {
                        int hash = _baseCharacter.ObjectHash;
                        if (GameManager.IsCharacterValid(hash))
                        {
                            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
                            vitals.currentStamina = 0f;
                        }
                    },
                    () =>
                    {
                        float stNow = GetCurrentStamina();
                        if (stNow < 5f) { LogPass($"枯渇直後: スタミナ={stNow:F1}（まだ回復していない）"); }
                        else { LogFail($"枯渇直後なのに回復: スタミナ={stNow:F1}"); }
                    });

                AddStep("21b_枯渇ペナルティ確認", 1.0f, default, null, () =>
                {
                    float stNow = GetCurrentStamina();
                    float recovery = stNow - _snapshotStamina;
                    if (recovery < 5f) { LogPass($"枯渇ペナルティ: 1秒で回復={recovery:F1}（遅延あり）"); }
                    else { LogFail($"枯渇ペナルティなし: 1秒で回復={recovery:F1}（即回復）"); }
                });
            }

            // ===== 空中攻撃 =====
            if (_testAerialAttack)
            {
                AddStep("19_ジャンプ開始", 0.25f,
                    new MovementInfo { jumpPressed = true, jumpHeld = true }, null,
                    () =>
                    {
                        bool grounded = _baseCharacter.IsGrounded;
                        if (!grounded) { LogPass($"ジャンプ離陸確認 grounded={grounded}"); }
                        else { LogPass("ジャンプ入力送信（まだ接地中）"); }
                    });

                AddStep("19b_空中弱攻撃", 0.15f,
                    new MovementInfo { attackInput = AttackInputType.AerialLight, chargeMultiplier = 1f, jumpHeld = true },
                    null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        bool grounded = _baseCharacter.IsGrounded;
                        if (busy && !grounded) { LogPass($"空中弱攻撃発動 grounded={grounded}"); }
                        else if (!grounded) { LogPass($"空中状態OK executing={busy} grounded={grounded}"); }
                        else { LogFail($"空中弱攻撃問題 executing={busy} grounded={grounded}"); }
                    });

                AddStep("19c_空中攻撃着地待機", 2.5f, default, null, () =>
                {
                    if (_baseCharacter.IsGrounded) { LogPass("空中攻撃後着地"); }
                    else { LogFail("空中攻撃後未着地"); }
                });

                AddStep("20_ジャンプ開始(落下攻撃)", 0.2f,
                    new MovementInfo { jumpPressed = true, jumpHeld = true }, null,
                    () => { LogPass("ジャンプ入力送信（落下攻撃用）"); });

                AddStep("20b_空中強攻撃(落下)", 0.1f,
                    new MovementInfo { attackInput = AttackInputType.AerialHeavy, chargeMultiplier = 1f }, null,
                    () =>
                    {
                        bool busy = _actionExecutor != null && _actionExecutor.IsExecuting;
                        LogPass($"空中強攻撃送信 executing={busy}");
                    });

                AddStep("20c_落下攻撃着地", 1.5f, default, null, () =>
                {
                    if (_baseCharacter.IsGrounded) { LogPass("落下攻撃後着地"); }
                    else { LogFail("落下攻撃後未着地（まだ空中）"); }
                });
            }

            // ===== 複合入力テスト =====
            if (_testComposite)
            {
                AddStep("22_移動+ジャンプ同時", 0.2f,
                    new MovementInfo { moveDirection = new Vector2(1f, 0f), jumpPressed = true, jumpHeld = true },
                    null,
                    () =>
                    {
                        float dx = _player.transform.position.x - _snapshotPosition.x;
                        float dy = _player.transform.position.y - _snapshotPosition.y;
                        if (dx > 0.01f && dy > 0.01f) { LogPass($"移動+ジャンプ同時 dx={dx:F2} dy={dy:F2}"); }
                        else { LogFail($"移動+ジャンプ同時失敗 dx={dx:F2} dy={dy:F2}"); }
                    });

                AddStep("22b_複合着地待機", 0.8f, default, null, () => { LogPass("複合入力着地待機完了"); });

                AddStep("23_ガード+移動", 0.3f,
                    new MovementInfo { guardHeld = true, moveDirection = new Vector2(-1f, 0f) }, null,
                    () => { LogPass("ガード+移動 同時入力（クラッシュなし）"); });
            }

            // 最終状態ログ — 常に実行
            AddStep("24_最終状態ログ", 0.1f, default, null, () =>
            {
                float stNow = GetCurrentStamina();
                Vector2 pos = _player.transform.position;
                bool grounded = _baseCharacter.IsGrounded;
                bool executing = _actionExecutor != null && _actionExecutor.IsExecuting;
                WriteLog($"[AutoInputTester] 最終状態: pos={pos} grounded={grounded} stamina={stNow:F1} executing={executing}");
                LogPass("最終状態ログ出力完了");
            });
        }

        private void AddStep(string name, float duration, MovementInfo input,
            System.Action onStart, System.Action onValidate)
        {
            _steps.Add(new TestStep
            {
                name = name,
                duration = duration,
                input = input,
                onStart = onStart,
                onValidate = onValidate
            });
        }
    }
}
