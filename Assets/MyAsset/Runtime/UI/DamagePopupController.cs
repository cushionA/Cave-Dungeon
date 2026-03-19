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

        private Queue<GameObject> _pool;
        private IDisposable _subscription;

        private void Awake()
        {
            _pool = new Queue<GameObject>();

            for (int i = 0; i < _poolSize; i++)
            {
                GameObject go = CreatePopupObject();
                go.SetActive(false);
                _pool.Enqueue(go);
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
            GameObject go;
            if (_pool.Count > 0)
            {
                go = _pool.Dequeue();
            }
            else
            {
                go = CreatePopupObject();
            }

            // 初期位置設定
            float offsetX = Random.Range(-_horizontalSpread, _horizontalSpread);
            Vector3 startPos = new Vector3(
                data.worldPosition.x + offsetX,
                data.worldPosition.y + 0.5f,
                0f);

            go.transform.position = startPos;
            go.transform.localScale = Vector3.one;
            go.SetActive(true);

            TextMesh textMesh = go.GetComponent<TextMesh>();
            textMesh.text = data.value.ToString();

            // タイプ別の色・サイズ設定
            Color baseColor;
            switch (data.type)
            {
                case FeedbackType.Critical:
                    baseColor = new Color(1f, 0.8f, 0.2f, 1f);
                    textMesh.fontSize = 50;
                    break;
                case FeedbackType.Heal:
                    baseColor = new Color(0.3f, 1f, 0.3f, 1f);
                    textMesh.fontSize = 40;
                    break;
                default:
                    baseColor = Color.white;
                    textMesh.fontSize = 40;
                    break;
            }
            textMesh.color = baseColor;

            // --- LitMotion トゥイーン ---
            Transform popupTransform = go.transform;

            // 1. Y方向に浮かぶ（EaseOutCubicで減速）
            Vector3 endPos = startPos + new Vector3(0f, _floatHeight, 0f);
            LMotion.Create(startPos, endPos, _duration)
                .WithEase(Ease.OutCubic)
                .BindWithState(popupTransform, (pos, t) =>
                {
                    t.position = pos;
                });

            // 2. フェードアウト（後半で加速）
            LMotion.Create(1f, 0f, _duration * 0.7f)
                .WithEase(Ease.InQuad)
                .WithDelay(_duration * 0.3f)
                .BindWithState(textMesh, (alpha, tm) =>
                {
                    Color c = tm.color;
                    c.a = alpha;
                    tm.color = c;
                });

            // 3. クリティカル時のスケールパンチ
            if (data.type == FeedbackType.Critical)
            {
                LMotion.Create(_criticalScale, 1f, _duration * 0.4f)
                    .WithEase(Ease.OutBack)
                    .BindWithState(popupTransform, (scale, t) =>
                    {
                        t.localScale = new Vector3(scale, scale, 1f);
                    });
            }

            // 4. 完了後にプールへ返却
            LMotion.Create(0f, 1f, _duration)
                .WithOnComplete(() =>
                {
                    go.SetActive(false);
                    _pool.Enqueue(go);
                })
                .RunWithoutBinding();
        }

        private GameObject CreatePopupObject()
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

            return go;
        }
    }
}
