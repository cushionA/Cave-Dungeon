using System;

namespace Game.Core
{
    public class GateStateController
    {
        private GateDefinition _definition;
        private bool _isOpen;

        public GateDefinition Definition => _definition;
        public bool IsOpen => _isOpen;
        public string HintText { get; set; }

        public event Action<string> OnGateOpened;

        public GateStateController(GateDefinition definition)
        {
            _definition = definition;
            _isOpen = false;
            HintText = GetDefaultHint(definition);
        }

        public bool TryOpen(CharacterFlags playerFlags,
            Func<int, bool> hasItem, Func<string, bool> hasFlag)
        {
            if (_isOpen)
            {
                return true;
            }

            bool canOpen = GateConditionChecker.Evaluate(_definition, playerFlags, hasItem, hasFlag);
            if (canOpen)
            {
                _isOpen = true;
                OnGateOpened?.Invoke(_definition.gateId);
            }

            return canOpen;
        }

        public void ForceOpen()
        {
            _isOpen = true;
            OnGateOpened?.Invoke(_definition.gateId);
        }

        /// <summary>
        /// ゲートを強制クローズする。永続ゲート (isPermanent = true、ボスクリア等) は
        /// ゲーム進行フラグと紐づいているため ForceClose では閉じない。戻り値: 実際に閉じたかどうか。
        /// </summary>
        public bool ForceClose()
        {
            if (_definition.isPermanent)
            {
                return false;
            }
            _isOpen = false;
            return true;
        }

        private static string GetDefaultHint(GateDefinition def)
        {
            switch (def.gateType)
            {
                case GateType.Ability:
                    return $"{def.requiredAbility}が必要";
                case GateType.Key:
                    return "特定のアイテムが必要";
                case GateType.Clear:
                    return "ボスの討伐が必要";
                default:
                    return "";
            }
        }
    }
}
