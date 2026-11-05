namespace Game.Core
{
    /// <summary>死亡時の個体を保持する通知。既に登録解除済みでも別個体へ解決し直さない。</summary>
    public readonly struct ActorDeathEvent
    {
        public ActorRef Dead { get; }
        public ActorRef Killer { get; }
        public ActorDeathEvent(ActorRef dead, ActorRef killer) { Dead = dead; Killer = killer; }
    }
}
