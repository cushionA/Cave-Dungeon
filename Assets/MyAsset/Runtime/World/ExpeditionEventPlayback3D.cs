using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>専用シーンのカメラとタイムラインを再生し、終了通知を探索側に返す。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionEventPlayback3D : MonoBehaviour
    {
        [SerializeField] private PlayableDirector director;
        [SerializeField] private Camera eventCamera;

        private VisualElement _backdrop;
        private Button _skip;
        private bool _playing;
        private bool _finishing;

        public PlayableDirector Director => director;
        public Camera EventCamera => eventCamera;
        internal event Action Finished;

        internal bool TryBegin()
        {
            if (_playing || !isActiveAndEnabled || director == null || !director.isActiveAndEnabled
                || director.playableAsset == null || eventCamera == null || !eventCamera.gameObject.activeInHierarchy)
            {
                return false;
            }

            _playing = true;
            eventCamera.enabled = true;
            if (TryGetComponent(out UIDocument document))
            {
                VisualElement root = document.rootVisualElement;
                if (root != null)
                {
                    _backdrop = new VisualElement { name = "expedition-event-backdrop", pickingMode = PickingMode.Position };
                    _backdrop.style.position = Position.Absolute;
                    _backdrop.style.left = 0f;
                    _backdrop.style.right = 0f;
                    _backdrop.style.top = 0f;
                    _backdrop.style.bottom = 0f;
                    _skip = new Button(Finish) { name = "expedition-event-skip", text = "スキップ" };
                    _skip.style.position = Position.Absolute;
                    _skip.style.right = 16f;
                    _skip.style.bottom = 16f;
                    _skip.style.minWidth = 104f;
                    _skip.style.minHeight = 36f;
                    _backdrop.Add(_skip);
                    root.Add(_backdrop);
                    _skip.Focus();
                }
            }
            director.stopped += OnDirectorStopped;
            director.time = 0d;
            director.Play();
            return true;
        }

        public void Finish()
        {
            if (_playing) { _finishing = true; }
        }

        private void OnDirectorStopped(PlayableDirector _) => Finish();

        private void LateUpdate()
        {
            // Timeline 通知の評価中は Director を停止せず、フレーム内の評価完了を待つ。
            if (!_finishing) { return; }
            _finishing = false;
            Finished?.Invoke();
        }

        private void OnDisable() => EndPresentation();

        internal void EndPresentation()
        {
            if (!_playing) { return; }
            _playing = false;
            _finishing = false;
            if (_skip != null)
            {
                _skip.clicked -= Finish;
                _skip = null;
            }
            _backdrop?.RemoveFromHierarchy();
            _backdrop = null;
            if (director != null)
            {
                director.stopped -= OnDirectorStopped;
                director.Stop();
            }
            if (eventCamera != null) { eventCamera.enabled = false; }
        }
    }
}
