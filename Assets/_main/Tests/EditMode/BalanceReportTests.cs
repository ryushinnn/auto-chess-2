using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutoChess.Core.Combat;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    /// <summary>
    /// Balance simulator (opt-in: set AUTOCHESS_BALANCE=1; AUTOCHESS_GAME_DATA picks a candidate data file).
    /// Plays thousands of battles on the real game data and writes Temp/balance_report.md:
    /// unit stats, 1v1 duels (win rates by hero and by tier, 2-star vs 1-star of the next tier), role metrics
    /// against a training dummy (burst vs sustained damage, casts), team fights by player level (duration,
    /// time-outs) and the value of each synergy (same team with and without its traits).
    /// Run: py Tools/balance/balance.py report  (or set the variables and run core_tests --filter BalanceReport).
    /// </summary>
    public class BalanceReportTests {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double MaxSeconds = 60;

        GameDatabase db;
        List<UnitDefinition> heroes;
        readonly StringBuilder md = new();
        /// <summary>Machine-readable results (Temp/balance_report.json) for Tools/balance/balance.py.</summary>
        readonly Dictionary<string, object> json = new();

        [Test]
        public void WriteBalanceReport() {
            if (Environment.GetEnvironmentVariable("AUTOCHESS_BALANCE") != "1") Assert.Ignore("opt-in: AUTOCHESS_BALANCE=1");
            var path = TestData.RealDataPath();
            if (path == null) Assert.Ignore("no game data");
            db = GameDataJson.Load(File.ReadAllText(path));
            heroes = db.Units.Where(u => !u.Summoned).OrderBy(u => u.Reputation).ThenBy(u => u.Id).ToList();

            md.AppendLine($"# Balance report ({Path.GetFileName(path)})").AppendLine();
            // AUTOCHESS_BALANCE_SECTIONS (comma list: units, duels, profile, fights, traits, items) narrows the run.
            var only = (Environment.GetEnvironmentVariable("AUTOCHESS_BALANCE_SECTIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            bool Run(string section) => only.Length == 0 || only.Contains(section);
            if (Run("units")) UnitTable();
            if (Run("duels")) Duels();
            if (Run("profile")) DummyMetrics();
            if (Run("fights")) TeamFights();
            if (Run("traits")) TraitValue();
            if (Run("items")) ItemValue(Path.GetDirectoryName(Path.GetDirectoryName(path)));

            var dir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? ".", "Temp");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "balance_report.md"), md.ToString());
            File.WriteAllText(Path.Combine(dir, "balance_report.json"), MiniJson.Serialize(json));
        }

        // ------------------------------------------------------------------ helpers

        static string F(double v, int digits = 0) => v.ToString("F" + digits, Inv);

        static string Roles(UnitDefinition u) {
            var buffer = new Role[6];
            var n = u.Roles.GetRoles(buffer);
            return string.Join("+", buffer.Take(n));
        }

        sealed class Fight {
            public BattleSimulation Battle;
            public double Seconds;
            public readonly Dictionary<int, double> DamageBy = new();
            public readonly Dictionary<int, double> EarlyDamageBy = new();
            public readonly Dictionary<int, int> CastsBy = new();
            public int Dodges, Summons;
        }

        Fight Run(BattleSetup setup, double earlySeconds = 5) {
            var events = new EventBuffer();
            var battle = new BattleSimulation(1, db, setup, events);
            var fight = new Fight { Battle = battle };
            var buffer = new List<GameEvent>();
            var limit = (int)(MaxSeconds * db.Rules.TicksPerSecond);
            for (var t = 0; t < limit && !battle.Finished; t++) {
                battle.Step();
                buffer.Clear();
                events.DrainTo(buffer);
                foreach (var e in buffer) {
                    switch (e) {
                        case UnitDamagedEvent d when d.SourceId >= 0:
                            fight.DamageBy.TryGetValue(d.SourceId, out var total);
                            fight.DamageBy[d.SourceId] = total + d.HpLost;
                            if (battle.Time <= earlySeconds) {
                                fight.EarlyDamageBy.TryGetValue(d.SourceId, out var early);
                                fight.EarlyDamageBy[d.SourceId] = early + d.HpLost;
                            }
                            break;
                        case UnitSkillStartedEvent s:
                            fight.CastsBy.TryGetValue(s.UnitId, out var casts);
                            fight.CastsBy[s.UnitId] = casts + 1;
                            break;
                        case UnitDodgedEvent _:
                            fight.Dodges++;
                            break;
                        case UnitSpawnedEvent _:
                            fight.Summons++;
                            break;
                    }
                }
            }
            if (!battle.Finished) battle.ForceEnd(BattleOutcome.Draw);
            fight.Seconds = battle.Time;
            return fight;
        }

        /// <summary>
        /// Signed result for the home side: + the share of health its survivors keep when it wins, - the
        /// enemy's share when it loses, 0 on a draw. Smoother than win / loss for calibration.
        /// </summary>
        static double Margin(BattleSimulation b) {
            if (b.Outcome != BattleOutcome.HomeWin && b.Outcome != BattleOutcome.AwayWin) return 0;
            var side = b.Outcome == BattleOutcome.HomeWin ? TeamSide.Home : TeamSide.Away;
            double hp = 0, max = 0;
            foreach (var u in b.Units) {
                if (u.Side != side || u.Def.Summoned) continue;
                max += u.MaxHp;
                if (u.Alive) hp += u.Hp;
            }
            var share = max > 0 ? hp / max : 0;
            return side == TeamSide.Home ? share : -share;
        }

        static CombatantSetup Unit(string id, HeroRank rank, int row, int col, double hpScale = 1) =>
            new() { UnitId = id, Rank = rank, Cell = new HexCoord(row, col), HpScale = hpScale };

        // ------------------------------------------------------------------ sections

        void UnitTable() {
            md.AppendLine("## Units (1-star, no items)").AppendLine();
            md.AppendLine("| unit | tier | realm | roles | hp | ad | ap | arm | res | as | range | e/atk | e/hit | dps* | ehp* |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            var stats = new StatBlock();
            foreach (var u in heroes) {
                Combat.UnitStats.Compute(db, u, HeroRank.B, null, stats);
                var dmg = Math.Max(stats[StatKind.PhysicalDamage], stats[StatKind.MagicalDamage]);
                var dps = dmg * stats[StatKind.AttackSpeed] * (1 + stats[StatKind.CriticalChance] * (stats[StatKind.CriticalDamage] - 1));
                var ehp = stats[StatKind.MaxHp] * (1 + (stats[StatKind.Armor] + stats[StatKind.Resistance]) / 2 / db.Rules.DamageReductionConstant);
                md.AppendLine($"| {u.Id} | {u.Reputation} | {u.Realm} | {Roles(u)} | {F(stats[StatKind.MaxHp])} | {F(stats[StatKind.PhysicalDamage])} | " +
                              $"{F(stats[StatKind.MagicalDamage])} | {F(stats[StatKind.Armor])} | {F(stats[StatKind.Resistance])} | {F(stats[StatKind.AttackSpeed], 2)} | " +
                              $"{u.AttackRange} | {F(u.EnergyRegenPerAttack)} | {F(u.EnergyRegenPerHit)} | {F(dps)} | {F(ehp)} |");
            }
            md.AppendLine().AppendLine("dps* = auto-attacks only (main damage x attack speed x average crit); ehp* = hp x (1 + mean defense / 100).").AppendLine();
        }

        /// <summary>Every pair, both sides, three seeds, no synergies.</summary>
        void Duels() {
            var wins = heroes.ToDictionary(h => h.Id, _ => 0d);
            var games = heroes.ToDictionary(h => h.Id, _ => 0);
            var sameWins = heroes.ToDictionary(h => h.Id, _ => 0d);
            var sameGames = heroes.ToDictionary(h => h.Id, _ => 0);
            var power = heroes.ToDictionary(h => h.Id, _ => 0d);
            var powerGames = heroes.ToDictionary(h => h.Id, _ => 0);
            var byTier = new double[4, 4];
            var byTierGames = new int[4, 4];
            var seconds = new List<double>();
            foreach (var a in heroes) {
                foreach (var b in heroes) {
                    if (a == b) continue;
                    for (var seed = 0; seed < 5; seed++) {
                        var setup = new BattleSetup { Seed = (ulong)(seed * 7919 + a.Id.Length * 31 + b.Id.Length) };
                        setup.Home.Units.Add(Unit(a.Id, HeroRank.B, 2, 3));
                        setup.Away.Units.Add(Unit(b.Id, HeroRank.B, 5, 4));
                        var f = Run(setup);
                        var score = f.Battle.Outcome == BattleOutcome.HomeWin ? 1 : f.Battle.Outcome == BattleOutcome.AwayWin ? 0 : 0.5;
                        wins[a.Id] += score;
                        wins[b.Id] += 1 - score;
                        games[a.Id]++;
                        games[b.Id]++;
                        if (a.Reputation == b.Reputation) {
                            sameWins[a.Id] += score;
                            sameWins[b.Id] += 1 - score;
                            sameGames[a.Id]++;
                            sameGames[b.Id]++;
                            var margin = Margin(f.Battle);
                            power[a.Id] += margin;
                            power[b.Id] -= margin;
                            powerGames[a.Id]++;
                            powerGames[b.Id]++;
                        }
                        byTier[(int)a.Reputation, (int)b.Reputation] += score;
                        byTierGames[(int)a.Reputation, (int)b.Reputation]++;
                        seconds.Add(f.Seconds);
                    }
                }
            }
            MirroredTeams(power, powerGames);
            md.AppendLine("## 1v1 duels (1-star vs 1-star, no synergies)").AppendLine();
            md.AppendLine($"Median duel: {F(Median(seconds), 1)} s, p90 {F(Percentile(seconds, 0.9), 1)} s, time-outs {seconds.Count(s => s >= MaxSeconds - 0.1)}/{seconds.Count}.").AppendLine();
            md.AppendLine("| unit | tier | roles | win rate | vs own tier | power* |").AppendLine("|---|---|---|---|---|---|");
            var perHero = new Dictionary<string, object>();
            foreach (var h in heroes.OrderByDescending(h => wins[h.Id] / games[h.Id])) {
                var own = sameGames[h.Id] == 0 ? 0.5 : sameWins[h.Id] / sameGames[h.Id];
                var pw = powerGames[h.Id] == 0 ? 0 : power[h.Id] / powerGames[h.Id];
                md.AppendLine($"| {h.Id} | {h.Reputation} | {Roles(h)} | {F(100 * wins[h.Id] / games[h.Id])}% | {F(100 * own)}% | {F(pw, 2)} |");
                perHero[h.Id] = new Dictionary<string, object> {
                    ["tier"] = (int)h.Reputation, ["winRate"] = wins[h.Id] / games[h.Id], ["ownTier"] = own, ["power"] = pw,
                };
            }
            json["heroes"] = perHero;
            json["duelMedian"] = Median(seconds);
            md.AppendLine().AppendLine("power* = mean signed margin against the same tier (1v1 duels + mirrored 3v3 with the same two partners); 0 = even.").AppendLine();
            md.AppendLine("Tier vs tier (row wins):").AppendLine();
            md.AppendLine("| | Unknown | Elite | Legendary |").AppendLine("|---|---|---|---|");
            var tiers = new Dictionary<string, object>();
            for (var r = 1; r <= 3; r++) {
                md.Append($"| {(Reputation)r} |");
                for (var c = 1; c <= 3; c++) {
                    md.Append(byTierGames[r, c] == 0 ? " - |" : $" {F(100 * byTier[r, c] / byTierGames[r, c])}% |");
                    if (byTierGames[r, c] > 0) tiers[$"{r}v{c}"] = byTier[r, c] / byTierGames[r, c];
                }
                md.AppendLine();
            }
            json["tiers"] = tiers;

            // 2-star of a lower tier against 1-star of the next one (TFT: roughly even).
            md.AppendLine().AppendLine("2-star vs 1-star of the next tier (row = the 2-star tier):").AppendLine();
            for (var low = 1; low <= 2; low++) {
                double score = 0;
                var n = 0;
                foreach (var a in heroes.Where(h => (int)h.Reputation == low)) {
                    foreach (var b in heroes.Where(h => (int)h.Reputation == low + 1)) {
                        for (var side = 0; side < 2; side++) {
                            var setup = new BattleSetup { Seed = (ulong)(side * 101 + a.Id.Length + b.Id.Length * 13) };
                            var first = Unit(a.Id, HeroRank.A, side == 0 ? 2 : 5, 3);
                            var second = Unit(b.Id, HeroRank.B, side == 0 ? 5 : 2, 4);
                            if (side == 0) {
                                setup.Home.Units.Add(first);
                                setup.Away.Units.Add(second);
                            }
                            else {
                                setup.Home.Units.Add(second);
                                setup.Away.Units.Add(first);
                            }
                            var o = Run(setup).Battle.Outcome;
                            var aWon = side == 0 ? o == BattleOutcome.HomeWin : o == BattleOutcome.AwayWin;
                            score += o == BattleOutcome.Draw ? 0.5 : aWon ? 1 : 0;
                            n++;
                        }
                    }
                }
                md.AppendLine($"- {(Reputation)low} 2-star vs {(Reputation)(low + 1)} 1-star: {F(100 * score / Math.Max(1, n))}% ({n} duels)");
                json[$"twoStar{low}"] = score / Math.Max(1, n);
            }
            md.AppendLine();
        }

        /// <summary>
        /// A vs B inside a team: both sides get the same two partners of their tier (front / back by range), so
        /// area skills and tanking count. Adds to the same-tier power score.
        /// </summary>
        void MirroredTeams(Dictionary<string, double> power, Dictionary<string, int> games) {
            foreach (var tier in heroes.Select(h => h.Reputation).Distinct()) {
                var group = heroes.Where(h => h.Reputation == tier).ToList();
                var rng = new DeterministicRandom((ulong)tier * 7717);
                foreach (var a in group) {
                    foreach (var b in group) {
                        if (a == b) continue;
                        for (var round = 0; round < 3; round++) {
                            var others = heroes.Where(h => h != a && h != b && (group.Count >= 4 ? h.Reputation == tier : true)).ToList();
                            var x = others[rng.Range(0, others.Count)];
                            var y = others[rng.Range(0, others.Count)];
                            var setup = new BattleSetup { Seed = rng.NextUInt() };
                            setup.Home = Trio(a, x, y, false);
                            setup.Away = Trio(b, x, y, true);
                            var margin = Margin(Run(setup).Battle);
                            power[a.Id] += margin;
                            power[b.Id] -= margin;
                            games[a.Id]++;
                            games[b.Id]++;
                        }
                    }
                }
            }
        }

        TeamSetup Trio(UnitDefinition main, UnitDefinition x, UnitDefinition y, bool away) {
            var team = new TeamSetup { PlayerId = away ? 1 : 0, ApplyTraits = false };
            var front = 0;
            var back = 0;
            foreach (var def in new[] { main, x, y }) {
                var melee = def.AttackRange <= 1;
                var cell = melee ? new HexCoord(3, 2 + front++ * 2) : new HexCoord(1, 2 + back++ * 2);
                if (away) cell = cell.Mirror(db.Rules.BoardRows, db.Rules.BoardColumns);
                team.Units.Add(new CombatantSetup { UnitId = def.Id, Rank = HeroRank.B, Cell = cell });
            }
            return team;
        }

        /// <summary>Each hero alone against an unkillable, harmless dummy: how its damage is spread over time.</summary>
        void DummyMetrics() {
            var profile = new Dictionary<string, object>();
            json["profile"] = profile;
            md.AppendLine("## Damage profile vs a training dummy (1-star, 20 s)").AppendLine();
            md.AppendLine("| unit | roles | 0-5 s dps | 5-20 s dps | burst ratio | casts / 20 s | first cast |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var h in heroes) {
                var setup = new BattleSetup { Seed = 42 };
                setup.Home.Units.Add(Unit(h.Id, HeroRank.B, 3, 3));
                setup.Away.Units.Add(Unit("_Dummy", HeroRank.B, 4, 3, hpScale: 1000));
                var events = new EventBuffer();
                var battle = new BattleSimulation(1, db, setup, events);
                var buffer = new List<GameEvent>();
                double early = 0, late = 0;
                var casts = 0;
                var firstCast = double.NaN;
                for (var t = 0; t < 20 * db.Rules.TicksPerSecond && !battle.Finished; t++) {
                    battle.Step();
                    buffer.Clear();
                    events.DrainTo(buffer);
                    foreach (var e in buffer) {
                        if (e is UnitDamagedEvent d && d.SourceId == 1) {
                            if (battle.Time <= 5) early += d.HpLost;
                            else late += d.HpLost;
                        }
                        else if (e is UnitSkillStartedEvent s && s.UnitId == 1) {
                            casts++;
                            if (double.IsNaN(firstCast)) firstCast = battle.Time;
                        }
                    }
                }
                var earlyDps = early / 5;
                var lateDps = late / 15;
                profile[h.Id] = new Dictionary<string, object> {
                    ["early"] = earlyDps, ["late"] = lateDps, ["casts"] = casts, ["firstCast"] = double.IsNaN(firstCast) ? -1 : firstCast,
                };
                md.AppendLine($"| {h.Id} | {Roles(h)} | {F(earlyDps)} | {F(lateDps)} | {F(earlyDps / Math.Max(1, lateDps), 2)} | {casts} | " +
                              $"{(double.IsNaN(firstCast) ? "-" : F(firstCast, 1) + " s")} |");
            }
            md.AppendLine().AppendLine("burst ratio = early dps / sustained dps (assassins high, marksmen ~1).").AppendLine();
        }

        /// <summary>Random boards of a player's level (shop odds of that level, some 2-stars), synergies on.</summary>
        void TeamFights() {
            var fightsJson = new Dictionary<string, object>();
            json["teamFights"] = fightsJson;
            md.AppendLine("## Team fights (random boards, synergies on)").AppendLine();
            md.AppendLine("| level | fights | median | p90 | time-outs | dodges / fight | summons |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var level in new[] { 3, 5, 7, 9 }) {
                var rng = new DeterministicRandom((ulong)level * 1337);
                var seconds = new List<double>();
                var timeouts = 0;
                var dodges = 0;
                var summons = 0;
                const int fights = 60;
                for (var i = 0; i < fights; i++) {
                    var setup = new BattleSetup { Seed = rng.NextUInt() };
                    setup.Home = Board(level, rng, false);
                    setup.Away = Board(level, rng, true);
                    var f = Run(setup);
                    seconds.Add(f.Seconds);
                    if (f.Battle.Outcome == BattleOutcome.Draw) timeouts++;
                    dodges += f.Dodges;
                    summons += f.Summons;
                }
                md.AppendLine($"| {level} | {fights} | {F(Median(seconds), 1)} s | {F(Percentile(seconds, 0.9), 1)} s | {timeouts} | {F(dodges / (double)fights, 1)} | {summons} |");
                fightsJson[level.ToString(Inv)] = new Dictionary<string, object> {
                    ["median"] = Median(seconds), ["p90"] = Percentile(seconds, 0.9), ["timeouts"] = timeouts,
                };
            }
            md.AppendLine();
        }

        TeamSetup Board(int level, DeterministicRandom rng, bool away, IReadOnlyList<string> forced = null) {
            var team = new TeamSetup { PlayerId = away ? 1 : 0, ApplyTraits = true };
            var odds = db.Rules.Level(level).ShopOdds;
            var picks = new List<string>();
            if (forced != null) picks.AddRange(forced);
            while (picks.Count < level) {
                var roll = rng.NextFloat() * 100;
                var tier = roll < odds[0] ? Reputation.Unknown : roll < odds[0] + odds[1] ? Reputation.Elite : Reputation.Legendary;
                var pool = db.ShopPool(tier);
                if (pool.Count == 0) continue;
                picks.Add(pool[rng.Range(0, pool.Count)].Id);
            }
            var front = 0;
            var back = 0;
            foreach (var id in picks) {
                var def = db.Unit(id);
                var melee = def.AttackRange <= 1;
                var row = melee ? 3 - front / 8 : back / 8;
                var col = melee ? front++ % 8 : back++ % 8;
                var cell = new HexCoord(row, col);
                if (away) cell = cell.Mirror(db.Rules.BoardRows, db.Rules.BoardColumns);
                var rank = rng.NextFloat() < (level - 2) * 0.06 ? HeroRank.A : HeroRank.B;
                team.Units.Add(new CombatantSetup { UnitId = id, Rank = rank, Cell = cell });
            }
            return team;
        }

        /// <summary>The same board with and without its synergies, for every trait at its first tier.</summary>
        void TraitValue() {
            var traitsJson = new Dictionary<string, object>();
            json["traits"] = traitsJson;
            md.AppendLine("## Synergy value (same board, the trait on vs off)").AppendLine();
            md.AppendLine("| trait | members used | win rate with the trait |").AppendLine("|---|---|---|");
            foreach (var trait in db.Traits.OrderBy(t => t.Id)) {
                var members = heroes.Where(h => trait.Kind == TraitKind.Role ? h.Roles.HasRole(trait.Role) : h.Realm == trait.Realm)
                    .Select(h => h.Id).ToList();
                var need = trait.Checkpoints.Length > 0 ? trait.Checkpoints[0] : 1;
                if (members.Count < need) {
                    md.AppendLine($"| {trait.Name ?? trait.Id.ToString()} ({trait.Id}) | only {members.Count} unit(s), needs {need} | - |");
                    continue;
                }
                var rng = new DeterministicRandom((ulong)trait.Id.GetHashCode() & 0xffff);
                double score = 0;
                const int runs = 120;
                for (var i = 0; i < runs; i++) {
                    var forced = members.OrderBy(_ => rng.NextUInt()).Take(need).ToList();
                    var seed = rng.NextUInt();
                    // the trait side alternates between home and away so the home side's first move does not count
                    var withHome = i % 2 == 0;
                    var home = Board(Math.Max(need + 2, 5), new DeterministicRandom(seed), false, forced);
                    var away = Board(Math.Max(need + 2, 5), new DeterministicRandom(seed), true, forced);
                    (withHome ? away : home).DisabledTraits.Add(trait.Id);
                    var o = Run(new BattleSetup { Seed = rng.NextUInt(), Home = home, Away = away }).Battle.Outcome;
                    var won = withHome ? BattleOutcome.HomeWin : BattleOutcome.AwayWin;
                    score += o == won ? 1 : o == BattleOutcome.Draw ? 0.5 : 0;
                }
                md.AppendLine($"| {trait.Id} | {need} | {F(100 * score / runs)}% |");
                traitsJson[trait.Id.ToString()] = score / runs;
            }
            md.AppendLine().AppendLine("Both sides field the same units and synergies, except the tested trait (on one side, alternating home / away). Mirrored boards amplify small edges (+10% damage on one unit already wins ~75%), so ~70-85% at the first tier is healthy; 50% means the trait does nothing.").AppendLine();
        }

        /// <summary>
        /// Each combined item on one unit of a random level-6 board (the unit its carrier tag in Tools/items/items.json
        /// points at) against the same board without it; sides alternate.
        /// </summary>
        void ItemValue(string root) {
            var carriers = new Dictionary<string, string>();
            var config = root == null ? null : Path.Combine(root, "Tools", "items", "items.json");
            if (config != null && File.Exists(config)) {
                var items = (List<object>)((Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(config)))["items"];
                foreach (Dictionary<string, object> item in items) carriers[(string)item["id"]] = (string)item["carrier"];
            }
            var itemsJson = new Dictionary<string, object>();
            json["items"] = itemsJson;
            md.AppendLine("## Item value (same board, one unit with the item vs without)").AppendLine();
            md.AppendLine("| item | name | carrier | win rate |").AppendLine("|---|---|---|---|");
            const int runs = 300;
            const int level = 6;
            foreach (var item in db.CombinedItems.OrderBy(i => i.Id, StringComparer.Ordinal)) {
                carriers.TryGetValue(item.Id, out var carrier);
                carrier ??= "any";
                var rng = new DeterministicRandom((ulong)(item.Id.GetHashCode() & 0x7fffffff) + 17);
                double score = 0;
                for (var i = 0; i < runs; i++) {
                    var seed = rng.NextUInt();
                    var withHome = i % 2 == 0;
                    var home = Board(level, new DeterministicRandom(seed), false);
                    var away = Board(level, new DeterministicRandom(seed), true);
                    var holder = withHome ? home : away;
                    holder.Units[Carrier(holder, carrier)].Items = new[] { item.Id };
                    var o = Run(new BattleSetup { Seed = rng.NextUInt(), Home = home, Away = away }).Battle.Outcome;
                    var won = withHome ? BattleOutcome.HomeWin : BattleOutcome.AwayWin;
                    score += o == won ? 1 : o == BattleOutcome.Draw ? 0.5 : 0;
                }
                md.AppendLine($"| {item.Id} | {item.Name} | {carrier} | {F(100 * score / runs)}% |");
                itemsJson[item.Id] = score / runs;
            }
            md.AppendLine().AppendLine("One item on one of six units of a mirrored board; 50% = no effect.").AppendLine();
        }

        /// <summary>Index of the unit an item of <paramref name="carrier"/> type goes to.</summary>
        int Carrier(TeamSetup team, string carrier) {
            double Score(UnitDefinition d) => carrier switch {
                "ad" => d.PhysicalDamage * d.AttackSpeed,
                "ap" => d.MagicalDamage,
                "tank" => d.MaxHp * (d.AttackRange <= 1 ? 2 : 1),
                "attack" => (d.PhysicalDamage + d.MagicalDamage) * d.AttackSpeed,
                _ => (int)d.Reputation * 10000 + d.MaxHp,
            };
            var best = 0;
            for (var i = 1; i < team.Units.Count; i++) {
                if (Score(db.Unit(team.Units[i].UnitId)) > Score(db.Unit(team.Units[best].UnitId))) best = i;
            }
            return best;
        }

        static double Median(List<double> v) => Percentile(v, 0.5);

        static double Percentile(List<double> v, double p) {
            if (v.Count == 0) return 0;
            var s = v.OrderBy(x => x).ToList();
            return s[Math.Clamp((int)Math.Round(p * (s.Count - 1)), 0, s.Count - 1)];
        }
    }
}
