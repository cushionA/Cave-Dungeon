using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.Core;

namespace Game.Editor
{
    /// <summary>
    /// Physics2D Collision Matrixをアーキテクチャ定義に従って設定するエディタツール。
    /// Architect/08_物理レイヤー定義.md 参照。
    /// </summary>
    public static class CollisionMatrixSetup
    {
        private const int k_Ground = 6;
        private const int k_PlayerHitbox = 10;
        private const int k_EnemyHitbox = 11;
        private const int k_CharaPassThrough = GameConstants.k_LayerCharaPassThrough;
        private const int k_CharaCollide = GameConstants.k_LayerCharaCollide;
        private const int k_CharaInvincible = GameConstants.k_LayerCharaInvincible;

        /// <summary>
        /// 期待される IgnoreLayerCollision 設定のエントリ。
        /// Apply と Validate の両方で同じ期待値テーブルを参照する。
        /// </summary>
        public readonly struct ExpectedPair
        {
            public readonly int layerA;
            public readonly int layerB;
            /// <summary>true なら衝突無視 (IgnoreLayerCollision=true)、false なら衝突有効 (=false)。</summary>
            public readonly bool shouldIgnore;

            public ExpectedPair(int layerA, int layerB, bool shouldIgnore)
            {
                this.layerA = layerA;
                this.layerB = layerB;
                this.shouldIgnore = shouldIgnore;
            }
        }

        /// <summary>
        /// アーキテクチャ定義に従った期待値テーブルを返す。
        /// </summary>
        public static IReadOnlyList<ExpectedPair> GetExpectedPairs()
        {
            List<ExpectedPair> pairs = new List<ExpectedPair>();

            // 全キャラ関連レイヤー同士 = 既定で衝突無視
            int[] charaLayers = { k_CharaPassThrough, k_CharaCollide, k_CharaInvincible };
            for (int i = 0; i < charaLayers.Length; i++)
            {
                for (int j = 0; j < charaLayers.Length; j++)
                {
                    pairs.Add(new ExpectedPair(charaLayers[i], charaLayers[j], true));
                }
            }

            // CharaPassThrough (12): 地形+Hitbox検知、キャラ同士すり抜け
            pairs.Add(new ExpectedPair(k_CharaPassThrough, k_Ground, false));
            pairs.Add(new ExpectedPair(k_CharaPassThrough, k_PlayerHitbox, false));
            pairs.Add(new ExpectedPair(k_CharaPassThrough, k_EnemyHitbox, false));

            // CharaCollide (13): 地形+Hitbox+キャラ同士衝突 (PassThrough含む)
            pairs.Add(new ExpectedPair(k_CharaCollide, k_Ground, false));
            pairs.Add(new ExpectedPair(k_CharaCollide, k_PlayerHitbox, false));
            pairs.Add(new ExpectedPair(k_CharaCollide, k_EnemyHitbox, false));
            pairs.Add(new ExpectedPair(k_CharaCollide, k_CharaCollide, false));
            pairs.Add(new ExpectedPair(k_CharaCollide, k_CharaPassThrough, false));

            // CharaInvincible (14): 地形のみ、Hitbox・キャラすべてすり抜け
            pairs.Add(new ExpectedPair(k_CharaInvincible, k_Ground, false));

            return pairs;
        }

        [MenuItem("Game/Physics2D Collision Matrix設定")]
        public static void SetupCollisionMatrix()
        {
            // 期待値テーブルを順に適用する。
            // 後勝ちで上書きされるため、広い範囲 (全キャラ同士無効化) を先に、
            // 個別の例外 (CharaCollide ↔ CharaCollide 衝突有効など) を後に指定すること。
            foreach (ExpectedPair pair in GetExpectedPairs())
            {
                Physics2D.IgnoreLayerCollision(pair.layerA, pair.layerB, pair.shouldIgnore);
            }

            Debug.Log("[CollisionMatrixSetup] Physics2D Collision Matrixを設定しました");
        }

        /// <summary>
        /// 現在の Physics2D.IgnoreLayerCollision 設定を期待値テーブルと照合する。
        /// 不一致があれば LogError を出すだけで、設定自体は変更しない。
        /// </summary>
        /// <returns>不一致件数。0 なら期待通り。</returns>
        public static int ValidateCollisionMatrix()
        {
            // 期待値テーブルは後勝ちで上書きされる構造のため、
            // (layerA, layerB) の正規化キーごとに最終期待値を畳み込んでから照合する。
            Dictionary<(int, int), bool> resolvedExpectation = new Dictionary<(int, int), bool>();
            foreach (ExpectedPair pair in GetExpectedPairs())
            {
                int a = Mathf.Min(pair.layerA, pair.layerB);
                int b = Mathf.Max(pair.layerA, pair.layerB);
                resolvedExpectation[(a, b)] = pair.shouldIgnore;
            }

            int mismatchCount = 0;
            foreach (KeyValuePair<(int, int), bool> kv in resolvedExpectation)
            {
                int a = kv.Key.Item1;
                int b = kv.Key.Item2;
                bool expected = kv.Value;
                bool actual = Physics2D.GetIgnoreLayerCollision(a, b);
                if (actual != expected)
                {
                    Debug.LogError($"[CollisionMatrixSetup] Layer {a} <-> {b} のコリジョン設定が期待値と異なります " +
                                   $"(expected IgnoreLayerCollision={expected}, actual={actual})");
                    mismatchCount++;
                }
            }

            return mismatchCount;
        }
    }

    /// <summary>
    /// Editor 起動時に Collision Matrix の期待値との差分を検出する。
    /// 設定自体は変更せず、差分があれば LogError を出して開発者に通知する。
    /// </summary>
    [InitializeOnLoad]
    public static class CollisionMatrixValidator
    {
        static CollisionMatrixValidator()
        {
            // ドメインリロード直後は他の InitializeOnLoad の競合を避けるため delayCall で1フレーム遅延させる
            EditorApplication.delayCall += () =>
            {
                CollisionMatrixSetup.ValidateCollisionMatrix();
            };
        }
    }
}
