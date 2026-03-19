using UnityEngine;

namespace Game.Core
{
    public enum WarpTarget : byte
    {
        Behind,
        Beside,
        NearAlly,
    }

    /// <summary>
    /// Coop action that warps the companion to a position relative to the target.
    /// Combo 0 = Behind, Combo 1 = Beside.
    /// </summary>
    public class WarpCoopAction : CoopActionBase
    {
        public override string ActionName => "Warp";
        public override int MpCost => 15;
        public override float CooldownDuration => 12f;
        public override int MaxComboCount => 2;
        public override float ComboInputWindow => 0.8f;

        private WarpTarget[] _comboTargets;
        private SoACharaDataDic _data;

        public Vector2 LastWarpPosition { get; private set; }

        public WarpCoopAction(SoACharaDataDic data)
        {
            _data = data;
            _comboTargets = new WarpTarget[] { WarpTarget.Behind, WarpTarget.Beside };
        }

        public override void ExecuteCombo(int comboIndex, int companionHash, int targetHash)
        {
            if (!_data.TryGetValue(companionHash, out int _) || !_data.TryGetValue(targetHash, out int _))
            {
                return;
            }

            ref CharacterVitals targetV = ref _data.GetVitals(targetHash);
            WarpTarget warpType = comboIndex < _comboTargets.Length
                ? _comboTargets[comboIndex]
                : WarpTarget.Behind;

            Vector2 warpPos = CalculateWarpPosition(targetV.position, warpType);
            ref CharacterVitals companionV = ref _data.GetVitals(companionHash);
            companionV.position = warpPos;
            LastWarpPosition = warpPos;
        }

        /// <summary>
        /// Calculates the warp destination relative to the target position.
        /// </summary>
        public static Vector2 CalculateWarpPosition(Vector2 targetPos, WarpTarget warpType)
        {
            switch (warpType)
            {
                case WarpTarget.Behind:
                    return targetPos + new Vector2(-1.5f, 0f);
                case WarpTarget.Beside:
                    return targetPos + new Vector2(0f, 1.5f);
                case WarpTarget.NearAlly:
                    return targetPos + new Vector2(2f, 0f);
                default:
                    return targetPos;
            }
        }
    }
}
