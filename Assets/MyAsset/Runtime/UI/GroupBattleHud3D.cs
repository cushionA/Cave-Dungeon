using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>集団戦の試作で、目標と騎士の号令を表示する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GroupBattleHud3D : MonoBehaviour
    {
        [SerializeField] private PlayerCharacterInput3D input;
        [SerializeField] private EncounterCombat3D encounter;
        [SerializeField] private EncounterGoal3D objective;

        private UIDocument _document;
        private VisualElement _panel;
        private Label _goal;
        private Label _orders;
        private ChallengeState? _shownState;
        private BroadcastAction? _shownSlot;
        private BroadcastAction? _shownOrder;
        private int _shownCount = -1;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null) { return; }

            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            _panel.style.position = Position.Absolute;
            _panel.style.top = 18f;
            _panel.style.left = 18f;
            _panel.style.paddingTop = 8f;
            _panel.style.paddingBottom = 8f;
            _panel.style.paddingLeft = 12f;
            _panel.style.paddingRight = 12f;
            _panel.style.backgroundColor = (Color)new Color32(20, 27, 36, 225);

            _goal = AddLabel("group-battle-goal", 18, FontStyle.Bold);
            _orders = AddLabel("group-battle-orders", 14, FontStyle.Normal);
            root.Add(_panel);

            _shownState = null;
            _shownSlot = null;
            _shownOrder = null;
            _shownCount = -1;
        }

        private void OnDisable()
        {
            _panel?.RemoveFromHierarchy();
            _panel = null;
        }

        private void LateUpdate()
        {
            if (_panel == null || objective == null || input == null || encounter == null) { return; }

            ChallengeState state = objective.Result.state;
            if (state != _shownState)
            {
                _shownState = state;
                _goal.text = state switch
                {
                    ChallengeState.Running => "東の通路を進み、奥の目標地点へ",
                    ChallengeState.Completed => "目標地点に到達",
                    ChallengeState.Failed => "遭遇終了",
                    _ => "遭遇を準備中",
                };
                _orders.style.display = state == ChallengeState.Running ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (state != ChallengeState.Running) { return; }

            BroadcastAction slot = input.ActiveFieldOrder;
            BroadcastAction? order = encounter.ActiveOrder;
            int count = encounter.AssignedOrderCount;
            if (slot == _shownSlot && order == _shownOrder && count == _shownCount) { return; }

            _shownSlot = slot;
            _shownOrder = order;
            _shownCount = count;
            _orders.text = order.HasValue
                ? $"F / 十字キー下　{OrderName(slot)}　　{OrderName(order.Value)}を受理 {count}体"
                : $"F / 十字キー下　{OrderName(slot)}　短押しで発令・長押しで切替";
        }

        private Label AddLabel(string name, int size, FontStyle style)
        {
            Label label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = style;
            label.style.color = Color.white;
            _panel.Add(label);
            return label;
        }

        private static string OrderName(BroadcastAction order) => order switch
        {
            BroadcastAction.Rally => "集合",
            BroadcastAction.FocusFire => "集中攻撃",
            BroadcastAction.DefendPoint => "ここを守れ",
            BroadcastAction.ReleaseOrder => "おまかせ",
            _ => "号令",
        };
    }
}
