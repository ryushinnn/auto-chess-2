using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoChess.Core.Combat;
using AutoChess.Core.Net;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    public class MatchFlowTests {
        static void RunUntil(LocalGameSession s, MatchPhase phase, int maxSeconds = 300) {
            var ticks = maxSeconds * s.Match.Rules.TicksPerSecond;
            for (var i = 0; i < ticks && s.State.Phase != phase; i++) s.RunTicks(1);
            Assert.AreEqual(phase, s.State.Phase, $"stuck in {s.State.Phase}");
        }

        [Test]
        public void StarterPackStartsTheGame() {
            var s = TestData.Session(skipStarter: false);
            Assert.AreEqual(MatchPhase.Setup, s.State.Phase);
            var offer = s.LocalPlayer.StarterOffers[1];
            Assert.IsNotNull(offer.UnitId);
            Assert.IsTrue(s.Execute(new RerollStarterCommand { OfferIndex = 1 }).Ok);
            Assert.AreEqual(CommandError.WrongPhase, s.Execute(new BuyXpCommand()).Error);
            offer = s.LocalPlayer.StarterOffers[1];
            var (unit, item) = (offer.UnitId, offer.ItemId);
            Assert.IsTrue(s.Execute(new ChooseStarterCommand { OfferIndex = 1 }).Ok);
            Assert.AreEqual(MatchPhase.Preparation, s.State.Phase);
            Assert.AreEqual(unit, s.LocalPlayer.Units.Single().DefinitionId);
            Assert.AreEqual(1, s.LocalPlayer.ItemCount(item));
            Assert.IsTrue(s.LocalPlayer.Shop.Slots.All(x => x != null), "the first preparation rolls the shop");
            Assert.AreEqual(CommandError.WrongPhase, s.Execute(new ChooseStarterCommand { OfferIndex = 0 }).Error);
        }

        [Test]
        public void ARoundGoesThroughEveryPhaseAndPaysRewards() {
            var s = TestData.Session();
            var p = s.LocalPlayer;
            var seen = new List<MatchPhase>();
            s.EventRaised += e => {
                if (e is PhaseChangedEvent pc) seen.Add(pc.Phase);
            };
            s.Execute(new CheatUnitCommand { UnitId = TestData.Knight });
            Assert.IsTrue(s.Execute(new ReadyCommand()).Ok);
            RunUntil(s, MatchPhase.Transition);
            Assert.AreEqual(1, p.BoardCount, "the bench unit was auto-deployed");
            Assert.IsNotNull(s.State.BattleOf(p.Id));
            RunUntil(s, MatchPhase.Summary);
            Assert.AreEqual(BattleOutcome.HomeWin, p.LastOutcome);
            RunUntil(s, MatchPhase.Preparation);
            Assert.AreEqual(5, p.Gold, "round coins");
            Assert.AreEqual(1, p.Items.Sum(x => x.Count), "win reward item");
            Assert.AreEqual(2, p.Level, "+2 xp per round levels 1 -> 2");
            Assert.AreEqual(1, s.State.RoundIndex);
            CollectionAssert.AreEqual(new[] { MatchPhase.Transition, MatchPhase.Battle, MatchPhase.Summary, MatchPhase.Preparation }, seen);
        }

        [Test]
        public void TheMatchFinishesAfterTheLastRound() {
            var s = TestData.Session();
            s.Execute(new ReadyCommand());
            RunUntil(s, MatchPhase.Transition);
            RunUntil(s, MatchPhase.Preparation);
            s.Execute(new ReadyCommand());
            RunUntil(s, MatchPhase.Transition);
            RunUntil(s, MatchPhase.Finished);
            Assert.AreEqual(2, s.LocalPlayer.Losses, "an empty board loses at once");
            Assert.AreEqual(CommandError.WrongPhase, s.Execute(new BuyXpCommand()).Error);
        }

        [Test]
        public void BattleTimeoutIsADraw() {
            var rules = TestData.Rules();
            rules.BattleSeconds = 1;
            var s = TestData.Session(TestData.Database(rules));
            s.Execute(new CheatUnitCommand { UnitId = TestData.Dummy });
            s.Execute(new ReadyCommand());
            RunUntil(s, MatchPhase.Summary);
            Assert.AreEqual(BattleOutcome.Draw, s.LocalPlayer.LastOutcome, "two dummies never hurt each other");
        }

        [Test]
        public void CommandsRespectThePhase() {
            var s = TestData.Session();
            s.Execute(new CheatUnitCommand { UnitId = TestData.Brawler });
            var unit = s.LocalPlayer.Units[0];
            s.Execute(new ReadyCommand());
            RunUntil(s, MatchPhase.Battle);
            Assert.AreEqual(CommandError.WrongPhase, s.Execute(new MoveUnitCommand { UnitId = unit.Id, Target = UnitLocation.Bench(3) }).Error);
            s.Execute(new CheatGoldCommand { Amount = 10 });
            s.Execute(new CheatSetShopCommand { UnitIds = new[] { TestData.Archer } });
            Assert.IsTrue(s.Execute(new BuyUnitCommand { ShopSlot = 0 }).Ok, "buying is allowed during battles");
            Assert.AreEqual(CommandError.InvalidPlayer, s.Match.Submit(new BuyXpCommand { PlayerId = 5 }).Error);
        }

        [Test]
        public void CheatsAreOffByDefault() {
            var s = TestData.Session(cheats: false);
            Assert.AreEqual(CommandError.CheatsDisabled, s.Execute(new CheatGoldCommand { Amount = 99 }).Error);
            Assert.AreEqual(0, s.LocalPlayer.Gold);
        }

        [Test]
        public void SeveralPlayersFightTheirOwnBattles() {
            var db = TestData.Database();
            var match = new MatchSimulation(db, new MatchConfig { Seed = 4, PlayerCount = 3, SkipStarterPack = true, AllowCheats = true, Abilities = TestData.Abilities() });
            match.Start();
            for (var i = 0; i < 3; i++) {
                match.Submit(new CheatUnitCommand { PlayerId = i, UnitId = TestData.Knight });
                Assert.AreEqual(CommandResult.Success.Error, match.Submit(new ReadyCommand { PlayerId = i }).Error);
            }
            for (var t = 0; t < 30 * 120 && match.Phase != MatchPhase.Summary; t++) match.Tick();
            Assert.AreEqual(MatchPhase.Summary, match.Phase);
            Assert.IsTrue(match.State.Players.All(p => p.LastOutcome == BattleOutcome.HomeWin));
            Assert.AreNotEqual(match.State.Players[0].Rng.GetState(), match.State.Players[1].Rng.GetState(), "each player has its own luck");
        }
    }

    public class DeterminismTests {
        static ulong Script(ulong seed, out MatchRecording recording) {
            var s = new LocalGameSession(TestData.Database(), new MatchConfig {
                Seed = seed, AllowCheats = true, Abilities = TestData.Abilities()
            });
            s.Start();
            s.Execute(new RerollStarterCommand { OfferIndex = 0 });
            s.Execute(new ChooseStarterCommand { OfferIndex = 0 });
            s.Execute(new CheatGoldCommand { Amount = 30 });
            s.Execute(new BuyXpCommand());
            s.Execute(new RefreshShopCommand());
            s.Execute(new BuyUnitCommand { ShopSlot = 0 });
            s.Execute(new BuyUnitCommand { ShopSlot = 1 });
            var p = s.LocalPlayer;
            s.Execute(new MoveUnitCommand { UnitId = p.Units[0].Id, Target = UnitLocation.Board(1, 3) });
            s.Execute(new EquipItemCommand { UnitId = p.Units[0].Id, ItemId = p.Items[0].ItemId });
            s.Execute(new ReadyCommand());
            for (var i = 0; i < 30 * 40; i++) s.RunTicks(1);
            recording = s.Recorder.Recording;
            return StateHasher.Hash(s.State);
        }

        [Test]
        public void SameSeedAndCommandsGiveTheSameMatch() {
            Assert.AreEqual(Script(77, out _), Script(77, out _));
            Assert.AreNotEqual(Script(77, out _), Script(78, out _));
        }

        [Test]
        public void ARecordingReplaysBitForBit() {
            var hash = Script(91, out var recording);
            var replayed = MatchRecorder.Replay(TestData.Database(), recording, TestData.Abilities());
            Assert.AreEqual(hash, StateHasher.Hash(replayed.State));
        }
    }

    /// <summary>Runs every real hero through the simulation. Needs GameData/game_data.json (exported by the editor).</summary>
    public class RealDataTests {
        static GameDatabase LoadOrIgnore() {
            var path = TestData.RealDataPath();
            if (path == null) Assert.Ignore("GameData/game_data.json not found (menu AutoChess/Export Game Data)");
            return GameDataJson.Load(File.ReadAllText(path));
        }

        [Test]
        public void EveryHeroHasAbilitiesAndValidParameters() {
            var db = LoadOrIgnore();
            foreach (var u in db.Units) {
                if (u.Summoned) continue;
                Assert.IsTrue(AbilityRegistry.Default.Has(u.Id), $"{u.Id} has no abilities registered");
                Assert.DoesNotThrow(() => {
                    var attack = AbilityRegistry.Default.CreateAttack(u);
                    AbilityRegistry.Default.CreateSkill(u, attack);
                }, u.Id);
            }
        }

        [Test]
        public void EveryHeroSurvivesAFullBattle() {
            var db = LoadOrIgnore();
            var heroes = db.Units.Where(u => !u.Summoned).ToList();
            var castBy = new HashSet<string>();
            for (var i = 0; i < heroes.Count; i++) {
                foreach (var rank in new[] { HeroRank.B, HeroRank.S }) {
                    var setup = new BattleSetup { Seed = (ulong)(i * 31 + (int)rank) };
                    setup.Home.ApplyTraits = true;
                    setup.Home.Units.Add(new CombatantSetup { UnitId = heroes[i].Id, Rank = rank, Cell = new HexCoord(1, 3) });
                    setup.Home.Units.Add(new CombatantSetup { UnitId = heroes[(i + 3) % heroes.Count].Id, Rank = rank, Cell = new HexCoord(0, 4) });
                    setup.Away.Units.Add(new CombatantSetup { UnitId = heroes[(i + 1) % heroes.Count].Id, Rank = rank, Cell = new HexCoord(6, 3) });
                    setup.Away.Units.Add(new CombatantSetup { UnitId = heroes[(i + 5) % heroes.Count].Id, Rank = rank, Cell = new HexCoord(7, 2) });
                    var events = new EventBuffer();
                    var battle = new BattleSimulation(i, db, setup, events);
                    Assert.DoesNotThrow(() => {
                        for (var t = 0; t < 30 * 60 && !battle.Finished; t++) battle.Step();
                    }, $"{heroes[i].Id} ({rank})");
                    Assert.That(events.Pending.OfType<UnitDamagedEvent>().Count(), Is.GreaterThan(10), $"{heroes[i].Id} ({rank}): the battle was fought");
                    foreach (var cast in events.Pending.OfType<UnitSkillStartedEvent>()) castBy.Add(battle.FindUnit(cast.UnitId).Def.Id);
                    foreach (var u in battle.Units) {
                        Assert.That(u.Hp, Is.LessThanOrEqualTo(u.MaxHp + 1e-3f), u.ToString());
                        Assert.IsFalse(float.IsNaN(u.Hp) || float.IsNaN(u.Position.X), u.ToString());
                    }
                }
            }
            // Heroes whose data grants no energy at all (Aatrox Dark / Light today) can never cast.
            var canCast = heroes.Where(h => h.EnergyRegenPerAttack > 0 || h.EnergyRegenPerHit > 0).Select(h => h.Id);
            CollectionAssert.AreEquivalent(canCast, castBy, "every hero that gains energy casts its skill at least once");
        }

        [Test]
        public void EveryStageRoundCanBePlayed() {
            var db = LoadOrIgnore();
            Assert.Greater(db.Stages.Count, 0);
            var s = new LocalGameSession(db, new MatchConfig { Seed = 5, AllowCheats = true });
            s.Start();
            s.Execute(new ChooseStarterCommand { OfferIndex = 0 });
            var rounds = db.Stages.Sum(st => st.Rounds.Length);
            for (var r = 0; r < rounds; r++) {
                Assert.AreEqual(MatchPhase.Preparation, s.State.Phase, $"round {r}");
                s.Execute(new ReadyCommand());
                for (var t = 0; t < 30 * 10 && s.State.Phase == MatchPhase.Preparation; t++) s.RunTicks(1);
                for (var t = 0; t < 30 * 90 && s.State.Phase != MatchPhase.Preparation && s.State.Phase != MatchPhase.Finished; t++) s.RunTicks(1);
            }
            Assert.AreEqual(MatchPhase.Finished, s.State.Phase);
        }

        [Test]
        public void EveryPairOfComponentsCombines() {
            var db = LoadOrIgnore();
            foreach (var a in db.Components) {
                foreach (var b in db.Components) Assert.IsNotNull(db.Combine(a.Id, b.Id), $"{a.Id} + {b.Id}");
            }
        }
    }
}
