namespace Game.Core
{
    /// <summary>
    /// キャラクター同士の当たり判定ロジック（純ロジック）。
    /// 通常時はすり抜け、アクション時のみcontactTypeに応じて衝突/運搬を制御する。
    /// </summary>
    public static class CharacterCollisionLogic
    {
        /// <summary>
        /// contactTypeに基づき、移動をブロックする衝突が発生するか判定する。
        /// StopOnHitとCarryは衝突有効、PassThroughはすり抜け。
        /// </summary>
        public static bool ShouldBlockMovement(AttackContactType contactType)
        {
            switch (contactType)
            {
                case AttackContactType.StopOnHit:
                case AttackContactType.Carry:
                    return true;
                case AttackContactType.PassThrough:
                default:
                    return false;
            }
        }

        /// <summary>
        /// 対象を運搬できるか判定する。
        /// 怯み・スタン・ガードブレイク・吹き飛ばし中の敵のみ運搬可能。
        /// </summary>
        public static bool CanCarry(ActState targetState, CharacterBelong targetBelong)
        {
            if (targetBelong != CharacterBelong.Enemy)
            {
                return false;
            }

            switch (targetState)
            {
                case ActState.Flinch:
                case ActState.Stunned:
                case ActState.GuardBroken:
                case ActState.Knockbacked:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 現在の衝突モードを取得する。
        /// アクション非実行中は常にPassThrough（すり抜け）。
        /// </summary>
        public static AttackContactType GetCollisionMode(bool isActionActive, AttackContactType actionContactType)
        {
            if (!isActionActive)
            {
                return AttackContactType.PassThrough;
            }

            return actionContactType;
        }
    }
}
