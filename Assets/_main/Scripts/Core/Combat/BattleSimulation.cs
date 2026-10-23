using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>
    /// One battle, simulated in fixed ticks (was BattleField + the per-hero behaviour trees and abilities).
    /// Deterministic for a given <see cref="BattleSetup"/> (seed included), engine-free, and independent of
    /// the players' boards, so a server can run many of them side by side or replay one from its setup.
    /// </summary>
    public sealed class BattleSimulation {
        enum CellState : byte {
            Empty,
            Reserved,
            Occupied,
        }

        public readonly int Id;
        public readonly GameDatabase Db;
        public readonly GameRules Rules;
        public readonly HexGrid Grid;
        public readonly DeterministicRandom Rng;
        public readonly BattleSetup Setup;
        public readonly double TickDelta;

        readonly EventBuffer events;
        readonly List<CombatUnit> units = new();
        readonly List<CombatUnit>[] alive = { new(), new() };
        readonly CellState[] cellState;
        readonly CombatUnit[] cellOwner;
        readonly List<Projectile> projectiles = new();
        readonly List<AreaEffect> areas = new();
        readonly PathFinder pathFinder;
        readonly List<int> pathBuffer = new();
        readonly Func<int, bool> isOccupied;
        internal readonly List<StatModifier> ModifierScratch = new();

        int nextEffectId;
        int nextProjectileId;
        int nextAreaId;

        public int Tick { get; private set; }
        public double Time => Tick * TickDelta;
        public bool Started { get; private set; }
        public bool Finished { get; private set; }
        public BattleOutcome Outcome { get; private set; }

        public IReadOnlyList<CombatUnit> Units => units;
        public IReadOnlyList<Projectile> Projectiles => projectiles;
        public IReadOnlyList<AreaEffect> Areas => areas;
        public IReadOnlyList<CombatUnit> AliveUnits(TeamSide side) => alive[(int)side];

        /// <summary>Raised inside the tick when a unit dies (dead unit, killer or null). For synergies.</summary>
        public event Action<CombatUnit, CombatUnit> UnitDied;
        /// <summary>A unit finished casting its skill (not interrupted). For synergies.</summary>
        public event Action<CombatUnit> SkillCompleted;

        readonly List<IHitHook> hitHooks = new();
        readonly List<Action<double>> tickers = new();
        readonly AbilityRegistry abilityRegistry;
        int lastUnitId;

        /// <summary>What the damage being dealt right now comes from (auto-attack, ability, effect).</summary>
        public HitKind CurrentHit { get; private set; } = HitKind.Ability;

        /// <summary>Marks the damage dealt inside the using block as <paramref name="kind"/>.</summary>
        internal HitScope Hit(HitKind kind) {
            var previous = CurrentHit;
            CurrentHit = kind;
            return new HitScope(this, previous);
        }

        internal readonly struct HitScope : IDisposable {
            readonly BattleSimulation battle;
            readonly HitKind previous;

            public HitScope(BattleSimulation battle, HitKind previous) {
                this.battle = battle;
                this.previous = previous;
            }

            public void Dispose() => battle.CurrentHit = previous;
        }

        /// <summary>Synergies that react to every hit (dodge, conversions, procs...), in registration order.</summary>
        public void AddHitHook(IHitHook hook) => hitHooks.Add(hook);

        /// <summary>Synergies that run once per tick, after the units acted.</summary>
        public void AddTicker(Action<double> ticker) => tickers.Add(ticker);

        internal bool BeforeHit(CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) {
            foreach (var hook in hitHooks) {
                if (!hook.BeforeHit(target, source, ref damage, kind)) return false;
            }
            return true;
        }

        internal void AfterHit(CombatUnit target, CombatUnit source, Damage damage, double dealt, HitKind kind) {
            foreach (var hook in hitHooks) hook.AfterHit(target, source, damage, dealt, kind);
        }

        internal void OnSkillCompleted(CombatUnit unit) => SkillCompleted?.Invoke(unit);

        public BattleSimulation(int id, GameDatabase db, BattleSetup setup, EventBuffer events, AbilityRegistry abilities = null) {
            Id = id;
            Db = db;
            Rules = db.Rules;
            Grid = db.Grid;
            Setup = setup;
            TickDelta = Rules.TickDelta;
            Rng = new DeterministicRandom(setup.Seed);
            this.events = events ?? new EventBuffer();
            cellState = new CellState[Grid.CellCount];
            cellOwner = new CombatUnit[Grid.CellCount];
            pathFinder = new PathFinder(Grid);
            isOccupied = cell => cellState[cell] == CellState.Occupied;
            abilities ??= AbilityRegistry.Default;
            abilityRegistry = abilities;

            var nextUnitId = 0;
            foreach (var side in new[] { TeamSide.Home, TeamSide.Away }) {
                foreach (var c in setup.Team(side).Units) {
                    if (!Grid.Contains(c.Cell)) throw new ArgumentException($"battle {id}: {c.UnitId} outside the board at {c.Cell}");
                    var cell = Grid.IndexOf(c.Cell);
                    if (cellState[cell] != CellState.Empty) throw new ArgumentException($"battle {id}: two units on {c.Cell}");
                    var def = db.Unit(c.UnitId);
                    var attack = abilities.CreateAttack(def);
                    var skill = abilities.CreateSkill(def, attack);
                    var unit = new CombatUnit(this, ++nextUnitId, side, def, c, attack, skill);
                    unit.Position = unit.PreviousPosition = Grid.Center(cell);
                    units.Add(unit);
                    alive[(int)side].Add(unit);
                    Occupy(unit, cell);
                }
            }
            foreach (var u in units) u.ApplyLoadout();
            lastUnitId = nextUnitId;
        }

        /// <summary>
        /// Adds a unit mid-battle (a synergy's summon) on the free cell nearest to <paramref name="near"/>.
        /// Returns null when the board is full.
        /// </summary>
        public CombatUnit Summon(string unitId, TeamSide side, Vec2 near, HeroRank rank = HeroRank.B) {
            var cell = Grid.NearestIndex(near, IsCellEmpty);
            if (cell < 0) return null;
            var def = Db.Unit(unitId);
            var attack = abilityRegistry.CreateAttack(def);
            var setup = new CombatantSetup { UnitId = unitId, Rank = rank, Cell = Grid.CoordOf(cell) };
            var unit = new CombatUnit(this, ++lastUnitId, side, def, setup, attack, abilityRegistry.CreateSkill(def, attack));
            unit.Position = unit.PreviousPosition = Grid.Center(cell);
            units.Add(unit);
            alive[(int)side].Add(unit);
            Occupy(unit, cell);
            unit.ApplyLoadout();
            Emit(new UnitSpawnedEvent { UnitId = unit.Id });
            return unit;
        }

        public CombatUnit FindUnit(int unitId) {
            foreach (var u in units) {
                if (u.Id == unitId) return u;
            }
            return null;
        }

        internal void Emit(BattleEvent e) {
            e.BattleId = Id;
            events.Add(e);
        }

        internal int NextEffectId() => ++nextEffectId;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Activates synergies; the first <see cref="Step"/> calls it when needed.</summary>
        public void Start() {
            if (Started) return;
            Started = true;
            foreach (var side in new[] { TeamSide.Home, TeamSide.Away }) {
                if (Setup.Team(side).ApplyTraits) TraitEffects.Apply(this, side);
            }
            Emit(new BattleStartedEvent());
            CheckEnd();
        }

        public void Step() {
            if (!Started) Start();
            if (Finished) return;

            Tick++;
            var dt = TickDelta;
            foreach (var u in units) u.PreviousPosition = u.Position;

            for (var i = 0; i < units.Count && !Finished; i++) {
                var u = units[i];
                if (!u.Alive) continue;
                u.TickEffects(dt);
                if (u.Alive) u.Attack.Tick(u, dt);
                if (u.Alive) u.Skill.Tick(u, dt);
                if (u.Alive) TickMovement(u, dt);
                if (u.Alive && !Finished) Think(u);
            }

            if (!Finished) TickProjectiles(dt);
            if (!Finished) TickAreas(dt);
            for (var i = 0; i < tickers.Count && !Finished; i++) tickers[i](dt);
        }

        /// <summary>Ends the battle from outside (time out).</summary>
        public void ForceEnd(BattleOutcome outcome) {
            if (!Finished) Finish(outcome);
        }

        internal void OnUnitDied(CombatUnit unit, CombatUnit killer) {
            ReleaseCell(unit);
            alive[(int)unit.Side].Remove(unit);
            Emit(new UnitDiedEvent { UnitId = unit.Id, KillerId = killer?.Id ?? -1 });
            CheckEnd();
            UnitDied?.Invoke(unit, killer);
        }

        void CheckEnd() {
            if (!Started || Finished) return;
            if (alive[(int)TeamSide.Home].Count == 0) Finish(BattleOutcome.AwayWin);
            else if (alive[(int)TeamSide.Away].Count == 0) Finish(BattleOutcome.HomeWin);
        }

        void Finish(BattleOutcome outcome) {
            Finished = true;
            Outcome = outcome;
            foreach (var u in units) {
                if (u.Alive) StopMove(u, false);
            }
            Emit(new BattleEndedEvent { Outcome = outcome });
        }

        // ------------------------------------------------------------------ decisions (was HeroBT)

        void Think(CombatUnit u) {
            u.Target = NearestOpponent(u);
            if (!u.Def.Moveable) return;
            if (u.Target == null) {
                StopMove(u, false);
                return;
            }
            PlanMove(u);
            if (!u.Movement.Reached) return;
            if (u.Skill.TryStart(u)) return;
            u.Attack.TryStart(u);
        }

        public CombatUnit NearestOpponent(CombatUnit u) {
            CombatUnit best = null;
            var bestSqr = double.MaxValue;
            foreach (var o in alive[(int)u.Side.Opponent()]) {
                var d = Vec2.SqrDistance(u.Position, o.Position);
                if (d < bestSqr) {
                    bestSqr = d;
                    best = o;
                }
            }
            return best;
        }

        public CombatUnit FurthestOpponent(CombatUnit u) {
            CombatUnit best = null;
            var bestSqr = double.MinValue;
            foreach (var o in alive[(int)u.Side.Opponent()]) {
                var d = Vec2.SqrDistance(u.Position, o.Position);
                if (d > bestSqr) {
                    bestSqr = d;
                    best = o;
                }
            }
            return best;
        }

        public CombatUnit RandomOpponent(CombatUnit u) {
            var list = alive[(int)u.Side.Opponent()];
            return list.Count == 0 ? null : list[Rng.Range(0, list.Count)];
        }

        // ------------------------------------------------------------------ board occupancy & movement (was HeroMovement)

        bool InRange(int a, int b, int range) => Grid.Distance(a, b) <= range;

        void Occupy(CombatUnit u, int cell) {
            ReleaseCell(u);
            cellState[cell] = CellState.Occupied;
            cellOwner[cell] = u;
            u.OccupiedCell = cell;
        }

        void ReleaseCell(CombatUnit u) {
            if (u.OccupiedCell >= 0 && cellOwner[u.OccupiedCell] == u) {
                cellState[u.OccupiedCell] = CellState.Empty;
                cellOwner[u.OccupiedCell] = null;
            }
            u.OccupiedCell = -1;
        }

        void Reserve(CombatUnit u, int cell) {
            cellState[cell] = CellState.Reserved;
            cellOwner[cell] = u;
        }

        void ClearReservation(CombatUnit u, int cell) {
            if (cell >= 0 && cellOwner[cell] == u && cellState[cell] == CellState.Reserved) {
                cellState[cell] = CellState.Empty;
                cellOwner[cell] = null;
            }
        }

        public bool IsCellEmpty(int cell) => cellState[cell] == CellState.Empty;

        /// <summary>A unit stands on the cell (reservations of walking units excluded).</summary>
        public bool IsCellOccupied(int cell) => cellState[cell] == CellState.Occupied;

        /// <summary>Unit holding or walking to a cell, or null.</summary>
        public CombatUnit CellOwner(int cell) => cellOwner[cell];

        void PlanMove(CombatUnit u) {
            if (u.Status.IsStunned || u.Status.IsAirborne) return;
            var mv = u.Movement;
            var targetCell = Grid.NearestIndex(u.Target.Position);
            if (targetCell == mv.TargetCell) return;
            mv.TargetCell = targetCell;

            var range = u.AttackRange;
            if (u.OccupiedCell >= 0 && InRange(u.OccupiedCell, targetCell, range)) {
                mv.Reached = true;
                return;
            }
            if (mv.NextCell >= 0 && InRange(mv.NextCell, targetCell, range)) return;

            var myCell = Grid.NearestIndex(u.Position);
            var destination = NearestFreeCellInRange(targetCell, range, myCell);
            if (destination < 0) destination = Grid.NearestIndex(Grid.Center(targetCell), IsCellEmpty, targetCell);
            if (destination < 0 || destination == mv.Destination) return;

            ReleaseCell(u);
            ClearReservation(u, mv.Destination);
            mv.Destination = destination;
            Reserve(u, destination);
            mv.Reached = false;

            if (!pathFinder.FindPath(myCell, destination, pathBuffer, isOccupied)) {
                pathFinder.FindPath(myCell, destination, pathBuffer);
            }
            mv.Waypoints.Clear();
            if (pathBuffer.Count > 1) {
                for (var i = 1; i < pathBuffer.Count; i++) mv.Waypoints.Add(pathBuffer[i]);
            }
            else if (pathBuffer.Count == 1 && Vec2.Distance(Grid.Center(pathBuffer[0]), u.Position) > 0.01) {
                mv.Waypoints.Add(pathBuffer[0]);
            }

            if (mv.Waypoints.Count == 0) {
                FinishMove(u);
                return;
            }
            mv.WaypointIndex = 0;
            mv.NextCell = mv.Waypoints[0];
            if (!mv.Moving) {
                mv.Moving = true;
                Emit(new UnitMoveStartedEvent { UnitId = u.Id, Destination = Grid.CoordOf(destination) });
            }
        }

        int NearestFreeCellInRange(int targetCell, int range, int fromCell) {
            var from = Grid.Center(fromCell);
            var best = -1;
            var bestSqr = double.MaxValue;
            for (var c = 0; c < Grid.CellCount; c++) {
                if (c == targetCell || cellState[c] != CellState.Empty || !InRange(c, targetCell, range)) continue;
                var d = Vec2.SqrDistance(Grid.Center(c), from);
                if (d < bestSqr) {
                    bestSqr = d;
                    best = c;
                }
            }
            return best;
        }

        void TickMovement(CombatUnit u, double dt) {
            var mv = u.Movement;
            if (!mv.Moving) return;
            var budget = u.MovementSpeed * Grid.AverageCellDistance * dt;
            while (mv.Moving && budget > 1e-6d) {
                var before = u.Position;
                u.Position = Vec2.MoveTowards(u.Position, Grid.Center(mv.NextCell), budget, out var arrived);
                budget -= Vec2.Distance(before, u.Position);
                if (!arrived) break;
                ArriveAt(u, mv.NextCell);
            }
        }

        void ArriveAt(CombatUnit u, int cell) {
            var mv = u.Movement;
            if (mv.WaypointIndex < mv.Waypoints.Count - 1) {
                // Stop early on a free cell when the target already is in range from here.
                var target = u.Target;
                if (target != null && target.Alive && cell != mv.Destination && IsCellEmpty(cell)
                    && InRange(Grid.NearestIndex(target.Position), cell, u.AttackRange)) {
                    ClearReservation(u, mv.Destination);
                    mv.Destination = -1;
                    Occupy(u, cell);
                    mv.NextCell = -1;
                    StopMove(u, false);
                    mv.Reached = true;
                    return;
                }
                mv.WaypointIndex++;
                mv.NextCell = mv.Waypoints[mv.WaypointIndex];
                return;
            }
            FinishMove(u);
        }

        void FinishMove(CombatUnit u) {
            var mv = u.Movement;
            if (mv.Destination >= 0) {
                var destination = mv.Destination;
                ClearReservation(u, destination);
                Occupy(u, destination);
            }
            mv.Destination = -1;
            mv.NextCell = -1;
            StopMove(u, false);
            mv.Reached = true;
        }

        internal void StopMove(CombatUnit u, bool reset) {
            var mv = u.Movement;
            if (mv.Moving) {
                mv.Moving = false;
                Emit(new UnitMoveStoppedEvent { UnitId = u.Id });
            }
            if (!reset) return;
            mv.TargetCell = -1;
            ClearReservation(u, mv.Destination);
            mv.Destination = -1;
            mv.NextCell = -1;
        }

        // ------------------------------------------------------------------ projectiles & areas

        public Projectile LaunchProjectile(CombatUnit source, CombatUnit target, string visual, Action<Projectile> onArrive,
            double speed = 0) {
            var p = new Projectile {
                Id = ++nextProjectileId, Source = source, Target = target, Position = source.Position,
                PreviousPosition = source.Position,
                Speed = speed > 0 ? speed : Rules.ProjectileSpeed, OnArrive = onArrive, Visual = visual, Kind = CurrentHit
            };
            projectiles.Add(p);
            Emit(new ProjectileLaunchedEvent {
                ProjectileId = p.Id, SourceId = source.Id, TargetId = target.Id, Visual = visual, Speed = p.Speed
            });
            return p;
        }

        void TickProjectiles(double dt) {
            for (var i = 0; i < projectiles.Count; i++) {
                var p = projectiles[i];
                p.PreviousPosition = p.Position;
                var step = p.Speed * dt;
                p.Position = Vec2.MoveTowards(p.Position, p.Target.Position, step, out var arrived);
                if (!arrived) continue;
                var hit = p.Source.Alive && p.Target.Alive;
                if (hit) {
                    using (Hit(p.Kind)) p.OnArrive?.Invoke(p);
                }
                projectiles.RemoveAt(i--);
                Emit(new ProjectileEndedEvent { ProjectileId = p.Id, Hit = hit });
                if (Finished) return;
            }
        }

        public AreaEffect CreateArea(CombatUnit dealer, Vec2 center, double radius, Damage damage, int strikes,
            double interval, bool canCrit, string visual) {
            var area = new AreaEffect {
                Id = ++nextAreaId, Dealer = dealer, Center = center, Radius = radius, Damage = damage,
                StrikesLeft = strikes, Interval = interval, Timer = interval, CanCrit = canCrit,
                CritChance = dealer.CriticalChance, CritDamage = dealer.CriticalDamage, Visual = visual
            };
            areas.Add(area);
            Emit(new AreaCreatedEvent {
                AreaId = area.Id, OwnerId = dealer.Id, Position = center, Radius = radius, Visual = visual,
                Duration = interval * strikes
            });
            return area;
        }

        void TickAreas(double dt) {
            for (var i = 0; i < areas.Count; i++) {
                var a = areas[i];
                if (a.Timer > 0) {
                    a.Timer -= dt;
                    continue;
                }
                Strike(a);
                if (Finished) return;
                if (--a.StrikesLeft <= 0) {
                    areas.RemoveAt(i--);
                    Emit(new AreaExpiredEvent { AreaId = a.Id });
                }
                else {
                    a.Timer = a.Interval;
                }
            }
        }

        void Strike(AreaEffect a) {
            var reach = a.Radius + Rules.UnitRadius;
            var targets = alive[(int)a.Dealer.Side.Opponent()];
            for (var i = 0; i < targets.Count; i++) {
                var t = targets[i];
                if (Vec2.SqrDistance(t.Position, a.Center) > reach * reach) continue;
                var isNew = a.HitUnits.Add(t.Id);
                var d = a.Damage;
                if (a.CanCrit && Rng.NextFloat() < a.CritChance) {
                    d.Value *= a.CritDamage;
                    d.Crit = true;
                }
                var before = targets.Count;
                using (Hit(HitKind.Effect)) t.TakeDamage(d, a.Dealer, isNew);
                if (targets.Count < before) i--; // t died and left the list
                if (Finished) return;
            }
        }
    }

    public sealed class Projectile {
        public int Id;
        public CombatUnit Source;
        public CombatUnit Target;
        public Vec2 Position;
        /// <summary>Position at the start of the current tick (for view interpolation).</summary>
        public Vec2 PreviousPosition;
        public double Speed;
        public string Visual;
        /// <summary>Kind of the hit that launched it (an auto-attack's arrow is still an attack).</summary>
        public HitKind Kind;
        internal Action<Projectile> OnArrive;
    }

    /// <summary>What a hit comes from: synergies like dodge or on-attack procs only react to auto-attacks.</summary>
    public enum HitKind {
        Attack,
        /// <summary>A skill, or anything not marked otherwise.</summary>
        Ability,
        /// <summary>Damage over time, areas, synergy procs.</summary>
        Effect,
    }

    /// <summary>A synergy reacting to hits (registered with <see cref="BattleSimulation.AddHitHook"/>).</summary>
    public interface IHitHook {
        /// <summary>Before mitigation; may change the damage. Return false to cancel the hit (dodge).</summary>
        bool BeforeHit(CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind);

        /// <summary>After the damage was applied (the target may have died).</summary>
        void AfterHit(CombatUnit target, CombatUnit source, Damage damage, double dealt, HitKind kind);
    }

    /// <summary>Repeated area damage (Ashe's arrow rain; was DamageOverTimeArea).</summary>
    public sealed class AreaEffect {
        public int Id;
        public CombatUnit Dealer;
        public Vec2 Center;
        public double Radius;
        public Damage Damage;
        public int StrikesLeft;
        public double Interval;
        public double Timer;
        public bool CanCrit;
        public double CritChance;
        public double CritDamage;
        public string Visual;
        public readonly HashSet<int> HitUnits = new();
    }
}
