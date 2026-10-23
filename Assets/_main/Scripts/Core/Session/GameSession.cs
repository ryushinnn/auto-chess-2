using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// What the client (views, input, UI) talks to. The views read <see cref="State"/>, react to
    /// <see cref="EventRaised"/> and change the game only through <see cref="Submit"/>. Today the match runs
    /// in-process (<see cref="LocalGameSession"/>) or online (<see cref="LockstepSession"/>: commands go to the
    /// server, the frames it relays drive a local replica), without touching the views.
    /// </summary>
    public interface IGameSession {
        GameDatabase Db { get; }
        MatchState State { get; }
        int LocalPlayerId { get; }
        PlayerState LocalPlayer { get; }
        float PhaseTimeLeft { get; }
        float PhaseDuration { get; }
        /// <summary>0..1 progress inside the current simulation tick (for smooth interpolation).</summary>
        float TickProgress { get; }
        bool BoardLocked { get; }

        /// <summary>Raises the first events (call once, after the views subscribed).</summary>
        void Start();
        /// <summary>Feeds real time (once per frame).</summary>
        void Advance(float deltaTime);
        void Submit(GameCommand command, Action<CommandResult> onResult = null);
        CommandError CanMove(int unitId, UnitLocation target);
        bool CanEquip(int unitId, string itemId);

        event Action<GameEvent> EventRaised;
    }

    /// <summary>Runs the authoritative <see cref="MatchSimulation"/> locally on a fixed-step accumulator.</summary>
    public sealed class LocalGameSession : IGameSession {
        public readonly MatchSimulation Match;
        public readonly MatchRecorder Recorder;
        /// <summary>Speed multiplier of the simulation clock (independent of the engine's time scale).</summary>
        public float Speed = 1;
        /// <summary>Upper bound of ticks simulated per Advance call, so a long frame cannot snowball.</summary>
        public int MaxTicksPerAdvance = 8;

        readonly List<GameEvent> dispatch = new();
        float accumulator;
        bool flushing;

        public event Action<GameEvent> EventRaised;

        public LocalGameSession(GameDatabase db, MatchConfig config, int localPlayerId = 0, bool record = true) {
            Match = new MatchSimulation(db, config);
            LocalPlayerId = localPlayerId;
            if (record) Recorder = new MatchRecorder(Match);
        }

        public GameDatabase Db => Match.Db;
        public MatchState State => Match.State;
        public int LocalPlayerId { get; }
        public PlayerState LocalPlayer => Match.Player(LocalPlayerId);
        public float PhaseTimeLeft => (float)Match.PhaseTimeLeft;
        public float PhaseDuration => (float)Match.PhaseDuration;
        public float TickProgress => (float)Math.Clamp(accumulator / Match.Rules.TickDelta, 0, 1);
        public bool BoardLocked => Match.BoardLocked;

        public void Start() {
            Match.Start();
            Flush();
        }

        /// <summary>Feeds real time; runs as many whole ticks as it covers, then dispatches the events.</summary>
        public void Advance(float deltaTime) {
            var dt = (float)Match.Rules.TickDelta;
            accumulator += Math.Max(0, deltaTime) * Speed;
            var ticks = 0;
            while (accumulator >= dt && ticks < MaxTicksPerAdvance) {
                if (Recorder != null) Recorder.Tick();
                else Match.Tick();
                accumulator -= dt;
                ticks++;
            }
            if (ticks == MaxTicksPerAdvance && accumulator >= dt) accumulator = dt * 0.999f;
            Flush();
        }

        /// <summary>Runs <paramref name="ticks"/> ticks immediately (tests, fast-forward).</summary>
        public void RunTicks(int ticks) {
            for (var i = 0; i < ticks; i++) {
                if (Recorder != null) Recorder.Tick();
                else Match.Tick();
            }
            Flush();
        }

        public void Submit(GameCommand command, Action<CommandResult> onResult = null) {
            command.PlayerId = command.PlayerId < 0 ? LocalPlayerId : command.PlayerId;
            var result = Recorder != null ? Recorder.Submit(command) : Match.Submit(command);
            onResult?.Invoke(result);
            Flush();
        }

        /// <summary>Synchronous variant for local tools and tests.</summary>
        public CommandResult Execute(GameCommand command) {
            CommandResult result = default;
            Submit(command, r => result = r);
            return result;
        }

        public CommandError CanMove(int unitId, UnitLocation target) => Match.CanMove(LocalPlayerId, unitId, target);
        public bool CanEquip(int unitId, string itemId) => Match.CanEquip(LocalPlayerId, unitId, itemId);

        void Flush() {
            if (flushing) return; // a handler submitted a command: its events go out in this same loop
            flushing = true;
            try {
                while (Match.Events.Count > 0) {
                    dispatch.Clear();
                    Match.Events.DrainTo(dispatch);
                    foreach (var e in dispatch) EventRaised?.Invoke(e);
                }
            }
            finally {
                dispatch.Clear();
                flushing = false;
            }
        }
    }
}
