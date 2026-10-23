using System;
using System.IO;
using System.Linq;
using System.Text;
using AutoChess.Core.Net;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    /// <summary>
    /// Cross-runtime determinism probe: plays a fixed match on the real game data and writes a per-tick
    /// fingerprint (state hash + every battle unit's raw number bits) to Temp/determinism_&lt;runtime&gt;.txt.
    /// Run it in Unity (Mono) and with Tools/core_tests (CoreCLR), then diff the two files: the first
    /// differing line shows where the runtimes disagree. Online play needs them identical.
    /// </summary>
    public class DeterminismProbeTests {
        static string Runtime => Type.GetType("Mono.Runtime") != null ? "mono" : "coreclr";

        [Test]
        public void WriteFingerprint() {
            var path = TestData.RealDataPath();
            if (path == null) Assert.Ignore("GameData/game_data.json missing");
            var db = GameDataJson.Load(File.ReadAllText(path));
            var s = new LocalGameSession(db, new MatchConfig { Seed = 12345, PlayerCount = 2, AllowCheats = true, SkipStarterPack = true });
            s.Start();
            var units = db.Units.Where(u => !u.Summoned).Select(u => u.Id).ToArray();
            for (var p = 0; p < 2; p++) {
                for (var i = 0; i < 6; i++) s.Execute(new CheatUnitCommand { PlayerId = p, UnitId = units[(i * 3 + p * 5) % units.Length] });
                s.Execute(new CheatGoldCommand { PlayerId = p, Amount = 50 });
                s.Execute(new ReadyCommand { PlayerId = p });
            }

            var sb = new StringBuilder();
            var limit = 3 * 120 * db.Rules.TicksPerSecond;
            for (var t = 0; t < limit && s.State.Phase != MatchPhase.Finished && s.State.RoundIndex + s.State.StageIndex * 10 < 2; t++) {
                s.RunTicks(1);
                sb.Append(s.State.Tick).Append(' ').Append(s.State.Phase).Append(' ').Append(StateHasher.Hash(s.State).ToString("x16"));
                foreach (var battle in s.State.Battles.Values) {
                    foreach (var u in battle.Units) {
                        sb.Append(" | ").Append(u.Id).Append(':')
                            .Append(BitConverter.DoubleToInt64Bits(u.Hp).ToString("x16")).Append(',')
                            .Append(BitConverter.DoubleToInt64Bits(u.Energy).ToString("x16")).Append(',')
                            .Append(BitConverter.DoubleToInt64Bits(u.Position.X).ToString("x16")).Append(',')
                            .Append(BitConverter.DoubleToInt64Bits(u.Position.Y).ToString("x16"));
                    }
                }
                sb.Append('\n');
            }
            var dir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? ".", "Temp");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"determinism_{Runtime}.txt"), sb.ToString());
            Assert.Greater(sb.Length, 0);
        }
    }
}
