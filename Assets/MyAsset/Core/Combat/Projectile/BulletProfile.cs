using System;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct BulletProfile
    {
        public BulletMoveType moveType;
        public float speed;
        public float acceleration;
        public float angle;
        public float spreadAngle;
        public float lifeTime;
        public int hitLimit;

        /// <summary>各ターゲットに与える最大ヒット数。0 以下の場合は 1 として扱う (既定動作互換)。
        /// 総ヒット上限 <see cref="hitLimit"/> と組み合わせて二段管理する。
        /// NOTE: この上限は <see cref="ProjectileController.TryRegisterHit"/> 経路でのみ適用される。
        /// AoE 爆発 (<c>ProjectileManager.ProcessExplosion</c>) では
        /// 範囲内の全キャラクターに 1 回ずつヒットするため本値は参照されない。</summary>
        [Tooltip("各ターゲットに与える最大ヒット数。0/未設定は 1 扱い。爆発処理では適用されない (爆発は範囲内全員が 1 回被弾)")]
        public int perTargetHitLimit;

        public float emitInterval;
        public BulletFeature features;
        public float explodeRadius;
        public Vector2 knockbackForce;

        // --- 弾丸拡張 (C# 9 のため struct 初期化子は使えない。既定値ゼロを「無効/互換」と解釈する) ---

        /// <summary>発射時スケール。0 の場合は 1f として扱う（既定動作互換）。</summary>
        public float startScale;

        /// <summary>終了時スケール。0 の場合は 1f として扱う（既定動作互換）。</summary>
        public float endScale;

        /// <summary>スケール補間に使う秒数。0 以下で補間なし（互換）。</summary>
        public float scaleTime;

        /// <summary>スポーンから実際に移動・当たり判定が有効化されるまでの遅延秒数。0 で即時（互換）。</summary>
        public float spawnDelay;

        /// <summary>発射方向ローカル座標系でのスポーンオフセット。
        /// x=forward (発射方向), y=up (forward を 90 度反時計回りに回転した方向)。</summary>
        public Vector2 spawnOffset;

        /// <summary>ホーミング追尾力。0 の場合は既定 5f（旧ハードコード値）にフォールバック。</summary>
        public float homingStrength;

        /// <summary>homingStrength の時間変動量。0 で一定（互換）。</summary>
        public float homingAcceleration;

        /// <summary>startScale を安全に取得する（0 は 1f として扱う）。</summary>
        public float GetEffectiveStartScale()
        {
            return startScale == 0f ? 1f : startScale;
        }

        /// <summary>endScale を安全に取得する（0 は 1f として扱う）。</summary>
        public float GetEffectiveEndScale()
        {
            return endScale == 0f ? 1f : endScale;
        }

        /// <summary>homingStrength を安全に取得する（0 は旧ハードコード 5f にフォールバック）。</summary>
        public float GetEffectiveHomingStrength()
        {
            return homingStrength == 0f ? 5f : homingStrength;
        }

        /// <summary>perTargetHitLimit を安全に取得する（0 以下は 1 として扱い、旧 HashSet 互換にフォールバック）。</summary>
        public int GetEffectivePerTargetHitLimit()
        {
            return perTargetHitLimit > 0 ? perTargetHitLimit : 1;
        }
    }
}
