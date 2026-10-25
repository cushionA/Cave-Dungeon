using System.Text;
using System.Collections.Generic;
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
        private StatusEffectIconStrip _playerEffects;
        private StatusEffectIconStrip _companionEffects;
        private StatusEffectIconStrip _bossEffects;

        // ボス表示状態
        private int _bossHash;
        private bool _showBoss;

        // テキスト変更検出用キャッシュ（毎フレーム文字列アロケーション回避）
        private int _cachedHp = -1;
        private int _cachedMaxHp = -1;
        private float _cachedMp = -1f;
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
            _playerEffects = new StatusEffectIconStrip(root.Q<VisualElement>("player-effects"));
            _companionEffects = new StatusEffectIconStrip(root.Q<VisualElement>("companion-effects"));
            _bossEffects = new StatusEffectIconStrip(root.Q<VisualElement>("boss-effects"));
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
            ClearStatusEffects();
        }

        private void Update()
        {
            if (GameManager.Data == null)
            {
                ClearStatusEffects();
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
                _playerEffects?.Clear();
                return;
            }

            UpdateStatusEffects(_playerEffects, playerHash);

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

        private string FormatVitalsText(float current, int max)
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
                _companionEffects?.Clear();
                return;
            }

            _companionPanel.style.display = DisplayStyle.Flex;
            UpdateStatusEffects(_companionEffects, companionHash);
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
                _bossEffects?.Clear();
                return;
            }

            _bossPanel.style.display = DisplayStyle.Flex;
            UpdateStatusEffects(_bossEffects, _bossHash);
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
            _bossEffects?.Clear();
        }

        private static void UpdateStatusEffects(StatusEffectIconStrip strip, int hash)
        {
            if (GameManager.Data.GetFlags(hash).ActState == ActState.Dead)
            {
                strip?.Clear();
                return;
            }

            strip?.Sync(GameManager.StatusEffects?.Store, hash, GameManager.StatusEffectCatalog);
        }

        private void ClearStatusEffects()
        {
            _playerEffects?.Clear();
            _companionEffects?.Clear();
            _bossEffects?.Clear();
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

    internal sealed class StatusEffectIconStrip
    {
        private sealed class Entry
        {
            public VisualElement root;
            public VisualElement icon;
            public Label shortName;
            public Label time;
            public Label stack;
            public StatusEffectId id;
            public int seconds = -1;
            public int stackCount = -1;
            public StatusEffectPresentation presentation;
        }

        private readonly VisualElement _root;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();

        public StatusEffectIconStrip(VisualElement root)
        {
            _root = root;
        }

        public void Sync(StatusEffectStore store, int targetHash, StatusEffectCatalog catalog)
        {
            if (_root == null)
            {
                return;
            }

            _effects.Clear();
            store?.CopyActiveEffects(targetHash, _effects);
            int count = _effects.Count;
            while (_entries.Count > count)
            {
                int last = _entries.Count - 1;
                _entries[last].root.RemoveFromHierarchy();
                _entries.RemoveAt(last);
            }

            for (int i = 0; i < count; i++)
            {
                if (i == _entries.Count)
                {
                    Entry created = CreateEntry();
                    _entries.Add(created);
                    _root.Add(created.root);
                }

                Entry entry = _entries[i];
                ActiveEffect effect = _effects[i];
                StatusEffectPresentation presentation = null;
                if (catalog != null)
                {
                    catalog.TryGet(effect.id, out presentation);
                }

                if (entry.id != effect.id || entry.presentation != presentation)
                {
                    entry.id = effect.id;
                    entry.presentation = presentation;
                    entry.root.tooltip = !string.IsNullOrEmpty(presentation?.displayName)
                        ? presentation.displayName : effect.id.ToString();
                    entry.icon.style.backgroundColor = presentation != null
                        ? presentation.tint : StatusEffectCatalog.GetDefaultTint(effect.id);
                    bool hasIcon = presentation?.icon != null;
                    entry.icon.style.backgroundImage = hasIcon
                        ? new StyleBackground(presentation.icon) : new StyleBackground(StyleKeyword.None);
                    entry.shortName.text = StatusEffectCatalog.GetShortName(effect.id);
                    entry.shortName.style.display = hasIcon ? DisplayStyle.None : DisplayStyle.Flex;
                }

                int seconds = Mathf.CeilToInt(effect.remainTime);
                if (entry.seconds != seconds)
                {
                    entry.seconds = seconds;
                    entry.time.text = seconds.ToString();
                }
                if (entry.stackCount != effect.stackCount)
                {
                    entry.stackCount = effect.stackCount;
                    entry.stack.text = effect.stackCount > 1 ? effect.stackCount.ToString() : "";
                }
            }

            _root.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Clear()
        {
            _root?.Clear();
            _entries.Clear();
            _effects.Clear();
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private static Entry CreateEntry()
        {
            Entry entry = new Entry
            {
                root = new VisualElement(),
                icon = new VisualElement(),
                shortName = new Label { name = "effect-short-name" },
                time = new Label { name = "effect-time" },
                stack = new Label { name = "effect-stack" }
            };
            entry.root.AddToClassList("status-effect");
            entry.icon.AddToClassList("status-effect-icon");
            entry.shortName.AddToClassList("status-effect-short-name");
            entry.time.AddToClassList("status-effect-time");
            entry.stack.AddToClassList("status-effect-stack");
            entry.icon.Add(entry.shortName);
            entry.icon.Add(entry.stack);
            entry.root.Add(entry.icon);
            entry.root.Add(entry.time);
            return entry;
        }
    }
}
