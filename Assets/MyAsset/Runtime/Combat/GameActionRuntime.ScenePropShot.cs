using System;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        internal bool TryGetPropShot(AttackInfo attack, out BulletProfile profile, out GameObject projectilePrefab)
        {
            profile = default;
            projectilePrefab = null;
            if (attack == null) { return false; }
            int id;
            try { id = _catalog.Register(attack); }
            catch (ArgumentException) { return false; }
            if (!_catalog.TryGet(id, out ActionCatalog.Entry entry) || !entry.projectile || entry.multiLockRange != 0f
                || entry.definition.anticipationDuration != 0f || entry.definition.transitions.Count != 0) { return false; }

            MagicDefinition magic = entry.CreateMagic();
            BulletProfile bullet = magic.bulletProfile;
            if (magic.magicType != MagicType.Attack || magic.targetPolicy.Resolve() != AttackTargetRelation.Hostile
                || magic.bulletCount != 1 || magic.groundImpactRange != 0f
                || bullet.moveType != BulletMoveType.Straight || !(bullet.speed > 0f) || !(bullet.lifeTime > 0f)
                || !float.IsFinite(bullet.speed * bullet.lifeTime) || bullet.hitLimit != 1
                || bullet.features != BulletFeature.None || bullet.spawnOffset != Vector2.zero) { return false; }

            profile = bullet;
            projectilePrefab = magic.projectilePrefab;
            return true;
        }
    }
}
