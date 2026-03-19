using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// HUD表示コントローラ。UI ToolkitベースでHP/MP/スタミナバーを更新する。
    /// HudDataProvider（純ロジック）からデータを取得し、UIElementsに反映する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudController : MonoBehaviour
    {
        private UIDocument _uiDocument;

        // プレイヤーステータス
        private VisualElement _hpBarFill;
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

            SetBarWidth(_hpBarFill, hpRatio);
            SetBarWidth(_mpBarFill, mpRatio);
            SetBarWidth(_staminaBarFill, staminaRatio);

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

            // 仲間を探す（AllyHashesからPlayer以外）
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

            SetBarWidth(_companionHpBarFill, hpRatio);
            SetBarWidth(_companionMpBarFill, mpRatio);

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
            SetBarWidth(_bossHpBarFill, hpRatio);
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

        private static void SetBarWidth(VisualElement bar, float ratio)
        {
            if (bar != null)
            {
                bar.style.width = Length.Percent(ratio * 100f);
            }
        }
    }
}
