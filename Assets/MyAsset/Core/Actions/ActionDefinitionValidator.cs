using System;

namespace Game.Core
{
    /// <summary>実行定義を登録する前の検証。入力の配列や値は変更しない。</summary>
    public static class ActionDefinitionValidator
    {
        public static void Validate(in ActionDefinitionData data)
        {
            if (!TryValidate(data, out string error))
            {
                throw new ArgumentException(error, nameof(data));
            }
        }

        /// <summary>
        /// 遷移先は正のIDを要求する。登録済みかどうかは循環する定義を段階登録できるよう呼出側で判定する。
        /// cancelAfter/cancelPointの有限負値、0秒、null配列は既存の省略・無効指定として受理する。
        /// </summary>
        public static bool TryValidate(in ActionDefinitionData data, out string error)
        {
            if (data.id < 0) { return Reject("id must be zero or a positive registered ID.", out error); }
            if (!FiniteNonNegative(data.staminaCost)) { return Reject("staminaCost must be finite and nonnegative.", out error); }
            if (data.mpCost < 0) { return Reject("mpCost must be nonnegative.", out error); }
            if (!FiniteNonNegative(data.staminaPerSecond)) { return Reject("staminaPerSecond must be finite and nonnegative.", out error); }
            if (!FiniteNonNegative(data.anticipationDuration)) { return Reject("anticipationDuration must be finite and nonnegative.", out error); }
            if (!FiniteNonNegative(data.activeDuration)) { return Reject("activeDuration must be finite and nonnegative.", out error); }
            if (!FiniteNonNegative(data.recoveryDuration)) { return Reject("recoveryDuration must be finite and nonnegative.", out error); }
            if (!Finite(data.anticipationDuration + data.activeDuration + data.recoveryDuration))
            {
                return Reject("The total phase duration must be finite.", out error);
            }
            if (!FiniteNonNegative(data.duration)) { return Reject("duration must be finite and nonnegative.", out error); }
            if (!FiniteNonNegative(data.moveSpeed)) { return Reject("moveSpeed must be finite and nonnegative.", out error); }
            if (!FiniteNonNegative(data.moveStartTime)) { return Reject("moveStartTime must be finite and nonnegative.", out error); }
            if (!FiniteNonNegative(data.moveDuration)) { return Reject("moveDuration must be finite and nonnegative.", out error); }
            if (!Finite(data.moveStartTime + data.moveDuration)) { return Reject("The movement end time must be finite.", out error); }
            if (!Finite(data.cancelAfter)) { return Reject("cancelAfter must be finite.", out error); }
            if (!Finite(data.cancelPoint) || data.cancelPoint > 1f) { return Reject("cancelPoint must be finite and at most one.", out error); }
            if (!Enum.IsDefined(typeof(ActionEndMode), data.endMode)) { return Reject("endMode is not defined.", out error); }
            if (!Enum.IsDefined(typeof(ActionDurationMode), data.durationMode)) { return Reject("durationMode is not defined.", out error); }
            if (!Enum.IsDefined(typeof(ActState), data.actState)) { return Reject("actState is not defined.", out error); }
            if ((data.resources & ~ActionResources.All) != 0) { return Reject("resources contains undefined flags.", out error); }
            const ActionGameplayFlags gameplayFlags = ActionGameplayFlags.Move | ActionGameplayFlags.Guard
                | ActionGameplayFlags.Attack | ActionGameplayFlags.Cast | ActionGameplayFlags.PreserveVelocity;
            if ((data.gameplay & ~gameplayFlags) != 0) { return Reject("gameplay contains undefined flags.", out error); }

            if (data.effects != null)
            {
                for (int i = 0; i < data.effects.Length; i++)
                {
                    ActionEffect effect = data.effects[i];
                    if (!Enum.IsDefined(typeof(ActionEffectType), effect.type))
                    {
                        return Reject($"effects[{i}].type is not defined.", out error);
                    }
                    if (!FiniteNonNegative(effect.startTime) || !FiniteNonNegative(effect.duration)
                        || !Finite(effect.startTime + effect.duration))
                    {
                        return Reject($"effects[{i}] requires finite nonnegative times and a finite end time.", out error);
                    }
                    if (!FiniteNonNegative(effect.value))
                    {
                        return Reject($"effects[{i}].value must be finite and nonnegative.", out error);
                    }
                }
            }
            if (data.transitions != null)
            {
                for (int i = 0; i < data.transitions.Length; i++)
                {
                    ActionTransition transition = data.transitions[i];
                    if (!Enum.IsDefined(typeof(ActionSignalKind), transition.trigger))
                    {
                        return Reject($"transitions[{i}].trigger is not defined.", out error);
                    }
                    if (transition.nextDefinitionId <= 0)
                    {
                        return Reject($"transitions[{i}].nextDefinitionId must be positive.", out error);
                    }
                    if (!FiniteNonNegative(transition.minElapsed))
                    {
                        return Reject($"transitions[{i}].minElapsed must be finite and nonnegative.", out error);
                    }
                }
            }
            error = null;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool FiniteNonNegative(float value) => value >= 0f && !float.IsInfinity(value);
        private static bool Reject(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }
}
