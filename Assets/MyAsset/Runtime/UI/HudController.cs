using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;
using LitMotion;

namespace Game.Runtime
{
    /// <summary>
    /// HUD表示コントローラ。UI ToolkitベースでHP/MP/スタミナバーを更新する。
    /// LitMotionでバー幅をスムース補間する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudController : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private float _barTweenDuration = 0.25f;
        [SerializeField] private float _damageTweenDuration = 0.6f;

        private UIDocument _uiDocument;

        // プレイヤーステータス
        private VisualElement _hpBarFill;
        private VisualElement _hpBarDamage;
        private VisualElement _mpBarFill;
        private VisualElement _staminaBarFill;
        private Label _hpText;
        private Label _mpText;

        // 仲間ステータス
        private VisualElement _companionPanel;
        private VisualElement _companionHpBarFill;
        private VisualElement _companionMpBarFill;
        private Label _companionHpText;

        // ボスステータス
        private VisualElement _bossPanel;
        private VisualElement _bossHpBarFill;
        private Label _bossNameLabel;

        // 通貨
        private Label _currencyText;

        // ボス表示状態
        private int _bossHash;
        private bool _showBoss;

        // テキスト変更検出用キャッシュ（毎フレーム文字列アロケーション回避）
        private int _cachedHp = -1;
        private int _cachedMaxHp = -1;
        private int _cachedMp = -1;
        private int _cachedMaxMp = -1;
        private int _cachedCompanionHp = -1;
        private int _cachedCompanionMaxHp = -1;

        // "current/max" フォーマット用の再利用 StringBuilder。$ 補間の内部 array alloc を避ける (Issue #79 M6-Perf)。
        private readonly StringBuilder _vitalsTextBuilder = new StringBuilder(16);

        // 前フレームの比率（トゥイーン差分検出用）
        private float _prevHpRatio = -1f;
        private float _prevDamageBarRatio = -1f;
        private float _prevMpRatio = -1f;
        private float _prevStaminaRatio = -1f;
        private float _prevCompanionHpRatio = -1f;
        private float _prevCompanionMpRatio = -1f;
        private float _prevBossHpRatio = -1f;

        // アクティブなモーションハンドル
        private MotionHandle _hpHandle;
        private MotionHandle _hpDamageHandle;
        private MotionHandle _mpHandle;
        private MotionHandle _staminaHandle;
        private MotionHandle _companionHpHandle;
        private MotionHandle _companionMpHandle;
        private MotionHandle _bossHpHandle;

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                return;
            }

            // プレイヤー
            _hpBarFill = root.Q<VisualElement>("hp-bar-fill");
            _hpBarDamage = root.Q<VisualElement>("hp-bar-damage");
            _mpBarFill = root.Q<VisualElement>("mp-bar-fill");
            _staminaBarFill = root.Q<VisualElement>("stamina-bar-fill");
            _hpText = root.Q<Label>("hp-text");
            _mpText = root.Q<Label>("mp-text");

            // 仲間
            _companionPanel = root.Q<VisualElement>("companion-status");
            _companionHpBarFill = root.Q<VisualElement>("companion-hp-bar-fill");
            _companionMpBarFill = root.Q<VisualElement>("companion-mp-bar-fill");
            _companionHpText = root.Q<Label>("companion-hp-text");

            // ボス
            _bossPanel = root.Q<VisualElement>("boss-status");
            _bossHpBarFill = root.Q<VisualElement>("boss-hp-bar-fill");
            _bossNameLabel = root.Q<Label>("boss-name");

            // 通貨
            _currencyText = root.Q<Label>("currency-text");
        }

        private void OnDisable()
        {
            CancelHandle(ref _hpHandle);
            CancelHandle(ref _hpDamageHandle);
            CancelHandle(ref _mpHandle);
            CancelHandle(ref _staminaHandle);
            CancelHandle(ref _companionHpHandle);
            CancelHandle(ref _companionMpHandle);
            CancelHandle(ref _bossHpHandle);
        }

        private void Update()
        {
            if (GameManager.Data == null)
            {
                return;
            }

            UpdatePlayerStatus();
            UpdateCompanionStatus();
            UpdateBossStatus();
        }

        private void UpdatePlayerStatus()
        {
            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.IsCharacterValid(playerHash))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(playerHash);
            HudDataProvider.GetVitalsRatios(vitals, out float hpRatio, out float mpRatio, out float staminaRatio);

            // HPダメージバー（遅延で追従する赤バー）-- HPバーより先に判定する
            if (_hpBarDamage != null && _prevHpRatio >= 0f && hpRatio < _prevHpRatio)
            {
                TweenBar(_hpBarDamage, ref _hpDamageHandle, ref _prevDamageBarRatio, _prevHpRatio, _damageTweenDuration, Ease.InQuad, _barTweenDuration);
            }

            // HP: メインバー
            TweenBar(_hpBarFill, ref _hpHandle, ref _prevHpRatio, hpRatio, _barTweenDuration);

            TweenBar(_mpBarFill, ref _mpHandle, ref _prevMpRatio, mpRatio, _barTweenDuration);
            TweenBar(_staminaBarFill, ref _staminaHandle, ref _prevStaminaRatio, staminaRatio, _barTweenDuration * 0.5f);

            if (_hpText != null && (vitals.currentHp != _cachedHp || vitals.maxHp != _cachedMaxHp))
            {
                _cachedHp = vitals.currentHp;
                _cachedMaxHp = vitals.maxHp;
                _hpText.text = FormatVitalsText(vitals.currentHp, vitals.maxHp);
            }
            if (_mpText != null && (vitals.currentMp != _cachedMp || vitals.maxMp != _cachedMaxMp))
            {
                _cachedMp = vitals.currentMp;
                _cachedMaxMp = vitals.maxMp;
                _mpText.text = FormatVitalsText(vitals.currentMp, vitals.maxMp);
            }
        }

        /// <summary>
        /// "current/max" 文字列を生成する。再利用 StringBuilder で string 補間の内部 array alloc を避ける。
        /// 戻り値の string allocation 1 個分は UI Toolkit Label.text への代入で必要 (避けられない最低限のコスト)。
        /// </summary>
        private string FormatVitalsText(int current, int max)
        {
            _vitalsTextBuilder.Clear();
            _vitalsTextBuilder.Append(current);
            _vitalsTextBuilder.Append('/');
            _vitalsTextBuilder.Append(max);
            return _vitalsTextBuilder.ToString();
        }

        private void UpdateCompanionStatus()
        {
            if (_companionPanel == null)
            {
                return;
            }

            int companionHash = 0;
            for (int i = 0; i < CharacterRegistry.AllyHashes.Count; i++)
            {
                int hash = CharacterRegistry.AllyHashes[i];
                if (hash != CharacterRegistry.PlayerHash)
                {
                    companionHash = hash;
                    break;
                }
            }

            if (companionHash == 0 || !GameManager.IsCharacterValid(companionHash))
            {
                _companionPanel.style.display = DisplayStyle.None;
                return;
            }

            _companionPanel.style.display = DisplayStyle.Flex;
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(companionHash);
            HudDataProvider.GetVitalsRatios(vitals, out float hpRatio, out float mpRatio, out float _);

            TweenBar(_companionHpBarFill, ref _companionHpHandle, ref _prevCompanionHpRatio, hpRatio, _barTweenDuration);
            TweenBar(_companionMpBarFill, ref _companionMpHandle, ref _prevCompanionMpRatio, mpRatio, _barTweenDuration);

            if (_companionHpText != null && (vitals.currentHp != _cachedCompanionHp || vitals.maxHp != _cachedCompanionMaxHp))
            {
                _cachedCompanionHp = vitals.currentHp;
                _cachedCompanionMaxHp = vitals.maxHp;
                _companionHpText.text = FormatVitalsText(vitals.currentHp, vitals.maxHp);
            }
        }

        private void UpdateBossStatus()
        {
            if (_bossPanel == null)
            {
                return;
            }

            if (!_showBoss || _bossHash == 0 || !GameManager.IsCharacterValid(_bossHash))
            {
                _bossPanel.style.display = DisplayStyle.None;
                return;
            }

            _bossPanel.style.display = DisplayStyle.Flex;
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_bossHash);
            float hpRatio = HudDataProvider.CalculateBarRatio(vitals.currentHp, vitals.maxHp);
            TweenBar(_bossHpBarFill, ref _bossHpHandle, ref _prevBossHpRatio, hpRatio, _barTweenDuration);
        }

        /// <summary>
        /// ボスHP表示を開始する。
        /// </summary>
        public void ShowBossHp(int bossHash, string bossName)
        {
            _bossHash = bossHash;
            _showBoss = true;
            if (_bossNameLabel != null)
            {
                _bossNameLabel.text = bossName;
            }
        }

        /// <summary>
        /// ボスHP表示を終了する。
        /// </summary>
        public void HideBossHp()
        {
            _showBoss = false;
            _bossHash = 0;
        }

        /// <summary>
        /// 通貨表示を更新する。
        /// </summary>
        public void UpdateCurrency(int amount)
        {
            if (_currencyText != null)
            {
                _currencyText.text = $"{amount} G";
            }
        }

        /// <summary>
        /// バー幅をLitMotionでスムース補間する。
        /// 値が変わった場合のみトゥイーンを開始する。
        /// </summary>
        private void TweenBar(
            VisualElement bar,
            ref MotionHandle handle,
            ref float prevRatio,
            float targetRatio,
            float duration,
            Ease ease = Ease.OutCubic,
            float delay = 0f)
        {
            if (bar == null)
            {
                return;
            }

            // 差分が小さければスキップ（毎フレーム不要なトゥイーン防止）
            const float k_Threshold = 0.001f;
            if (Mathf.Abs(targetRatio - prevRatio) < k_Threshold && prevRatio >= 0f)
            {
                return;
            }

            float fromRatio = prevRatio < 0f ? targetRatio : prevRatio;
            prevRatio = targetRatio;

            CancelHandle(ref handle);

            handle = LMotion.Create(fromRatio * 100f, targetRatio * 100f, duration)
                .WithEase(ease)
                .WithDelay(delay)
                .Bind(bar, (percent, b) =>
                {
                    b.style.width = Length.Percent(percent);
                });
        }

        private static void CancelHandle(ref MotionHandle handle)
        {
            if (handle.IsActive())
            {
                handle.Cancel();
            }
            handle = default;
        }
    }
}
