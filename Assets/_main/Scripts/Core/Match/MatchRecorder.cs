using System;
using System.Collections.Generic;
using AutoChess.Core.Net;

namespace AutoChess.Core {
    /// <summary>
    /// Command log of a match: seed + every accepted command with the tick it was applied at. Enough to
    /// replay the match bit-for-bit (bug reports, server audits, regression tests).
    /// </summary>
    public sealed class MatchRecording {
        public ulong Seed;
        public int PlayerCount;
        public bool AllowCheats;
        public bool SkipStarterPack;
        public double? SetupSeconds;
        public double? PreparationSeconds;
        public readonly List<(int tick, byte[] command)> Commands = new();
        public int EndTick;
    }

    public sealed class MatchRecorder {
        public readonly MatchSimulation Match;
        public readonly MatchRecording Recording;

        public MatchRecorder(MatchSimulation match) {
            Match = match;
            Recording = new MatchRecording {
                Seed = match.Config.Seed, PlayerCount = match.State.Players.Count,
                AllowCheats = match.Config.AllowCheats, SkipStarterPack = match.Config.SkipStarterPack,
                SetupSeconds = match.Config.SetupSeconds, PreparationSeconds = match.Config.PreparationSeconds
            };
        }

        public CommandResult Submit(GameCommand command) {
            var tick = Match.State.Tick;
            var result = Match.Submit(command);
            if (result.Ok) Recording.Commands.Add((tick, CommandCodec.Encode(command)));
            return result;
        }

        public void Tick() {
            Match.Tick();
            Recording.EndTick = Match.State.Tick;
        }

        /// <summary>Re-runs a recording from scratch and returns the resulting match.</summary>
        public static MatchSimulation Replay(GameDatabase db, MatchRecording recording, Combat.AbilityRegistry abilities = null) {
            var match = new MatchSimulation(db, new MatchConfig {
                Seed = recording.Seed, PlayerCount = recording.PlayerCount,
                AllowCheats = recording.AllowCheats, SkipStarterPack = recording.SkipStarterPack, Abilities = abilities,
                SetupSeconds = recording.SetupSeconds, PreparationSeconds = recording.PreparationSeconds
            });
            match.Start();
            var next = 0;
            var commands = recording.Commands;
            while (true) {
                while (next < commands.Count && commands[next].tick == match.State.Tick) {
                    var result = match.Submit(CommandCodec.Decode(commands[next].command));
                    if (!result.Ok) throw new InvalidOperationException($"replay diverged at tick {match.State.Tick}: {result}");
                    next++;
                }
                if (match.State.Tick >= recording.EndTick || match.Phase == MatchPhase.Finished) break;
                match.Tick();
            }
            if (next < commands.Count) throw new InvalidOperationException($"replay ended with {commands.Count - next} commands left");
            return match;
        }
    }
}
