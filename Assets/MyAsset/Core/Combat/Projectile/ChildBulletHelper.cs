using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 子弾生成のピュアロジック。
    /// MagicDefinition差し替え、タイマーチェック、子弾有無判定を提供する。
    /// </summary>
    public static class ChildBulletHelper
    {
        /// <summary>公開照会用の独立コピー。固定tick内の計算・生成には使用しない。</summary>
        public static MagicDefinition DeepCopyForInspection(in MagicDefinition magic)
        {
            MagicDefinition copy = magic;
            copy.statusEffects = CopyEffects(magic.statusEffects);
            copy.equipmentStatusEffects = CopyEffects(magic.equipmentStatusEffects);
            if (magic.childBullet != null)
            {
                copy.childBullet = new ChildBulletConfig
                {
                    trigger = magic.childBullet.trigger,
                    profile = magic.childBullet.profile,
                    count = magic.childBullet.count,
                    spreadAngle = magic.childBullet.spreadAngle,
                };
            }
            return copy;
        }

        private static StatusEffectApplicationData[] CopyEffects(IReadOnlyList<StatusEffectApplicationData> effects)
        {
            if (effects == null) { return null; }
            if (effects.Count == 0) { return System.Array.Empty<StatusEffectApplicationData>(); }
            StatusEffectApplicationData[] copy = new StatusEffectApplicationData[effects.Count];
            for (int i = 0; i < copy.Length; i++) { copy[i] = effects[i]; }
            return copy;
        }

        /// <summary>
        /// 親のMagicDefinitionを基に子弾用MagicDefinitionを生成する。
        /// BulletProfileは子弾設定のものに差し替え、childBulletはnullにして無限再帰を防止する。
        /// </summary>
        public static MagicDefinition CreateChildMagic(in MagicDefinition parentMagic, ChildBulletConfig config)
        {
            MagicDefinition childMagic = parentMagic;
            childMagic.bulletProfile = config.profile;
            childMagic.childBullet = null;
            return childMagic;
        }

        /// <summary>
        /// MagicDefinitionに有効な子弾設定があるか判定する。
        /// </summary>
        public static bool HasChildBullet(in MagicDefinition magic)
        {
            return magic.childBullet != null
                && magic.childBullet.trigger != ChildBulletTrigger.None
                && magic.childBullet.count > 0;
        }

        /// <summary>
        /// OnTimerトリガーの発射タイミングを判定する。
        /// </summary>
        public static bool ShouldEmitOnTimer(float elapsedTime, float lastEmitTime, float emitInterval)
        {
            if (emitInterval <= 0f)
            {
                return false;
            }

            return elapsedTime - lastEmitTime >= emitInterval;
        }
    }
}
