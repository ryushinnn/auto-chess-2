using System.Linq;
using AutoChess.Core.Combat;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    public class EconomyTests {
        [Test]
        public void XpLevelsUpAndCarriesTheRest() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            Assert.IsTrue(Economy.GainXp(ctx, p, 5)); // 2 to reach 2, 2 to reach 3, then 1 of 6
            Assert.AreEqual(3, p.Level);
            Assert.AreEqual(1, p.Xp);
            Assert.AreEqual(3, Economy.MaxBoardUnits(ctx.Rules, p));
        }

        [Test]
        public void MaxLevelStopsXp() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            Economy.GainXp(ctx, p, 10000);
            Assert.AreEqual(ctx.Rules.MaxLevel, p.Level);
            Assert.IsFalse(Economy.GainXp(ctx, p, 1));
            Assert.AreEqual(CommandError.MaxLevel, Economy.BuyXp(ctx, p).Error);
        }

        [Test]
        public void BuyingXpCostsGold() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx, gold: 5);
            Assert.IsTrue(Economy.BuyXp(ctx, p).Ok);
            Assert.AreEqual(1, p.Gold);
            Assert.AreEqual(CommandError.NotEnoughGold, Economy.BuyXp(ctx, p).Error);
        }
    }

    public class ShopTests {
        [Test]
        public void LevelOneRollsOnlyCheapUnits() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            for (var i = 0; i < 50; i++) {
                Shop.Roll(ctx, p);
                foreach (var id in p.Shop.Slots) Assert.AreEqual(Reputation.Unknown, ctx.Db.Unit(id).Reputation);
            }
        }

        [Test]
        public void InsuranceGuaranteesALegendary() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            p.Level = 5; // 2% legendary odds -> insurance +2 per non legendary slot
            var legendary = 0;
            for (var roll = 0; roll < 20; roll++) {
                Shop.Roll(ctx, p);
                legendary += p.Shop.Slots.Count(id => ctx.Db.Unit(id).Reputation == Reputation.Legendary);
            }
            Assert.GreaterOrEqual(legendary, 1, "100 slots at 2% always include the pity legendary");
        }

        [Test]
        public void BuyingSpendsGoldAndEmptiesTheSlot() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx, gold: 3);
            p.Shop.Slots[0] = TestData.Knight; // costs 3
            p.Shop.Slots[1] = TestData.Brawler;
            Assert.IsTrue(Shop.Buy(ctx, p, 0, false).Ok);
            Assert.AreEqual(0, p.Gold);
            Assert.IsNull(p.Shop.Slots[0]);
            Assert.AreEqual(CommandError.ShopSlotEmpty, Shop.Buy(ctx, p, 0, false).Error);
            Assert.AreEqual(CommandError.NotEnoughGold, Shop.Buy(ctx, p, 1, false).Error);
            Assert.AreEqual(CommandError.InvalidSlot, Shop.Buy(ctx, p, 9, false).Error);
        }

        [Test]
        public void RefreshCostsAndUnlocks() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx, gold: 2);
            Shop.SetLocked(ctx, p, true);
            Assert.IsTrue(Shop.Refresh(ctx, p).Ok);
            Assert.AreEqual(0, p.Gold);
            Assert.IsFalse(p.Shop.Locked);
            Assert.AreEqual(CommandError.NotEnoughGold, Shop.Refresh(ctx, p).Error);
        }

        [Test]
        public void LockedShopSkipsTheAutoRefresh() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            Shop.Roll(ctx, p);
            var before = p.Shop.Slots.ToArray();
            Shop.SetLocked(ctx, p, true);
            Shop.AutoRefresh(ctx, p);
            Assert.AreEqual(before, p.Shop.Slots);
        }

        [Test]
        public void BuyingWithAFullBenchStillWorksWhenItMerges() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            for (var i = 0; i < ctx.Rules.BenchSize; i++) Roster.Create(ctx, p, i < 2 ? TestData.Brawler : TestData.Archer, HeroRank.B, i);
            p.Shop.Slots[0] = TestData.Brawler;
            p.Shop.Slots[1] = TestData.Knight;
            p.Shop.Slots[2] = TestData.Knight;
            Assert.IsTrue(Shop.Buy(ctx, p, 0, false).Ok, "third copy merges");
            Assert.IsTrue(Shop.Buy(ctx, p, 1, false).Ok, "the merge freed a slot");
            Assert.AreEqual(CommandError.BenchFull, Shop.Buy(ctx, p, 2, false).Error);
        }
    }

    public class RosterTests {
        [Test]
        public void ThreeCopiesMergeAndNineMakeAnS() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            for (var i = 0; i < 3; i++) Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Assert.AreEqual(1, p.Units.Count);
            Assert.AreEqual(HeroRank.A, p.Units[0].Rank);
            for (var i = 0; i < 6; i++) Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Assert.AreEqual(1, p.Units.Count);
            Assert.AreEqual(HeroRank.S, p.Units[0].Rank);
            Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Assert.AreEqual(2, p.Units.Count, "an S never merges again");
        }

        [Test]
        public void MergeKeepsTheBoardCopyAndCollectsItems() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            Economy.GainXp(ctx, p, 2);
            var a = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            var b = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Assert.IsTrue(Roster.Move(ctx, p, b, UnitLocation.Board(1, 1)).Ok);
            Items.TryEquip(ctx, a, TestData.Sword);
            Items.TryEquip(ctx, a, TestData.Shield);
            var kept = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Assert.AreSame(b, kept);
            Assert.AreEqual(HeroRank.A, b.Rank);
            Assert.AreEqual(UnitLocation.Board(1, 1), b.Location);
            Assert.AreEqual(new[] { TestData.Guard }, b.Items.ToArray(), "sword + shield moved over and combined");
        }

        [Test]
        public void LockedBoardMergesLater() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            Economy.GainXp(ctx, p, 6);
            var a = Roster.AddUnit(ctx, p, TestData.Archer, false);
            var b = Roster.AddUnit(ctx, p, TestData.Archer, false);
            Roster.Move(ctx, p, a, UnitLocation.Board(0, 0));
            Roster.Move(ctx, p, b, UnitLocation.Board(0, 1));
            Roster.AddUnit(ctx, p, TestData.Archer, true);
            Assert.AreEqual(3, p.Units.Count, "board copies are busy fighting");
            Roster.ResolveMerges(ctx, p);
            Assert.AreEqual(1, p.Units.Count);
            Assert.AreEqual(UnitLocation.Board(0, 0), p.Units[0].Location);
        }

        [Test]
        public void MovesFollowTheBoardLimit() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            var a = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            var b = Roster.AddUnit(ctx, p, TestData.Archer, false);
            Assert.IsTrue(Roster.Move(ctx, p, a, UnitLocation.Board(0, 0)).Ok);
            Assert.AreEqual(CommandError.BoardFull, Roster.Move(ctx, p, b, UnitLocation.Board(0, 1)).Error);
            Assert.IsTrue(Roster.Move(ctx, p, b, UnitLocation.Board(0, 0)).Ok, "swapping keeps the count");
            Assert.AreEqual(UnitLocation.Board(0, 0), b.Location);
            Assert.AreEqual(UnitLocation.Bench(1), a.Location);
            Assert.IsTrue(Roster.Move(ctx, p, b, UnitLocation.Board(3, 7)).Ok, "board to board is always fine");
            Assert.AreEqual(CommandError.InvalidLocation, Roster.Move(ctx, p, b, UnitLocation.Board(4, 0)).Error, "enemy half");
            Assert.AreEqual(CommandError.InvalidLocation, Roster.Move(ctx, p, b, UnitLocation.Bench(9)).Error);
            Assert.AreSame(b, p.Board[p.BoardIndex(new HexCoord(3, 7))]);
            Assert.IsNull(p.Board[p.BoardIndex(new HexCoord(0, 0))]);
        }

        [Test]
        public void AutoFillUsesTheLowestFreeCells() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            Economy.GainXp(ctx, p, 6); // level 3
            Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Roster.AddUnit(ctx, p, TestData.Archer, false);
            Roster.AddUnit(ctx, p, TestData.Knight, false);
            Roster.AddUnit(ctx, p, TestData.Dragon, false);
            Roster.AutoFillBoard(ctx, p);
            Assert.AreEqual(3, p.BoardCount);
            Assert.AreEqual(UnitLocation.Board(0, 0), p.Units[0].Location);
            Assert.AreEqual(UnitLocation.Board(0, 2), p.Units[2].Location);
            Assert.IsTrue(p.Units[3].Location.IsBench);
        }
    }

    public class ItemTests {
        [Test]
        public void ComponentsCombineAndCombinedItemsComeFirst() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            var u = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Assert.IsTrue(Items.TryEquip(ctx, u, TestData.Sword));
            Assert.IsTrue(Items.TryEquip(ctx, u, TestData.Sword));
            Assert.AreEqual(new[] { TestData.Blade }, u.Items.ToArray());
            Assert.IsTrue(Items.TryEquip(ctx, u, TestData.Shield));
            Assert.IsTrue(Items.TryEquip(ctx, u, TestData.Wall));
            Assert.AreEqual(new[] { TestData.Blade, TestData.Wall, TestData.Shield }, u.Items.ToArray());
            Assert.IsTrue(Items.CanEquip(ctx, u, TestData.Sword), "full, but the component combines with the last one");
            Assert.IsFalse(Items.CanEquip(ctx, u, TestData.Blade));
            Assert.IsTrue(Items.TryEquip(ctx, u, TestData.Sword));
            Assert.AreEqual(new[] { TestData.Blade, TestData.Wall, TestData.Guard }, u.Items.ToArray());
            Assert.IsFalse(Items.TryEquip(ctx, u, TestData.Sword));
        }

        [Test]
        public void EquippingFromTheBenchConsumesTheItem() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            var u = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Items.AddToBench(ctx, p, TestData.Sword, 2);
            Assert.IsTrue(Items.EquipFromBench(ctx, p, TestData.Sword, u, false).Ok);
            Assert.AreEqual(1, p.ItemCount(TestData.Sword));
            Assert.IsTrue(Items.EquipFromBench(ctx, p, TestData.Sword, u, false).Ok);
            Assert.AreEqual(0, p.Items.Count);
            Assert.AreEqual(CommandError.InvalidItem, Items.EquipFromBench(ctx, p, TestData.Sword, u, false).Error);
        }

        [Test]
        public void BoardUnitsOnlyTakeItemsDuringPreparation() {
            var ctx = TestData.Context();
            var p = TestData.Player(ctx);
            var u = Roster.AddUnit(ctx, p, TestData.Brawler, false);
            Roster.Move(ctx, p, u, UnitLocation.Board(0, 0));
            Items.AddToBench(ctx, p, TestData.Sword);
            Assert.AreEqual(CommandError.WrongPhase, Items.EquipFromBench(ctx, p, TestData.Sword, u, true).Error);
            Assert.IsTrue(Items.EquipFromBench(ctx, p, TestData.Sword, u, false).Ok);
        }
    }

    public class TraitAndStatTests {
        [Test]
        public void TraitsCountDistinctUnits() {
            var db = TestData.Database();
            var list = Traits.Compute(db, new[] { TestData.Archer, TestData.Archer, TestData.Brawler, TestData.Knight });
            var mortal = list.First(t => t.Id.Equals(TraitId.Of(Realm.Mortal)));
            Assert.AreEqual(2, mortal.Count);
            Assert.AreEqual(0, mortal.Tier);
            var marksman = list.First(t => t.Id.Equals(TraitId.Of(Role.Marksman)));
            Assert.AreEqual(1, marksman.Count);
            Assert.AreEqual(-1, marksman.Tier);
            Assert.AreEqual(TraitId.Of(Realm.Mortal), list[0].Id, "active traits are listed first");
        }

        [Test]
        public void FlatModifiersApplyBeforeCompoundingPercentages() {
            var rules = TestData.Rules();
            var mods = new System.Collections.Generic.List<StatModifier> {
                new(StatKind.PhysicalDamage, 0.5f, ModifierType.Percent),
                new(StatKind.PhysicalDamage, 20, ModifierType.Flat),
                new(StatKind.PhysicalDamage, 0.1f, ModifierType.Percent),
            };
            Assert.AreEqual((100 + 20) * 1.5f * 1.1f, UnitStats.Apply(rules, StatKind.PhysicalDamage, 100, mods), 1e-3);
        }

        [Test]
        public void StatsAreClamped() {
            var rules = TestData.Rules();
            var minus = new System.Collections.Generic.List<StatModifier> { new(StatKind.Armor, -500, ModifierType.Flat) };
            Assert.AreEqual(rules.MinArmorAndResistance, UnitStats.Apply(rules, StatKind.Armor, 20, minus));
            var crit = new System.Collections.Generic.List<StatModifier> { new(StatKind.CriticalChance, 5, ModifierType.Flat) };
            Assert.AreEqual(rules.MaxCriticalChance, UnitStats.Apply(rules, StatKind.CriticalChance, 0.15f, crit));
            var pen = new System.Collections.Generic.List<StatModifier> { new(StatKind.PhysicalPenetration, 3, ModifierType.Flat) };
            Assert.AreEqual(rules.MaxPenetration, UnitStats.Apply(rules, StatKind.PhysicalPenetration, 0, pen));
        }

        [Test]
        public void OwnedUnitStatsIncludeRankAndItems() {
            var db = TestData.Database();
            var stats = new StatBlock();
            UnitStats.Compute(db, db.Unit(TestData.Brawler), HeroRank.A, new[] { TestData.Sword, TestData.Blade }, stats);
            Assert.AreEqual(1000 * 1.8f, stats[StatKind.MaxHp], 1e-2);
            Assert.AreEqual((100 + 10) * 1.8f * 1.3f, stats[StatKind.PhysicalDamage], 1e-2);
            Assert.AreEqual(db.Rules.BaseCriticalChance, stats[StatKind.CriticalChance]);
        }
    }
}
