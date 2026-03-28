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

        [MenuItem("Game/Physics2D Collision Matrix設定")]
        public static void SetupCollisionMatrix()
        {
            // まず全キャラ関連レイヤー同士の衝突を無効化
            int[] charaLayers = { k_CharaPassThrough, k_CharaCollide, k_CharaInvincible };
            foreach (int a in charaLayers)
            {
                foreach (int b in charaLayers)
                {
                    Physics2D.IgnoreLayerCollision(a, b, true);
                }
            }

            // --- CharaPassThrough (12): 地形+Hitbox検知、キャラ同士すり抜け ---
            Physics2D.IgnoreLayerCollision(k_CharaPassThrough, k_Ground, false);
            Physics2D.IgnoreLayerCollision(k_CharaPassThrough, k_PlayerHitbox, false);
            Physics2D.IgnoreLayerCollision(k_CharaPassThrough, k_EnemyHitbox, false);
            // CharaPassThrough同士 = すり抜け（上で無効化済み）

            // --- CharaCollide (13): 地形+Hitbox+キャラ同士衝突 ---
            Physics2D.IgnoreLayerCollision(k_CharaCollide, k_Ground, false);
            Physics2D.IgnoreLayerCollision(k_CharaCollide, k_PlayerHitbox, false);
            Physics2D.IgnoreLayerCollision(k_CharaCollide, k_EnemyHitbox, false);
            Physics2D.IgnoreLayerCollision(k_CharaCollide, k_CharaCollide, false);
            Physics2D.IgnoreLayerCollision(k_CharaCollide, k_CharaPassThrough, false);
            // CharaCollide ↔ CharaInvincible = すり抜け（上で無効化済み）

            // --- CharaInvincible (14): 地形のみ、Hitbox・キャラすべてすり抜け ---
            Physics2D.IgnoreLayerCollision(k_CharaInvincible, k_Ground, false);
            // Hitbox/キャラは全部すり抜け（上で無効化済み）

            Debug.Log("[CollisionMatrixSetup] Physics2D Collision Matrixを設定しました");
        }
    }
}
