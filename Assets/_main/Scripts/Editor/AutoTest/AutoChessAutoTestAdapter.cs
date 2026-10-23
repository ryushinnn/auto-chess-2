using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AutoChess.Core;
using AutoChess.Core.Combat;
using AutoChess.Core.Net;
using AutoTest;
using UnityEditor;
using UnityEngine;

namespace AutoChess.Game.Editor {
    /// <summary>
    /// AutoTest game adapter (see AutoTest Server/PROTOCOL.md): exposes the match state, gameplay and cheat
    /// commands, model / view consistency rules and clickable targets of the Arena scene to the test rig.
    /// Everything goes through the same <see cref="IGameSession"/> the UI uses, so a scenario exercises the
    /// real game path; the view-side rules then check that what is drawn matches the model.
    /// </summary>
    public sealed class AutoChessAutoTestAdapter : IAutoTestGameAdapter, IAutoTestGameOps, IAutoTestInvariants, IAutoTestTargets {
        public string GameId => "auto-chess";

        static GameManager Game => GameManager.Instance;
        static IGameSession Session => Game.Session;
        static MatchRecorder Recorder => Game.LocalSession?.Recorder ?? Game.OnlineSession?.Recorder;
        static LocalGameSession Local => Game.LocalSession ?? throw new InvalidOperationException("offline only: an online match is running");

        public bool IsAvailable() => EditorApplication.isPlaying && Game != null && Game.IsReady;

        // ------------------------------------------------------------------ state

        public object GetState() {
            var session = Session;
            var state = session.State;
            var p = session.LocalPlayer;
            var rules = session.Db.Rules;
            var ui = UIManager_Arena.Instance;
            var battle = state.BattleOf(p.Id);

            return new Dictionary<string, object> {
                ["seed"] = Game.Seed.ToString(CultureInfo.InvariantCulture),
                ["tick"] = state.Tick,
                ["phase"] = state.Phase.ToString(),
                ["phaseTimeLeft"] = Round(session.PhaseTimeLeft),
                ["phaseDuration"] = Round(session.PhaseDuration),
                ["stage"] = state.StageIndex + 1,
                ["round"] = state.RoundIndex + 1,
                ["roundLabel"] = $"{state.StageIndex + 1}-{state.RoundIndex + 1}",
                ["boardLocked"] = session.BoardLocked,
                ["flags"] = new Dictionary<string, object> {
                    ["isReadyToPlay"] = state.Phase == MatchPhase.Preparation,
                    ["isSetup"] = state.Phase == MatchPhase.Setup,
                    ["isGameOver"] = state.Phase == MatchPhase.Finished,
                    ["inBattle"] = state.Phase == MatchPhase.Battle,
                },
                ["busy"] = new Dictionary<string, object> {
                    ["any"] = state.Phase is MatchPhase.Transition or MatchPhase.Battle or MatchPhase.Summary,
                    ["battleRunning"] = battle != null && battle.Started && !battle.Finished,
                },
                ["player"] = PlayerBlock(p, rules),
                ["roundKind"] = state.RoundKind.ToString(),
                ["viewed"] = Game.ViewedPlayerId,
                ["players"] = state.Players.Select(x => (object)new Dictionary<string, object> {
                    ["id"] = x.Id, ["name"] = x.Name, ["health"] = x.Health, ["level"] = x.Level, ["gold"] = x.Gold,
                    ["eliminated"] = x.Eliminated, ["placement"] = x.Placement, ["opponent"] = x.OpponentId,
                }).ToList(),
                ["battle"] = battle == null ? null : BattleBlock(battle),
                ["views"] = new Dictionary<string, object> {
                    ["roster"] = Game.Roster.Views.Count,
                    ["battle"] = Game.Battle.Views.Count,
                    ["battleVisible"] = Game.Battle.VisibleBattle != null,
                    ["boardVisible"] = Game.Roster.BoardVisible,
                    ["ui"] = new Dictionary<string, object> {
                        ["starterPack"] = ui.StarterPack.IsOpen,
                        ["arena"] = ui.Arena.IsOpen,
                        ["shop"] = ui.Shop.IsOpen,
                        ["inventory"] = ui.Inventory.IsOpen,
                        ["destinies"] = ui.Destinies.IsOpen,
                        ["heroInfo"] = ui.HeroInfo.IsOpen ? ui.HeroInfo.UnitId : -1,
                    },
                },
                ["recording"] = Recorder == null ? null : new Dictionary<string, object> {
                    ["commands"] = Recorder.Recording.Commands.Count,
                },
                ["online"] = Game.OnlineSession == null ? null : new Dictionary<string, object> {
                    ["match"] = Game.OnlineSession.Info.MatchId,
                    ["frame"] = Game.OnlineSession.Frame,
                    ["buffered"] = Game.OnlineSession.BufferedFrames,
                    ["desynced"] = Game.OnlineSession.IsDesynced,
                    ["ended"] = Game.OnlineSession.IsEnded,
                },
                ["connection"] = Game.Online?.State.ToString(),
                ["stateHash"] = StateHasher.Hash(state).ToString("x16"),
            };
        }

        static Dictionary<string, object> PlayerBlock(PlayerState p, GameRules rules) {
            var units = p.Units.Select(u => (object)new Dictionary<string, object> {
                ["id"] = u.Id,
                ["unit"] = u.DefinitionId,
                ["rank"] = u.Rank.ToString(),
                ["location"] = u.Location.ToString(),
                ["onBoard"] = u.Location.IsBoard,
                ["items"] = u.Items.ToList(),
            }).ToList();
            var traits = Traits.ForBoard(Session.Db, p).Select(t => (object)new Dictionary<string, object> {
                ["id"] = t.Id.ToString(), ["count"] = t.Count, ["tier"] = t.Tier, ["active"] = t.Active,
            }).ToList();
            var maxLevel = Economy.IsMaxLevel(rules, p);
            return new Dictionary<string, object> {
                ["id"] = p.Id,
                ["gold"] = p.Gold,
                ["level"] = p.Level,
                ["xp"] = p.Xp,
                ["xpToNext"] = maxLevel ? -1 : Economy.XpToNext(rules, p),
                ["boardCount"] = p.BoardCount,
                ["boardMax"] = Economy.MaxBoardUnits(rules, p),
                ["benchCount"] = p.Bench.Count(x => x != null),
                ["ready"] = p.Ready,
                ["starterChosen"] = p.StarterChosen,
                ["wins"] = p.Wins,
                ["losses"] = p.Losses,
                ["lastOutcome"] = p.LastOutcome.ToString(),
                ["health"] = p.Health,
                ["eliminated"] = p.Eliminated,
                ["placement"] = p.Placement,
                ["streak"] = p.Streak,
                ["opponent"] = p.OpponentId,
                ["units"] = units,
                ["unitCount"] = units.Count,
                ["bench"] = p.Bench.Select(u => (object)u?.Id).ToList(),
                ["items"] = p.Items.Select(s => (object)new Dictionary<string, object> { ["id"] = s.ItemId, ["count"] = s.Count }).ToList(),
                ["itemCount"] = p.Items.Sum(s => s.Count),
                ["shop"] = new Dictionary<string, object> {
                    ["slots"] = p.Shop.Slots.Select(x => (object)x).ToList(),
                    ["locked"] = p.Shop.Locked,
                    ["insurance"] = p.Shop.Insurance,
                },
                ["starterOffers"] = p.StarterOffers.Select(o => (object)new Dictionary<string, object> {
                    ["unit"] = o.UnitId, ["item"] = o.ItemId,
                }).ToList(),
                ["traits"] = traits,
            };
        }

        static Dictionary<string, object> BattleBlock(BattleSimulation b) {
            var units = b.Units.Select(u => (object)new Dictionary<string, object> {
                ["id"] = u.Id,
                ["unit"] = u.Def.Id,
                ["side"] = u.Side.ToString(),
                ["rank"] = u.Rank.ToString(),
                ["alive"] = u.Alive,
                ["hp"] = Round(u.Hp),
                ["maxHp"] = Round(u.MaxHp),
                ["energy"] = Round(u.Energy),
                ["cell"] = u.OccupiedCell >= 0 ? b.Grid.CoordOf(u.OccupiedCell).ToString() : null,
                ["pos"] = new[] { Round(u.Position.X), Round(u.Position.Y) },
                ["target"] = u.Target?.Id,
                ["attacking"] = u.Attack.Active,
                ["casting"] = u.Skill.Active,
                ["moving"] = u.Movement.Moving,
                ["stunned"] = u.Status.IsStunned,
                ["airborne"] = u.Status.IsAirborne,
                ["silenced"] = u.Status.IsSilenced,
                ["marks"] = u.Marks.Select(m => (object)$"{m.Key}x{m.Stacks}").ToList(),
            }).ToList();
            return new Dictionary<string, object> {
                ["id"] = b.Id,
                ["tick"] = b.Tick,
                ["time"] = Round(b.Time),
                ["started"] = b.Started,
                ["finished"] = b.Finished,
                ["outcome"] = b.Outcome.ToString(),
                ["homeAlive"] = b.AliveUnits(TeamSide.Home).Count,
                ["awayAlive"] = b.AliveUnits(TeamSide.Away).Count,
                ["units"] = units,
                ["projectiles"] = b.Projectiles.Count,
                ["areas"] = b.Areas.Count,
            };
        }

        static double Round(double v) => double.IsInfinity(v) ? -1 : Math.Round(v, 3);

        // ------------------------------------------------------------------ ops

        static readonly string[] OpNames = {
            "restart", "buy", "refresh", "lock", "buy-xp", "move", "equip", "ready", "choose-starter", "reroll-starter",
            "gold", "xp", "item", "unit", "skip-phase", "set-shop", "fast-forward", "speed", "set-seed",
            "export-data", "migrate", "missing-scripts", "unit-screen", "cell-screen", "can-move", "save-recording",
            "online-connect", "online-find", "online-cancel", "online-leave", "online-disconnect", "view", "sell",
        };

        public IReadOnlyList<string> Ops => OpNames;

        public object GameOp(string op, IReadOnlyDictionary<string, string> args) {
            switch (op) {
                // edit-mode tools
                case "export-data": return new { path = GameDataExporter.Export() };
                case "migrate": return new { report = ProjectMigration.Run() };
                case "missing-scripts": return new { missing = ProjectMigration.FindMissingScripts() };
                case "set-seed":
                    PlayerPrefs.SetInt(GameManager.SeedOverrideKey, Int(args, "seed"));
                    PlayerPrefs.Save();
                    return new { nextSeed = Int(args, "seed") };
            }

            if (!IsAvailable()) throw new InvalidOperationException("the game is not running (enter Play mode in the Arena scene)");
            if (op.StartsWith("online-", StringComparison.Ordinal)) return OnlineOp(op, args);
            if (op == "restart") {
                // Defaults keep the scenarios' solo, untimed match; bots=7 untimed=false is the real offline lobby.
                Game.Restart(args.ContainsKey("seed") ? (ulong)(uint)Int(args, "seed") : (ulong?)null, Int(args, "bots", 0),
                    Bool(args, "untimed", true));
                return new {
                    seed = Game.Seed.ToString(CultureInfo.InvariantCulture), phase = Session.State.Phase.ToString(),
                    players = Session.State.Players.Count
                };
            }
            var p = Session.LocalPlayer;
            switch (op) {
                case "buy": return Submit(new BuyUnitCommand { ShopSlot = Int(args, "slot") });
                case "refresh": return Submit(new RefreshShopCommand());
                case "lock": return Submit(new SetShopLockCommand { Locked = Bool(args, "on", true) });
                case "buy-xp": return Submit(new BuyXpCommand());
                case "move": return Submit(new MoveUnitCommand { UnitId = UnitArg(args, p), Target = Location(args, "to") });
                case "equip": return Submit(new EquipItemCommand { ItemId = Str(args, "item"), UnitId = UnitArg(args, p) });
                case "ready": return Submit(new ReadyCommand());
                case "choose-starter": return Submit(new ChooseStarterCommand { OfferIndex = Int(args, "offer") });
                case "reroll-starter": return Submit(new RerollStarterCommand { OfferIndex = Int(args, "offer") });
                case "gold": return Submit(new CheatGoldCommand { Amount = Int(args, "amount") });
                case "xp": return Submit(new CheatXpCommand { Amount = Int(args, "amount") });
                case "item": return Submit(new CheatItemCommand { ItemId = Str(args, "item"), Count = Int(args, "count", 1) });
                case "unit":
                    return Submit(new CheatUnitCommand {
                        UnitId = Str(args, "unit"), Rank = (HeroRank)Enum.Parse(typeof(HeroRank), Str(args, "rank", "B"), true)
                    });
                case "skip-phase": return Submit(new CheatSkipPhaseCommand());
                case "sell": return Submit(new SellUnitCommand { UnitId = UnitArg(args, p) });
                case "view":
                    // Scout another player's board (player=<id>); no argument = back home.
                    if (args.ContainsKey("player")) Game.ViewPlayer(Int(args, "player"));
                    else Game.ViewHome();
                    return new { viewed = Game.ViewedPlayerId, scouting = Game.IsScouting };
                case "set-shop":
                    return Submit(new CheatSetShopCommand {
                        UnitIds = Str(args, "units", "").Split(',').Select(s => s.Trim()).Select(s => s.Length == 0 || s == "-" ? null : s).ToArray()
                    });
                case "fast-forward": {
                    var seconds = Float(args, "seconds", 0);
                    var ticks = args.ContainsKey("ticks") ? Int(args, "ticks") : Session.Db.Rules.SecondsToTicks(seconds);
                    Local.RunTicks(Math.Max(0, ticks));
                    return new { ticks, tick = Session.State.Tick, phase = Session.State.Phase.ToString() };
                }
                case "speed":
                    Local.Speed = Float(args, "value", 1);
                    return new { speed = Local.Speed };
                case "can-move": {
                    var error = Session.CanMove(UnitArg(args, p), Location(args, "to"));
                    return new { ok = error == CommandError.None, error = error.ToString() };
                }
                case "unit-screen": return UnitScreen(args);
                case "cell-screen": return CellScreen(Location(args, "at"));
                case "save-recording": return SaveRecording(Str(args, "path", ""));
                default: throw new ArgumentException($"unknown op '{op}' (op=list shows them)");
            }
        }

        static object Submit(GameCommand command) {
            CommandResult result = default;
            Session.Submit(command, r => result = r);
            return new Dictionary<string, object> {
                ["ok"] = result.Ok,
                ["error"] = result.Error.ToString(),
                ["message"] = result.Message,
                ["command"] = command.Type.ToString(),
            };
        }

        /// <summary>"unit" = unit id, or "unit=Ashe" / "def=Ashe" picks the first owned copy of that hero.</summary>
        static int UnitArg(IReadOnlyDictionary<string, string> args, PlayerState p) {
            if (args.TryGetValue("def", out var def) || (args.TryGetValue("unit", out def) && !int.TryParse(def, out _))) {
                var unit = p.Units.FirstOrDefault(u => u.DefinitionId == def);
                if (unit == null) throw new ArgumentException($"no owned unit of '{def}'");
                return unit.Id;
            }
            return Int(args, "unit");
        }

        static UnitLocation Location(IReadOnlyDictionary<string, string> args, string key) {
            var text = Str(args, key);
            if (!UnitLocation.TryParse(text, out var location)) throw new ArgumentException($"'{key}' must look like bench:3 or board:1,4 (got '{text}')");
            return location;
        }

        static object UnitScreen(IReadOnlyDictionary<string, string> args) {
            var id = Int(args, "unit");
            Transform t = null;
            string kind = null;
            if (Game.Roster.FindView(id) is { } roster && roster.gameObject.activeInHierarchy) {
                t = roster.transform;
                kind = "roster";
            }
            else if (Game.Battle.FindView(id) is { } battle && battle.gameObject.activeInHierarchy) {
                t = battle.transform;
                kind = "battle";
            }
            if (t == null) return new { found = false };
            var screen = Camera.main.WorldToScreenPoint(t.position);
            return new { found = true, kind, screen = new[] { screen.x, screen.y }, world = new[] { t.position.x, t.position.y, t.position.z } };
        }

        static object CellScreen(UnitLocation location) {
            var world = MapVisual.Instance.LocationToWorld(location);
            var screen = Camera.main.WorldToScreenPoint(world);
            return new { location = location.ToString(), screen = new[] { screen.x, screen.y }, world = new[] { world.x, world.y, world.z } };
        }

        /// <summary>Online play (auto-chess-2-server): connect, queue, leave. The match then runs like a local one.</summary>
        static object OnlineOp(string op, IReadOnlyDictionary<string, string> args) {
            var online = Game.Online;
            switch (op) {
                case "online-connect":
                    online.Connect(Str(args, "url", AppSession.DefaultUrl), Str(args, "name", "AutoTest"), Str(args, "device", "autotest-editor"));
                    break;
                case "online-find": online.FindMatch(); break;
                case "online-cancel": online.CancelFind(); break;
                case "online-leave": Game.Restart(); break;
                case "online-disconnect": online.Disconnect(); break;
            }
            return new { state = online.State.ToString(), status = online.Status };
        }

        static object SaveRecording(string path) {
            var recorder = Recorder;
            if (recorder == null) throw new InvalidOperationException("this session does not record");
            var rec = recorder.Recording;
            var root = Directory.GetParent(Application.dataPath)!.FullName;
            if (string.IsNullOrEmpty(path)) path = Path.Combine(root, "AutoTest Server", "auto_touch", "out", "recordings", $"match_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = MiniJson.Serialize(new Dictionary<string, object> {
                ["seed"] = rec.Seed.ToString(CultureInfo.InvariantCulture),
                ["players"] = rec.PlayerCount,
                ["allowCheats"] = rec.AllowCheats,
                ["endTick"] = rec.EndTick,
                ["commands"] = rec.Commands.Select(c => (object)new Dictionary<string, object> {
                    ["tick"] = c.tick, ["data"] = Convert.ToBase64String(c.command),
                    ["text"] = CommandCodec.Decode(c.command).ToString(),
                }).ToList(),
            });
            File.WriteAllText(path, json);
            return new { path, commands = rec.Commands.Count };
        }

        static string Str(IReadOnlyDictionary<string, string> args, string key, string fallback = null) {
            if (args.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (fallback != null) return fallback;
            throw new ArgumentException($"missing '{key}'");
        }

        static int Int(IReadOnlyDictionary<string, string> args, string key, int? fallback = null) {
            if (args.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
            if (fallback.HasValue) return fallback.Value;
            throw new ArgumentException($"'{key}' must be an integer");
        }

        static float Float(IReadOnlyDictionary<string, string> args, string key, float fallback) =>
            args.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

        static bool Bool(IReadOnlyDictionary<string, string> args, string key, bool fallback) {
            if (!args.TryGetValue(key, out var v)) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ invariants

        static readonly string[] RuleNames = {
            "gold_non_negative", "level_bounds", "board_within_limit", "roster_slots_consistent", "unit_items_valid",
            "item_bench_valid", "shop_slots_valid", "roster_views_match_model", "battle_hp_bounds", "battle_cells_unique",
            "battle_views_match_model", "phase_timer_valid",
        };

        public IReadOnlyList<string> Rules => RuleNames;

        public List<InvariantFinding> Check(ISet<string> only) {
            var findings = new List<InvariantFinding>();
            if (!IsAvailable()) return findings;
            bool On(string rule) => only == null || only.Contains(rule);
            void Add(string rule, string severity, string detail, object data = null) =>
                findings.Add(new InvariantFinding { rule = rule, severity = severity, detail = detail, data = data });

            var session = Session;
            var db = session.Db;
            var rules = db.Rules;
            var state = session.State;
            var p = session.LocalPlayer;

            if (On("gold_non_negative") && p.Gold < 0) Add("gold_non_negative", "high", $"gold is {p.Gold}");

            if (On("level_bounds")) {
                if (p.Level < 1 || p.Level > rules.MaxLevel) Add("level_bounds", "high", $"level {p.Level}");
                else if (!Economy.IsMaxLevel(rules, p) && p.Xp >= Economy.XpToNext(rules, p)) Add("level_bounds", "high", $"xp {p.Xp} should have levelled up");
            }

            if (On("board_within_limit") && p.BoardCount > Economy.MaxBoardUnits(rules, p)) {
                Add("board_within_limit", "high", $"{p.BoardCount} units on a {Economy.MaxBoardUnits(rules, p)}-unit board");
            }

            if (On("roster_slots_consistent")) {
                foreach (var u in p.Units) {
                    var ok = u.Location.Kind switch {
                        LocationKind.Bench => u.Location.Slot >= 0 && u.Location.Slot < p.Bench.Length && p.Bench[u.Location.Slot] == u,
                        LocationKind.Board => db.Grid.Contains(u.Location.Cell) && u.Location.Cell.Row < rules.PlayerRows
                                              && p.Board[p.BoardIndex(u.Location.Cell)] == u,
                        _ => false
                    };
                    if (!ok) Add("roster_slots_consistent", "high", $"unit {u} is not where its location says");
                }
                var referenced = p.Bench.Where(x => x != null).Concat(p.Board.Where(x => x != null)).ToList();
                if (referenced.Count != p.Units.Count || referenced.Distinct().Count() != referenced.Count) {
                    Add("roster_slots_consistent", "high", $"{referenced.Count} slot references for {p.Units.Count} units");
                }
            }

            if (On("unit_items_valid")) {
                foreach (var u in p.Units) {
                    if (u.Items.Count > rules.ItemCapacity) Add("unit_items_valid", "high", $"unit {u.Id} holds {u.Items.Count} items");
                    foreach (var id in u.Items) {
                        if (!db.TryGetItem(id, out _)) Add("unit_items_valid", "high", $"unit {u.Id} holds unknown item {id}");
                    }
                    var components = u.Items.Count(id => db.TryGetItem(id, out var it) && !it.IsCombined);
                    if (components > 1) Add("unit_items_valid", "medium", $"unit {u.Id} holds {components} loose components (should have combined)");
                }
            }

            if (On("item_bench_valid")) {
                foreach (var s in p.Items) {
                    if (s.Count <= 0 || !db.TryGetItem(s.ItemId, out _)) Add("item_bench_valid", "medium", $"item stack {s.ItemId} x{s.Count}");
                }
            }

            if (On("shop_slots_valid")) {
                foreach (var id in p.Shop.Slots) {
                    if (id != null && (!db.TryGetUnit(id, out var def) || def.Summoned)) Add("shop_slots_valid", "high", $"shop offers '{id}'");
                }
            }

            if (On("roster_views_match_model")) CheckRosterViews(Game.ViewedPlayer, state, Add); // the board shows the viewed player
            var battle = state.BattleOf(p.Id);
            if (battle != null) {
                if (On("battle_hp_bounds")) {
                    foreach (var u in battle.Units) {
                        if (u.Hp > u.MaxHp + 0.01 || u.Hp < 0 || double.IsNaN(u.Hp)) Add("battle_hp_bounds", "high", $"{u}");
                        if (!u.Alive && u.Hp > 0) Add("battle_hp_bounds", "high", $"dead unit with hp {u}");
                    }
                }
                if (On("battle_cells_unique")) {
                    var seen = new Dictionary<int, CombatUnit>();
                    foreach (var u in battle.Units) {
                        if (!u.Alive || u.OccupiedCell < 0) continue;
                        if (seen.TryGetValue(u.OccupiedCell, out var other)) Add("battle_cells_unique", "high", $"{u} and {other} share a cell");
                        else seen[u.OccupiedCell] = u;
                    }
                }
                if (On("battle_views_match_model") && Game.Battle.VisibleBattle == battle) {
                    foreach (var u in battle.Units) {
                        var view = Game.Battle.FindView(u.Id);
                        if (view == null || !view.gameObject.activeInHierarchy) {
                            Add("battle_views_match_model", "high", $"battle unit {u.Id} has no view");
                            continue;
                        }
                        var drawn = new Vector2(view.transform.position.x, view.transform.position.z);
                        var expected = BoardSpace.BattleToWorld(u.Position); // rotated when we are the visiting team
                        var model = new Vector2(expected.x, expected.z);
                        var allowed = u.MovementSpeed * db.Grid.AverageCellDistance * rules.TickDelta * 2 + 0.05f;
                        if ((drawn - model).magnitude > allowed) {
                            Add("battle_views_match_model", "medium", $"unit {u.Id} drawn {(drawn - model).magnitude:0.00} away from its model");
                        }
                    }
                }
            }

            if (On("phase_timer_valid") && (state.PhaseTicksLeft < 0 || state.PhaseTicksLeft > state.PhaseTicksTotal)) {
                Add("phase_timer_valid", "low", $"{state.PhaseTicksLeft}/{state.PhaseTicksTotal} ticks");
            }
            return findings;
        }

        static void CheckRosterViews(PlayerState p, MatchState state, Action<string, string, string, object> add) {
            var views = Game.Roster.Views;
            foreach (var u in p.Units) {
                if (!views.TryGetValue(u.Id, out var view)) {
                    add("roster_views_match_model", "high", $"unit {u.Id} ({u.DefinitionId}) has no view", null);
                    continue;
                }
                if (view.Location != u.Location) add("roster_views_match_model", "high", $"unit {u.Id} drawn at {view.Location}, model says {u.Location}", null);
                if (view.Rank != u.Rank) add("roster_views_match_model", "medium", $"unit {u.Id} drawn as rank {view.Rank}, model says {u.Rank}", null);
                if (view.Trait == null || view.Trait.id != u.DefinitionId) add("roster_views_match_model", "high", $"unit {u.Id} drawn as the wrong hero", null);
                var shouldShow = !u.Location.IsBoard || Game.Roster.BoardVisible || state.Phase == MatchPhase.Transition;
                if (shouldShow && state.Phase == MatchPhase.Preparation && !view.gameObject.activeInHierarchy) {
                    add("roster_views_match_model", "high", $"unit {u.Id} is hidden during preparation", null);
                }
            }
            foreach (var id in views.Keys) {
                if (p.FindUnit(id) == null) add("roster_views_match_model", "high", $"view of unit {id} that no longer exists", null);
            }
        }

        // ------------------------------------------------------------------ targets

        public List<TargetInfo> GetTargets() {
            var targets = new List<TargetInfo>();
            if (!IsAvailable()) return targets;
            var ui = UIManager_Arena.Instance;
            var p = Session.LocalPlayer;

            var cards = ui.Shop.GetComponentsInChildren<Shop_Hero>(true);
            foreach (var card in cards) {
                var slot = card.Slot;
                var id = slot < p.Shop.Slots.Length ? p.Shop.Slots[slot] : null;
                targets.Add(AutoTestUtil.Target($"shop:{slot}", "shop", card.gameObject, id ?? "(sold)",
                    new Dictionary<string, object> { ["unit"] = id, ["sold"] = card.Sold }));
            }

            var offers = ui.StarterPack.GetComponentsInChildren<StarterPack_Offer>(true);
            for (var i = 0; i < offers.Length; i++) {
                targets.Add(AutoTestUtil.Target($"starter:{i}", "starter", offers[i].gameObject, p.StarterOffers[i].UnitId,
                    new Dictionary<string, object> { ["unit"] = p.StarterOffers[i].UnitId, ["item"] = p.StarterOffers[i].ItemId }));
            }

            foreach (var item in ui.Inventory.GetComponentsInChildren<Inventory_Item>(true)) {
                if (item.Empty) continue;
                targets.Add(AutoTestUtil.Target($"item:{item.ItemId}", "item", item.gameObject, item.ItemId));
            }

            var cam = Camera.main;
            var rules = Session.Db.Rules;
            for (var s = 0; s < rules.BenchSize; s++) {
                targets.Add(WorldTarget(cam, $"bench:{s}", "bench", MapVisual.Instance.LocationToWorld(UnitLocation.Bench(s)),
                    p.Bench[s] != null ? p.Bench[s].DefinitionId : "(empty)",
                    new Dictionary<string, object> { ["unitId"] = p.Bench[s]?.Id }));
            }
            for (var r = 0; r < rules.PlayerRows; r++) {
                for (var c = 0; c < rules.BoardColumns; c++) {
                    var cell = new HexCoord(r, c);
                    var unit = p.Board[p.BoardIndex(cell)];
                    targets.Add(WorldTarget(cam, $"board:{cell}", "board", BoardSpace.Instance.CellToWorld(cell),
                        unit != null ? unit.DefinitionId : "(empty)", new Dictionary<string, object> { ["unitId"] = unit?.Id }));
                }
            }
            foreach (var (unitId, view) in Game.Roster.Views) {
                if (!view.gameObject.activeInHierarchy) continue;
                var t = WorldTarget(cam, $"unit:{unitId}", "unit", view.transform.position, view.Trait != null ? view.Trait.id : "?",
                    new Dictionary<string, object> { ["rank"] = view.Rank.ToString(), ["location"] = view.Location.ToString() });
                t.path = AutoTestUtil.PathOf(view.gameObject);
                targets.Add(t);
            }
            return targets;
        }

        static TargetInfo WorldTarget(Camera cam, string id, string kind, Vector3 world, string label, Dictionary<string, object> data) {
            var screen = cam.WorldToScreenPoint(world);
            var visible = screen.z > 0 && screen.x >= 0 && screen.y >= 0 && screen.x <= Screen.width && screen.y <= Screen.height;
            return new TargetInfo {
                id = id, kind = kind, label = label, center = new[] { screen.x, screen.y },
                rect = new[] { screen.x - 20, screen.y - 20, screen.x + 20, screen.y + 20 },
                visible = visible, interactable = visible, data = data,
            };
        }
    }
}
