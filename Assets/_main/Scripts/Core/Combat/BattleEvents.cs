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
        public double AnimationSpeed;
        public double Duration;
    }

    public sealed class UnitAttackInterruptedEvent : UnitBattleEvent { }

    public sealed class UnitSkillStartedEvent : UnitBattleEvent {
        public int TargetId;
        /// <summary>Hero-specific variant (Yone: 0 = divine sword, 1 = devil sword).</summary>
        public int Variant;
        public double Duration;
    }

    public sealed class UnitSkillInterruptedEvent : UnitBattleEvent { }

    public sealed class UnitDamagedEvent : UnitBattleEvent {
        public int SourceId;
        /// <summary>Damage after mitigation (what the number above the unit shows).</summary>
        public double Amount;
        /// <summary>Health actually removed (Amount without the overkill): what damage meters count.</summary>
        public double HpLost;
        public DamageType Type;
        public bool Crit;
        /// <summary>Position inside a multi-part hit (0, 1...) so the numbers can be stacked.</summary>
        public int StackIndex;
    }

    public sealed class UnitHealedEvent : UnitBattleEvent {
        public double Amount;
    }

    public sealed class UnitEnergyDrainEvent : UnitBattleEvent {
        public double Delay;
        public double Duration;
    }

    public sealed class UnitAirborneEvent : UnitBattleEvent {
        public double Duration;
    }

    public sealed class UnitDiedEvent : UnitBattleEvent {
        public int KillerId;
    }

    /// <summary>A hit was avoided (dodge) or ignored (invulnerable).</summary>
    public sealed class UnitDodgedEvent : UnitBattleEvent {
        public int SourceId;
        public bool Invulnerable;
    }

    /// <summary>A unit joined the battle after it was built (a summon).</summary>
    public sealed class UnitSpawnedEvent : UnitBattleEvent { }

    public sealed class MarkAddedEvent : UnitBattleEvent {
        public int MarkId;
        public string Key;
        public int Stacks;
        /// <summary>Seconds, or double.PositiveInfinity for a permanent mark.</summary>
        public double Duration;
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
        public double Duration;
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
        public double Speed;
    }

    public sealed class ProjectileEndedEvent : BattleEvent {
        public int ProjectileId;
        public bool Hit;
    }

    public sealed class AreaCreatedEvent : BattleEvent {
        public int AreaId;
        public int OwnerId;
        public Vec2 Position;
        public double Radius;
        public string Visual;
        public double Duration;
    }

    public sealed class AreaExpiredEvent : BattleEvent {
        public int AreaId;
    }
}
