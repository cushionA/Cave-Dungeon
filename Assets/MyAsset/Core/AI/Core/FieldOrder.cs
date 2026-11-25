using System;
using UnityEngine;

namespace Game.Core
{
    [Flags]
    public enum FieldOrderIssuer : byte
    {
        None = 0,
        Player = 1,
        EnemyCommander = 2,
        CompanionAI = 4,
    }

    public readonly struct FieldOrder
    {
        public BroadcastAction Action { get; }
        public FieldOrderIssuer Issuer { get; }
        public ActorRef Source { get; }
        public ActorRef Target { get; }
        public Vector2? Position { get; }

        public FieldOrder(BroadcastAction action, FieldOrderIssuer issuer, ActorRef source,
            ActorRef target = default, Vector2? position = null)
        {
            Action = action;
            Issuer = issuer;
            Source = source;
            Target = target;
            Position = position;
        }
    }

    public static class FieldOrderCatalog
    {
        public static FieldOrderIssuer AllowedIssuers(BroadcastAction action)
        {
            const FieldOrderIssuer ai = FieldOrderIssuer.EnemyCommander | FieldOrderIssuer.CompanionAI;
            switch (action)
            {
                case BroadcastAction.DesignateTarget:
                case BroadcastAction.Rally:
                case BroadcastAction.FocusFire:
                case BroadcastAction.DefendPoint:
                case BroadcastAction.ReleaseOrder:
                    return FieldOrderIssuer.Player | ai;
                case BroadcastAction.Scatter:
                case BroadcastAction.Disengage:
                case BroadcastAction.ModeSync:
                    return ai;
                case BroadcastAction.Taunt:
                    return FieldOrderIssuer.EnemyCommander;
                default:
                    return FieldOrderIssuer.None;
            }
        }

        public static bool Allows(BroadcastAction action, FieldOrderIssuer issuer) =>
            (issuer == FieldOrderIssuer.Player || issuer == FieldOrderIssuer.EnemyCommander || issuer == FieldOrderIssuer.CompanionAI)
            && (AllowedIssuers(action) & issuer) != 0;

        public static bool RequiresTarget(BroadcastAction action) =>
            action == BroadcastAction.DesignateTarget || action == BroadcastAction.FocusFire;

        public static bool RequiresPosition(BroadcastAction action) => action == BroadcastAction.DefendPoint;
    }
}
