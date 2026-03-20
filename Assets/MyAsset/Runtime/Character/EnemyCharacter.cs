using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 敵キャラクターMonoBehaviour。
    /// AI行動はEnemyControllerが担当する。
    /// このクラスはSoA登録・接地判定・位置同期のみを行う。
    /// </summary>
    public class EnemyCharacter : BaseCharacter
    {
        private DamageDealer _damageDealer;

        public DamageDealer DamageDealer => _damageDealer;

        protected override void Awake()
        {
            base.Awake();
            _damageDealer = GetComponentInChildren<DamageDealer>();
        }

        protected override void Start()
        {
            base.Start();
            CharacterRegistry.RegisterEnemy(ObjectHash);
        }

        private void FixedUpdate()
        {
            if (!IsAlive)
            {
                return;
            }

            UpdateGroundCheck();
            SyncPositionToData();
        }

        protected override void OnDestroy()
        {
            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }
    }
}
