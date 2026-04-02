using UnityEngine;
using Game.Core;
using R3;
using System;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using LitMotion;

namespace Game.Runtime
{
    /// <summary>
    /// ダメージポップアップ表示コントローラ。
    /// GameManager.Events.OnDamageDealtを購読し、ワールド空間にダメージ数値を表示する。
    /// LitMotionでトゥイーン駆動（Update手動管理を排除）。
    /// </summary>
    public class DamagePopupController : MonoBehaviour
    {
        [Header("Pool")]
        [SerializeField] private int _poolSize = 10;

        [Header("Animation")]
        [SerializeField] private float _duration = 0.8f;
        [SerializeField] private float _floatHeight = 1.5f;
        [SerializeField] private float _horizontalSpread = 0.5f;
        [SerializeField] private float _criticalScale = 1.4f;

        [Header("Colors")]
        [SerializeField] private Color _criticalColor = new Color(1f, 0.8f, 0.2f, 1f);
        [SerializeField] private Color _healColor = new Color(0.3f, 1f, 0.3f, 1f);

        private struct PoolEntry
        {
            public GameObject gameObject;
            public TextMesh textMesh;
            public Transform transform;
            public MotionHandle positionHandle;
            public MotionHandle fadeHandle;
            public MotionHandle scaleHandle;
            public MotionHandle completionHandle;
        }

        private Queue<PoolEntry> _pool;
        private List<PoolEntry> _active;
        private IDisposable _subscription;

        private void Awake()
        {
            _pool = new Queue<PoolEntry>();
            _active = new List<PoolEntry>();

            for (int i = 0; i < _poolSize; i++)
            {
                PoolEntry entry = CreatePoolEntry();
                entry.gameObject.SetActive(false);
                _pool.Enqueue(entry);
            }
        }

        private void OnEnable()
        {
            if (GameManager.Events != null)
            {
                _subscription = GameManager.Events.OnDamageDealt
                    .Subscribe(e => OnDamageDealt(e.result, e.attackerHash, e.defenderHash));
            }
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnDestroy()
        {
            CancelAllActive();
        }

        private void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            if (result.totalDamage <= 0)
            {
                return;
            }

            Vector2 position = Vector2.zero;
            if (GameManager.Data != null && GameManager.Data.TryGetValue(defenderHash, out int _))
            {
                position = GameManager.Data.GetVitals(defenderHash).position;
            }

            DamagePopupData popupData = BattleFeedbackFactory.CreateDamagePopup(
                result.totalDamage, result.isCritical, position);

            SpawnPopup(popupData);
        }

        /// <summary>
        /// ダメージポップアップを生成し、LitMotionでアニメーションする。
        /// </summary>
        public void SpawnPopup(DamagePopupData data)
        {
            PoolEntry entry;
            if (_pool.Count > 0)
            {
                entry = _pool.Dequeue();
            }
            else
            {
                entry = CreatePoolEntry();
            }

            // 実行中のトゥイーンをキャンセル
            CancelHandles(ref entry);

            // 初期位置設定
            float offsetX = Random.Range(-_horizontalSpread, _horizontalSpread);
            Vector3 startPos = new Vector3(
                data.worldPosition.x + offsetX,
                data.worldPosition.y + 0.5f,
                0f);

            entry.transform.position = startPos;
            entry.transform.localScale = Vector3.one;
            entry.gameObject.SetActive(true);

            entry.textMesh.text = data.value.ToString();

            // タイプ別の色・サイズ設定
            Color baseColor;
            switch (data.type)
            {
                case FeedbackType.Critical:
                    baseColor = _criticalColor;
                    entry.textMesh.fontSize = 50;
                    break;
                case FeedbackType.Heal:
                    baseColor = _healColor;
                    entry.textMesh.fontSize = 40;
                    break;
                default:
                    baseColor = Color.white;
                    entry.textMesh.fontSize = 40;
                    break;
            }
            entry.textMesh.color = baseColor;

            // --- LitMotion トゥイーン ---
            // 1. Y方向に浮かぶ（EaseOutCubicで減速）
            Vector3 endPos = startPos + new Vector3(0f, _floatHeight, 0f);
            entry.positionHandle = LMotion.Create(startPos, endPos, _duration)
                .WithEase(Ease.OutCubic)
                .Bind(entry.transform, (pos, t) =>
                {
                    t.position = pos;
                });

            // 2. フェードアウト（後半で加速）
            entry.fadeHandle = LMotion.Create(1f, 0f, _duration * 0.7f)
                .WithEase(Ease.InQuad)
                .WithDelay(_duration * 0.3f)
                .Bind(entry.textMesh, (alpha, tm) =>
                {
                    Color c = tm.color;
                    c.a = alpha;
                    tm.color = c;
                });

            // 3. クリティカル時のスケールパンチ
            if (data.type == FeedbackType.Critical)
            {
                entry.scaleHandle = LMotion.Create(_criticalScale, 1f, _duration * 0.4f)
                    .WithEase(Ease.OutBack)
                    .Bind(entry.transform, (scale, t) =>
                    {
                        t.localScale = new Vector3(scale, scale, 1f);
                    });
            }

            // 4. 完了後にプールへ返却
            PoolEntry capturedEntry = entry;
            entry.completionHandle = LMotion.Create(0f, 1f, _duration)
                .WithOnComplete(() =>
                {
                    ReturnToPool(capturedEntry);
                })
                .RunWithoutBinding();

            _active.Add(entry);
        }

        private void ReturnToPool(PoolEntry entry)
        {
            entry.gameObject.SetActive(false);
            _active.Remove(entry);
            _pool.Enqueue(entry);
        }

        private PoolEntry CreatePoolEntry()
        {
            GameObject go = new GameObject("DamagePopup");
            go.transform.SetParent(transform);

            TextMesh textMesh = go.AddComponent<TextMesh>();
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.characterSize = 0.1f;
            textMesh.fontSize = 40;
            textMesh.color = Color.white;
            textMesh.fontStyle = FontStyle.Bold;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 100;

            return new PoolEntry
            {
                gameObject = go,
                textMesh = textMesh,
                transform = go.transform
            };
        }

        private static void CancelHandles(ref PoolEntry entry)
        {
            CancelHandle(ref entry.positionHandle);
            CancelHandle(ref entry.fadeHandle);
            CancelHandle(ref entry.scaleHandle);
            CancelHandle(ref entry.completionHandle);
        }

        private static void CancelHandle(ref MotionHandle handle)
        {
            if (handle.IsActive())
            {
                handle.Cancel();
            }
            handle = default;
        }

        private void CancelAllActive()
        {
            if (_active == null)
            {
                return;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                PoolEntry entry = _active[i];
                CancelHandles(ref entry);
            }
            _active.Clear();
        }
    }
}
