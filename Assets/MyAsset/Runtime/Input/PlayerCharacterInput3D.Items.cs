using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine.InputSystem;

namespace Game.Runtime
{
    public sealed partial class PlayerCharacterInput3D
    {
        private enum ItemPressKind : byte { Use, Previous, Next }

        private readonly struct ItemPress
        {
            public readonly InputAction Action;
            public readonly ActorRef Owner;
            public readonly ItemPressKind Kind;

            public ItemPress(InputAction action, ActorRef owner, ItemPressKind kind)
            {
                Action = action;
                Owner = owner;
                Kind = kind;
            }
        }

        private PlayerItemUse3D _itemUse;
        private InputAction _itemUseAction;
        private InputAction _padItemUseAction;
        private InputAction _previousItemAction;
        private InputAction _nextItemAction;
        private readonly Queue<ItemPress> _pendingItemInputs = new Queue<ItemPress>(4);

        private void BindItems(InputActionMap map)
        {
            BindItemAction(ref _itemUseAction, map?.FindAction("ItemUse"), QueueItemUse);
            // Native 3D uses the otherwise unconsumed B/○ binding, leaving keyboard C and 2D bindings as authored.
            BindItemAction(ref _padItemUseAction, map?.FindAction("Crouch"), QueueGamepadItemUse);
            BindItemAction(ref _previousItemAction, map?.FindAction("Previous"), QueuePreviousItem);
            BindItemAction(ref _nextItemAction, map?.FindAction("Next"), QueueNextItem);
        }

        private static void BindItemAction(ref InputAction bound, InputAction action, Action<InputAction.CallbackContext> handler)
        {
            if (bound != null) { bound.performed -= handler; }
            bound = action;
            if (bound != null) { bound.performed += handler; }
        }

        private void QueueItemUse(InputAction.CallbackContext context) => QueueItemPress(context.action, ItemPressKind.Use);

        private void QueueGamepadItemUse(InputAction.CallbackContext context)
        {
            if (context.control?.device is Gamepad) { QueueItemUse(context); }
        }

        private void QueuePreviousItem(InputAction.CallbackContext context) => QueueItemPress(context.action, ItemPressKind.Previous);

        private void QueueNextItem(InputAction.CallbackContext context) => QueueItemPress(context.action, ItemPressKind.Next);

        private void QueueItemPress(InputAction action, ItemPressKind kind)
        {
            if (TryGetItemRequester(out ActorRef owner))
            {
                _pendingItemInputs.Enqueue(new ItemPress(action, owner, kind));
            }
        }

        private void CancelItemInputIfDisabled(object action)
        {
            if (ReferenceEquals(action, _itemUseAction) || ReferenceEquals(action, _padItemUseAction)
                || ReferenceEquals(action, _previousItemAction) || ReferenceEquals(action, _nextItemAction))
            {
                _pendingItemInputs.Clear();
            }
        }

        private void ConsumeItemInput()
        {
            while (_pendingItemInputs.TryDequeue(out ItemPress press))
            {
                if (!press.Action.enabled || !TryGetItemRequester(out ActorRef owner) || owner != press.Owner) { continue; }
                switch (press.Kind)
                {
                    case ItemPressKind.Use:
                        _itemUse.TryUseActive();
                        break;
                    case ItemPressKind.Previous:
                        GameManager.ConsumableBelt?.Prev();
                        break;
                    case ItemPressKind.Next:
                        GameManager.ConsumableBelt?.Next();
                        break;
                }
            }
        }

        private bool TryGetItemRequester(out ActorRef owner)
        {
            owner = default;
            if (!isActiveAndEnabled || _playerInput.currentActionMap != _gameplayMap
                || _itemUse == null || !_itemUse.isActiveAndEnabled
                || _actor == null || !_actor.isActiveAndEnabled || !_actor.IsAlive)
            {
                return false;
            }
            owner = _actor.Actor;
            return owner.IsValid;
        }
    }
}
