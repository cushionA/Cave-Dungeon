using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    public sealed partial class ExpeditionFastTravel3D
    {
        private VisualElement _overlay;
        private VisualElement _contents;
        private Label _status;
        private Button _close;

        private void OpenMenu(VisualElement root)
        {
            _overlay = new VisualElement { name = "fast-travel-panel" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.right = _overlay.style.top = _overlay.style.bottom = 0;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.01f, 0.02f, 0.03f, 0.58f);
            _overlay.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                Close();
                evt.StopPropagation();
            });
            root.Add(_overlay);

            var panel = new VisualElement();
            panel.style.width = Length.Percent(90f);
            panel.style.maxWidth = 440;
            panel.style.maxHeight = Length.Percent(85f);
            panel.style.paddingLeft = panel.style.paddingRight = 20;
            panel.style.paddingTop = panel.style.paddingBottom = 16;
            panel.style.backgroundColor = new Color(0.065f, 0.09f, 0.135f, 0.98f);
            panel.style.color = new Color(0.94f, 0.96f, 0.98f);
            _overlay.Add(panel);

            var heading = new Label("ファストトラベル");
            heading.style.fontSize = 24;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginBottom = 8;
            panel.Add(heading);

            var list = new ScrollView();
            list.style.flexShrink = 1f;
            _contents = list.contentContainer;
            panel.Add(list);
            _status = new Label { name = "fast-travel-status" };
            _status.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_status);
            _close = new Button(Close) { name = "fast-travel-close", text = "閉じる" };
            panel.Add(_close);
            RenderMenu(string.Empty);
        }

        private void RenderMenu(string message)
        {
            _contents.Clear();
            Button first = null;
            foreach (Destination destination in destinations)
            {
                if (destination.pointId == _source.PointId || !TryFindDestination(destination.pointId, out _)
                    || !IsVisited(destination)) { continue; }
                var button = new Button(() => TryTravel(destination.pointId))
                {
                    name = $"fast-travel-{destination.pointId}",
                    text = string.IsNullOrWhiteSpace(destination.displayName) ? destination.pointId : destination.displayName,
                };
                button.style.minHeight = 32;
                button.SetEnabled(!IsTraveling);
                _contents.Add(button);
                if (first == null) { first = button; }
            }

            _status.text = first == null && string.IsNullOrEmpty(message) ? "訪問済みの移動先はありません。" : message;
            Button focus = !IsTraveling && first != null ? first : _close;
            focus.schedule.Execute(focus.Focus);
        }
    }
}
