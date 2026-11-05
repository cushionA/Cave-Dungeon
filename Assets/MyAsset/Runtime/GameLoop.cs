using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Game.Runtime
{
    public sealed class GameLoop : IDisposable
    {
        private sealed class FixedPhase { }
        private sealed class FramePhase { }
        private sealed class LatePhase { }

        private sealed class FrameRegistration
        {
            public MonoBehaviour component;
            public BaseCharacter owner;
            public CharacterAnimationController animation;
        }

        private static GameLoop s_Current;
        private static readonly Dictionary<MonoBehaviour, FrameRegistration> s_FrameComponents = new Dictionary<MonoBehaviour, FrameRegistration>();
        private static readonly List<FrameRegistration> s_FrameSnapshot = new List<FrameRegistration>(256);

        private readonly GameManager _manager;
        private readonly GameActionRuntime _actions;
        private readonly StatusEffectPresentationController _presentation;
        private bool _disposed;

        public GameLoop(GameManager manager, GameActionRuntime actions,
            StatusEffectPresentationController presentation)
        {
            _manager = manager;
            _actions = actions;
            _presentation = presentation;
        }

        public void Install()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemovePhases(ref loop);
            bool fixedInstalled = InsertAfter(ref loop,
                typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
                new PlayerLoopSystem { type = typeof(FixedPhase), updateDelegate = UpdateFixed });
            bool frameInstalled = InsertAfter(ref loop,
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                new PlayerLoopSystem { type = typeof(FramePhase), updateDelegate = UpdateFrame });
            bool lateInstalled = InsertAfter(ref loop,
                typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate),
                new PlayerLoopSystem { type = typeof(LatePhase), updateDelegate = UpdateLate });
            if (!fixedInstalled || !frameInstalled || !lateInstalled)
            {
                throw new InvalidOperationException("The current PlayerLoop is missing a game update phase.");
            }
            PlayerLoop.SetPlayerLoop(loop);
            s_Current = this;
        }

        public static void BindFrameOwner(BaseCharacter character)
        {
            BindFrameOwner(character.GetComponent<CharacterAnimationController>(), character);
        }

        public void TickFixed(float deltaTime, float currentTime)
        {
            if (_disposed) { return; }
            _manager.SimulationTick(deltaTime, currentTime);
        }

        public void TickFrame()
        {
            if (_disposed) { return; }
            TickFrameComponents();
        }

        public static void RegisterFrame(CharacterAnimationController animation)
        {
            BaseCharacter owner = animation.GetComponent<BaseCharacter>();
            if (owner != null) { owner.BindAnimationController(animation); }
            RegisterFrame(animation, owner);
        }

        public static void UnregisterFrame(MonoBehaviour component)
        {
            if (!s_FrameComponents.Remove(component)) { return; }
            if (s_FrameComponents.Count == 0) { s_FrameSnapshot.Clear(); }
            if (s_FrameComponents.Count == 0 && s_Current == null)
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemovePhases(ref loop, true);
                PlayerLoop.SetPlayerLoop(loop);
            }
        }

        public void TickLate()
        {
            if (_disposed) { return; }
            _presentation.Sync();
            _actions.Present();
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            if (ReferenceEquals(s_Current, this))
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemovePhases(ref loop);
                if (s_FrameComponents.Count > 0)
                {
                    InsertAfter(ref loop,
                        typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                        new PlayerLoopSystem { type = typeof(FramePhase), updateDelegate = UpdateFrame });
                }
                PlayerLoop.SetPlayerLoop(loop);
                s_Current = null;
            }
        }

        private static void RegisterFrame(CharacterAnimationController animation, BaseCharacter owner)
        {
            s_FrameComponents[animation] = new FrameRegistration
            {
                component = animation,
                owner = owner,
                animation = animation,
            };
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!ContainsFramePhase(loop))
            {
                if (!InsertAfter(ref loop,
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                    new PlayerLoopSystem { type = typeof(FramePhase), updateDelegate = UpdateFrame }))
                {
                    throw new InvalidOperationException("The current PlayerLoop is missing its frame update phase.");
                }
                PlayerLoop.SetPlayerLoop(loop);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Current = null;
            s_FrameComponents.Clear();
            s_FrameSnapshot.Clear();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemovePhases(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void BindFrameOwner(MonoBehaviour component, BaseCharacter owner)
        {
            if (component != null && s_FrameComponents.TryGetValue(component, out FrameRegistration entry))
            {
                entry.owner = owner;
            }
        }

        private static void TickFrameComponents()
        {
            s_FrameSnapshot.Clear();
            foreach (FrameRegistration entry in s_FrameComponents.Values)
            {
                s_FrameSnapshot.Add(entry);
            }
            for (int i = 0; i < s_FrameSnapshot.Count; i++)
            {
                FrameRegistration entry = s_FrameSnapshot[i];
                if (entry.component == null || !entry.component.isActiveAndEnabled
                    || !s_FrameComponents.TryGetValue(entry.component, out FrameRegistration current)
                    || !ReferenceEquals(current, entry)) { continue; }
                if (entry.owner != null && (!entry.owner.isActiveAndEnabled || !entry.owner.IsRegistered
                    || !GameManager.IsCharacterValid(entry.owner.ObjectHash))) { continue; }
                if (entry.animation != null) { entry.animation.TickFrame(); }
            }
        }

        private void UpdateFixed()
        {
            if (_manager != null && _manager.isActiveAndEnabled)
            {
                TickFixed(Time.fixedDeltaTime, Time.fixedTime);
            }
        }

        private static void UpdateFrame()
        {
            TickFrameComponents();
        }

        private void UpdateLate()
        {
            if (_manager != null && _manager.isActiveAndEnabled) { TickLate(); }
        }

        private static bool InsertAfter(ref PlayerLoopSystem loop, Type anchor, PlayerLoopSystem phase)
        {
            if (loop.subSystemList == null) { return false; }
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == anchor)
                {
                    PlayerLoopSystem[] children = new PlayerLoopSystem[loop.subSystemList.Length + 1];
                    Array.Copy(loop.subSystemList, 0, children, 0, i + 1);
                    children[i + 1] = phase;
                    Array.Copy(loop.subSystemList, i + 1, children, i + 2, loop.subSystemList.Length - i - 1);
                    loop.subSystemList = children;
                    return true;
                }
                PlayerLoopSystem child = loop.subSystemList[i];
                if (InsertAfter(ref child, anchor, phase))
                {
                    loop.subSystemList[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsFramePhase(PlayerLoopSystem loop)
        {
            if (loop.type == typeof(FramePhase)) { return true; }
            if (loop.subSystemList != null)
            {
                for (int i = 0; i < loop.subSystemList.Length; i++)
                {
                    if (ContainsFramePhase(loop.subSystemList[i])) { return true; }
                }
            }
            return false;
        }

        private static void RemovePhases(ref PlayerLoopSystem loop, bool frameOnly = false)
        {
            if (loop.subSystemList == null) { return; }
            List<PlayerLoopSystem> children = new List<PlayerLoopSystem>(loop.subSystemList.Length);
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                PlayerLoopSystem child = loop.subSystemList[i];
                if (child.type == typeof(FramePhase) || (!frameOnly
                    && (child.type == typeof(FixedPhase) || child.type == typeof(LatePhase)))) { continue; }
                RemovePhases(ref child, frameOnly);
                children.Add(child);
            }
            loop.subSystemList = children.ToArray();
        }
    }
}
