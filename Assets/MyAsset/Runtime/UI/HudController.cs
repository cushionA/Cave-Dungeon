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

        // 前フレームの比率（トゥイーン差分検出用）
        private float _prevHpRatio = -1f;
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
            if (playerHash == 0 || !GameManager.Data.TryGetValue(playerHash, out int _))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(playerHash);
            (float hpRatio, float mpRatio, float staminaRatio) = HudDataProvider.GetVitalsRatios(vitals);

            // HP: 減少方向はダメージバー演出付き
            TweenBar(_hpBarFill, ref _hpHandle, ref _prevHpRatio, hpRatio, _barTweenDuration);

            // HPダメージバー（遅延で追従する赤バー）
            if (_hpBarDamage != null && hpRatio < _prevHpRatio)
            {
                TweenBar(_hpBarDamage, ref _hpDamageHandle, ref _prevHpRatio, hpRatio, _damageTweenDuration, Ease.InQuad, _barTweenDuration);
            }

            TweenBar(_mpBarFill, ref _mpHandle, ref _prevMpRatio, mpRatio, _barTweenDuration);
            TweenBar(_staminaBarFill, ref _staminaHandle, ref _prevStaminaRatio, staminaRatio, _barTweenDuration * 0.5f);

            if (_hpText != null)
            {
                _hpText.text = $"{vitals.currentHp}/{vitals.maxHp}";
            }
            if (_mpText != null)
            {
                _mpText.text = $"{vitals.currentMp}/{vitals.maxMp}";
            }
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

            if (companionHash == 0 || !GameManager.Data.TryGetValue(companionHash, out int _))
            {
                _companionPanel.style.display = DisplayStyle.None;
                return;
            }

            _companionPanel.style.display = DisplayStyle.Flex;
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(companionHash);
            (float hpRatio, float mpRatio, float _) = HudDataProvider.GetVitalsRatios(vitals);

            TweenBar(_companionHpBarFill, ref _companionHpHandle, ref _prevCompanionHpRatio, hpRatio, _barTweenDuration);
            TweenBar(_companionMpBarFill, ref _companionMpHandle, ref _prevCompanionMpRatio, mpRatio, _barTweenDuration);

            if (_companionHpText != null)
            {
                _companionHpText.text = $"{vitals.currentHp}/{vitals.maxHp}";
            }
        }

        private void UpdateBossStatus()
        {
            if (_bossPanel == null)
            {
                return;
            }

            if (!_showBoss || _bossHash == 0 || !GameManager.Data.TryGetValue(_bossHash, out int _))
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
