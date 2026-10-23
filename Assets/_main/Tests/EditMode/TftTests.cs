using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    /// <summary>The TFT layer of multiplayer matches: PvP pairs, player damage, eliminations, income, pool, selling.</summary>
    public class TftTests {
        /// <summary>A match with no authored stage, so every round is PvP (round x-7 has no creeps to replay).</summary>
        static LocalGameSession Pvp(int players, ulong seed = 5, GameRules rules = null) {
            var s = new LocalGameSession(TestData.Database(rules, Array.Empty<StageDefinition>()), new MatchConfig {
                Seed = seed, PlayerCount = players, AllowCheats = true, SkipStarterPack = true, Abilities = TestData.Abilities()
            });
            s.Start();
            return s;
        }

        static void RunUntil(LocalGameSession s, Func<bool> done, int maxSeconds = 600) {
            var ticks = maxSeconds * s.Match.Rules.TicksPerSecond;
            for (var i = 0; i < ticks && !done(); i++) s.RunTicks(1);
            Assert.IsTrue(done(), $"stuck in {s.State.Phase}");
        }

        static void Give(LocalGameSession s, int player, string unit, int count = 1) {
            for (var i = 0; i < count; i++) Assert.IsTrue(s.Execute(new CheatUnitCommand { PlayerId = player, UnitId = unit }).Ok);
        }

        [Test]
        public void PlayersArePairedAndShareTheirBattle() {
            var s = Pvp(4);
            for (var p = 0; p < 4; p++) Give(s, p, TestData.Brawler);
            RunUntil(s, () => s.State.Phase == MatchPhase.Transition);
            Assert.AreEqual(RoundKind.PvP, s.State.RoundKind);
            Assert.AreEqual(2, s.State.BattleList.Count);
            foreach (var p in s.State.Players) {
                var opponent = s.State.Player(p.OpponentId);
                Assert.IsNotNull(opponent, $"{p} has no opponent");
                Assert.AreEqual(p.Id, opponent.OpponentId, "pairs are mutual");
                Assert.AreSame(s.State.BattleOf(p.Id), s.State.BattleOf(opponent.Id));
            }
        }

        [Test]
        public void TheOddPlayerOutFightsAGhostThatCostsItsOwnerNothing() {
            var s = Pvp(3);
            Give(s, 0, TestData.Knight, 2);
            Give(s, 1, TestData.Knight, 2);
            Give(s, 2, TestData.Knight, 2);
            RunUntil(s, () => s.State.Phase == MatchPhase.Transition);
            Assert.AreEqual(2, s.State.BattleList.Count);
            var ghostBattle = s.State.BattleList.Single(b => b.Setup.Away.Ghost);
            var alone = s.State.Player(ghostBattle.Setup.Home.PlayerId);
            Assert.IsTrue(alone.OpponentIsGhost);
            var copied = s.State.Player(ghostBattle.Setup.Away.PlayerId);
            Assert.AreNotEqual(alone.Id, copied.Id);
            Assert.AreNotSame(ghostBattle, s.State.BattleOf(copied.Id), "the copied player fights its own battle");
        }

        [Test]
        public void TheLoserTakesStageDamagePlusOnePerSurvivor() {
            var s = Pvp(2);
            Give(s, 0, TestData.Knight, 2);
            var damaged = new List<PlayerDamagedEvent>();
            s.EventRaised += e => {
                if (e is PlayerDamagedEvent d) damaged.Add(d);
            };
            RunUntil(s, () => s.State.Phase == MatchPhase.Summary);
            var loser = s.State.Player(1);
            var winner = s.State.Player(0);
            Assert.AreEqual(BattleOutcome.HomeWin, winner.LastOutcome);
            Assert.AreEqual(BattleOutcome.AwayWin, loser.LastOutcome);
            var expected = s.Match.Rules.DamageOfStage(0) + 1 * s.Match.Rules.DamagePerSurvivingUnit; // level 1: one unit fielded
            Assert.AreEqual(s.Match.Rules.StartingHealth - expected, loser.Health);
            Assert.AreEqual(s.Match.Rules.StartingHealth, winner.Health);
            Assert.AreEqual(1, damaged.Count);
            Assert.AreEqual((1, expected, 0), (damaged[0].PlayerId, damaged[0].Amount, damaged[0].SourceId));
            Assert.AreEqual(1, winner.Streak);
            Assert.AreEqual(-1, loser.Streak);
        }

        [Test]
        public void TheLastPlayerStandingWins() {
            var rules = TestData.Rules();
            rules.StartingHealth = 3;
            var s = Pvp(3, rules: rules);
            Give(s, 0, TestData.Knight, 3);
            var finished = new List<MatchFinishedEvent>();
            var eliminated = new List<PlayerEliminatedEvent>();
            s.EventRaised += e => {
                if (e is MatchFinishedEvent f) finished.Add(f);
                if (e is PlayerEliminatedEvent x) eliminated.Add(x);
            };
            RunUntil(s, () => s.State.Phase == MatchPhase.Finished);
            Assert.AreEqual(0, finished.Single().WinnerId);
            Assert.AreEqual(1, s.State.Player(0).Placement);
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, s.State.Players.Where(p => p.Id != 0).Select(p => p.Placement));
            Assert.AreEqual(2, eliminated.Count);
            Assert.IsTrue(s.State.Players.Where(p => p.Id != 0).All(p => p.Eliminated));
            Assert.AreEqual(CommandError.Eliminated, s.Execute(new BuyXpCommand { PlayerId = eliminated[0].PlayerId }).Error);
        }

        [Test]
        public void IncomeIsBasePlusInterestPlusStreak() {
            var s = Pvp(2);
            Give(s, 0, TestData.Knight, 2);
            s.Execute(new CheatGoldCommand { PlayerId = 1, Amount = 25 });
            var incomes = new List<IncomeEvent>();
            s.EventRaised += e => {
                if (e is IncomeEvent i) incomes.Add(i);
            };
            var goldBefore = s.State.Player(1).Gold;
            RunUntil(s, () => s.State.RoundNumber == 1 && s.State.Phase == MatchPhase.Preparation);
            var income = incomes.Single(i => i.PlayerId == 1);
            Assert.AreEqual(s.Match.Rules.IncomeOfRound(1), income.Base);
            Assert.AreEqual(2, income.Interest, "25 gold banked");
            Assert.AreEqual(0, income.Streak, "a one-round streak pays nothing");
            Assert.AreEqual(goldBefore + income.Total, s.State.Player(1).Gold);
            var winner = incomes.Single(i => i.PlayerId == 0);
            Assert.AreEqual(s.Match.Rules.PvpWinGold, s.State.Player(0).Gold - winner.Total, "+1 gold for the win");
        }

        [Test]
        public void SellingRefundsTheUnitAndReturnsItsItems() {
            var s = Pvp(2);
            Give(s, 0, TestData.Knight);
            s.Execute(new CheatItemCommand { PlayerId = 0, ItemId = TestData.Sword });
            var p = s.State.Player(0);
            var knight = p.Units.Single();
            Assert.IsTrue(s.Execute(new EquipItemCommand { PlayerId = 0, UnitId = knight.Id, ItemId = TestData.Sword }).Ok);
            var gold = p.Gold;
            Assert.IsTrue(s.Execute(new SellUnitCommand { PlayerId = 0, UnitId = knight.Id }).Ok);
            Assert.AreEqual(gold + 3, p.Gold, "a 1-star 3-gold unit sells for 3");
            Assert.AreEqual(0, p.Units.Count);
            Assert.AreEqual(1, p.ItemCount(TestData.Sword), "the item went back to the bench");

            s.Execute(new CheatUnitCommand { PlayerId = 0, UnitId = TestData.Knight, Rank = HeroRank.A });
            gold = p.Gold;
            Assert.IsTrue(s.Execute(new SellUnitCommand { PlayerId = 0, UnitId = p.Units.Single().Id }).Ok);
            Assert.AreEqual(gold + 3 * 3 - 1, p.Gold, "2-star: three copies minus one");
            Assert.AreEqual(CommandError.InvalidUnit, s.Execute(new SellUnitCommand { PlayerId = 0, UnitId = 999 }).Error);
        }

        [Test]
        public void TheSharedPoolRunsOutAndRefills() {
            var s = Pvp(2);
            var pool = s.State.SharedPool;
            Assert.IsNotNull(pool, "multiplayer matches share a finite pool");
            var copies = s.Match.Rules.PoolCopies[(int)Reputation.Unknown];
            var p = s.State.Player(0);
            int Brawlers() => pool.CountOf(TestData.Brawler);
            int Shown() => s.State.Players.Sum(x => x.Shop.Slots.Count(id => id == TestData.Brawler));
            Assert.AreEqual(copies, Brawlers() + Shown(), "shops hold the copies they show");

            s.Execute(new CheatSetShopCommand { PlayerId = 0, UnitIds = new[] { TestData.Brawler } });
            var slot = Array.IndexOf(p.Shop.Slots, TestData.Brawler);
            s.Execute(new CheatGoldCommand { PlayerId = 0, Amount = 10 });
            var before = Brawlers();
            Assert.IsTrue(s.Execute(new BuyUnitCommand { PlayerId = 0, ShopSlot = slot }).Ok);
            Assert.AreEqual(before, Brawlers(), "a bought copy stays out of the pool");
            Assert.IsTrue(s.Execute(new SellUnitCommand { PlayerId = 0, UnitId = p.Units.Last().Id }).Ok);
            Assert.AreEqual(before + 1, Brawlers(), "selling puts it back");
        }

        [Test]
        public void SinglePlayerKeepsTheCampaignRules() {
            var s = TestData.Session();
            Assert.IsNull(s.State.SharedPool);
            Assert.IsFalse(s.Match.IsMultiplayer);
            Assert.AreEqual(RoundKind.PvE, s.State.RoundKind);
        }
    }
}
