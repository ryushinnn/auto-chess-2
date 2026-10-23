namespace AutoChess.Core.Combat {
    public abstract class BattleEvent : GameEvent {
        public int BattleId;
    }

    public sealed class BattleStartedEvent : BattleEvent { }

    public sealed class BattleEndedEvent : BattleEvent {
        public BattleOutcome Outcome;
    }

    public abstract class UnitBattleEvent : BattleEvent {
        public int UnitId;
    }

    public sealed class UnitMoveStartedEvent : UnitBattleEvent {
        public HexCoord Destination;
    }

    public sealed class UnitMoveStoppedEvent : UnitBattleEvent { }

    public sealed class UnitAttackStartedEvent : UnitBattleEvent {
        public int TargetId;
        /// <summary>Animation play rate (>= 1) so the swing fits the attack interval.</summary>
        public float AnimationSpeed;
        public float Duration;
    }

    public sealed class UnitAttackInterruptedEvent : UnitBattleEvent { }

    public sealed class UnitSkillStartedEvent : UnitBattleEvent {
        public int TargetId;
        /// <summary>Hero-specific variant (Yone: 0 = divine sword, 1 = devil sword).</summary>
        public int Variant;
        public float Duration;
    }

    public sealed class UnitSkillInterruptedEvent : UnitBattleEvent { }

    public sealed class UnitDamagedEvent : UnitBattleEvent {
        public int SourceId;
        public float Amount;
        public DamageType Type;
        public bool Crit;
        /// <summary>Position inside a multi-part hit (0, 1...) so the numbers can be stacked.</summary>
        public int StackIndex;
    }

    public sealed class UnitHealedEvent : UnitBattleEvent {
        public float Amount;
    }

    public sealed class UnitEnergyDrainEvent : UnitBattleEvent {
        public float Delay;
        public float Duration;
    }

    public sealed class UnitAirborneEvent : UnitBattleEvent {
        public float Duration;
    }

    public sealed class UnitDiedEvent : UnitBattleEvent {
        public int KillerId;
    }

    public sealed class MarkAddedEvent : UnitBattleEvent {
        public int MarkId;
        public string Key;
        public int Stacks;
        /// <summary>Seconds, or float.PositiveInfinity for a permanent mark.</summary>
        public float Duration;
    }

    public sealed class MarkRemovedEvent : UnitBattleEvent {
        public int MarkId;
    }

    /// <summary>A timed or permanent effect with a visual (e.g. Malphite's stone skin).</summary>
    public sealed class EffectAddedEvent : UnitBattleEvent {
        public int EffectId;
        public string Key;
        public string Visual;
        public int OwnerId;
        public float Duration;
    }

    public sealed class EffectRemovedEvent : UnitBattleEvent {
        public int EffectId;
        public string Visual;
    }

    public sealed class ProjectileLaunchedEvent : BattleEvent {
        public int ProjectileId;
        public int SourceId;
        public int TargetId;
        public string Visual;
        public float Speed;
    }

    public sealed class ProjectileEndedEvent : BattleEvent {
        public int ProjectileId;
        public bool Hit;
    }

    public sealed class AreaCreatedEvent : BattleEvent {
        public int AreaId;
        public int OwnerId;
        public Vec2 Position;
        public float Radius;
        public string Visual;
        public float Duration;
    }

    public sealed class AreaExpiredEvent : BattleEvent {
        public int AreaId;
    }
}
