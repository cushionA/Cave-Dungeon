using System.Collections.Generic;

namespace Game.Core
{
    public enum MenuScreen : byte
    {
        None,
        Inventory,
        Equipment,
        Status,
        Map,
        Settings
    }

    /// <summary>
    /// メニュー画面スタック管理。Push/Popで画面遷移を制御する。
    /// </summary>
    public class MenuStackManager
    {
        private readonly Stack<MenuScreen> _stack;

        public MenuScreen CurrentScreen => _stack.Count > 0 ? _stack.Peek() : MenuScreen.None;
        public bool IsOpen => _stack.Count > 0;
        public int Depth => _stack.Count;

        public MenuStackManager()
        {
            _stack = new Stack<MenuScreen>();
        }

        public void Push(MenuScreen screen)
        {
            _stack.Push(screen);
        }

        /// <summary>スタックからPopして返す。空ならNoneを返す。</summary>
        public MenuScreen Pop()
        {
            if (_stack.Count == 0)
            {
                return MenuScreen.None;
            }

            return _stack.Pop();
        }

        public void CloseAll()
        {
            _stack.Clear();
        }
    }
}
