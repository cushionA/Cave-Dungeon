using UnityEngine;
using Game.Core;
using R3;
using System;
using Random = UnityEngine.Random;
using System.Collections.Generic;

namespace Game.Runtime
{
    /// <summary>
    /// ダメージポップアップ表示コントローラ。
    /// GameManager.Events.OnDamageDealtを購読し、ワールド空間にダメージ数値を表示する。
    /// 軽量実装：TextMesh使用（UI Toolkit非依存）。
    /// </summary>
    public class DamagePopupController : MonoBehaviour
    {
        [SerializeField] private int _poolSize = 10;
        [SerializeField] private float _floatSpeed = 2.0f;
        [SerializeField] private float _duration = 0.8f;
        [SerializeField] private float _fadeSpeed = 2.0f;

        private List<PopupInstance> _activePopups;
        private Queue<GameObject> _pool;
        private IDisposable _subscription;

        private struct PopupInstance
        {
            public GameObject gameObject;
            public TextMesh textMesh;
            public float remainingTime;
            public Vector3 velocity;
        }

        private void Awake()
        {
            _activePopups = new List<PopupInstance>();
            _pool = new Queue<GameObject>();

            // プール初期化
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

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                PopupInstance popup = _activePopups[i];
                popup.remainingTime -= deltaTime;

                if (popup.remainingTime <= 0f)
                {
                    popup.gameObject.SetActive(false);
                    _pool.Enqueue(popup.gameObject);
                    _activePopups.RemoveAt(i);
                    continue;
                }

                // 上に浮かぶ
                popup.gameObject.transform.position += popup.velocity * deltaTime;

                // フェードアウト
                float alpha = Mathf.Clamp01(popup.remainingTime * _fadeSpeed);
                Color color = popup.textMesh.color;
                color.a = alpha;
                popup.textMesh.color = color;

                _activePopups[i] = popup;
            }
        }

        private void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            if (result.totalDamage <= 0)
            {
                return;
            }

            // 被ダメ者の位置を取得
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
        /// ダメージポップアップを生成する。
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

            go.SetActive(true);
            go.transform.position = new Vector3(
                data.worldPosition.x + Random.Range(-0.3f, 0.3f),
                data.worldPosition.y + 1.0f,
                0f);

            TextMesh textMesh = go.GetComponent<TextMesh>();
            textMesh.text = data.value.ToString();

            switch (data.type)
            {
                case FeedbackType.Normal:
                    textMesh.color = Color.white;
                    textMesh.fontSize = 40;
                    break;
                case FeedbackType.Critical:
                    textMesh.color = new Color(1f, 0.8f, 0.2f);
                    textMesh.fontSize = 50;
                    break;
                case FeedbackType.Heal:
                    textMesh.color = new Color(0.3f, 1f, 0.3f);
                    textMesh.fontSize = 40;
                    break;
            }

            PopupInstance instance = new PopupInstance
            {
                gameObject = go,
                textMesh = textMesh,
                remainingTime = _duration,
                velocity = new Vector3(Random.Range(-0.5f, 0.5f), _floatSpeed, 0f)
            };

            _activePopups.Add(instance);
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

            // ソートレイヤー最前面
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 100;

            return go;
        }
    }
}
