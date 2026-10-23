using System.Linq;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    public class ItemCommandTests {
        [Test]
        public void AnItemComesOffBackToTheBenchWhole() {
            var s = TestData.Session();
            var db = s.Match.Db;
            var combined = db.CombinedItems[0];
            var p = s.LocalPlayer;
            Assert.IsTrue(s.Execute(new CheatUnitCommand { UnitId = db.Units.First(u => !u.Summoned).Id, Rank = HeroRank.B }).Ok);
            var unit = p.Units[0];
            s.Execute(new CheatItemCommand { ItemId = combined.Id });
            s.Execute(new CheatItemCommand { ItemId = combined.Ingredients[0] });
            Assert.IsTrue(s.Execute(new EquipItemCommand { UnitId = unit.Id, ItemId = combined.Id }).Ok);
            Assert.IsTrue(s.Execute(new EquipItemCommand { UnitId = unit.Id, ItemId = combined.Ingredients[0] }).Ok);
            Assert.AreEqual(2, unit.Items.Count);

            Assert.AreEqual(CommandError.InvalidItem, s.Execute(new UnequipItemCommand { UnitId = unit.Id, Index = 2 }).Error);
            Assert.IsTrue(s.Execute(new UnequipItemCommand { UnitId = unit.Id, Index = 0 }).Ok);
            CollectionAssert.AreEqual(new[] { combined.Ingredients[0] }, unit.Items);
            Assert.AreEqual(1, p.ItemCount(combined.Id), "the combined item stays whole");
        }

        [Test]
        public void BoardUnitsKeepTheirItemsDuringTheBattle() {
            var s = TestData.Session();
            var db = s.Match.Db;
            var p = s.LocalPlayer;
            s.Execute(new CheatUnitCommand { UnitId = db.Units.First(u => !u.Summoned).Id, Rank = HeroRank.B });
            var unit = p.Units[0];
            s.Execute(new MoveUnitCommand { UnitId = unit.Id, Target = UnitLocation.Board(1, 3) });
            s.Execute(new CheatItemCommand { ItemId = db.CombinedItems[0].Id });
            s.Execute(new EquipItemCommand { UnitId = unit.Id, ItemId = db.CombinedItems[0].Id });
            s.Execute(new ReadyCommand());
            for (var i = 0; i < 60 * s.Match.Rules.TicksPerSecond && s.State.Phase != MatchPhase.Battle; i++) s.RunTicks(1);
            Assert.AreEqual(MatchPhase.Battle, s.State.Phase);
            Assert.AreEqual(CommandError.WrongPhase, s.Execute(new UnequipItemCommand { UnitId = unit.Id, Index = 0 }).Error);
            Assert.AreEqual(1, unit.Items.Count);
        }

        [Test]
        public void TwoComponentsForgeOnTheBench() {
            var s = TestData.Session();
            var db = s.Match.Db;
            var p = s.LocalPlayer;
            var pair = db.CombinedItems.First(i => i.Ingredients[0] == i.Ingredients[1]);
            var mixed = db.CombinedItems.First(i => i.Ingredients[0] != i.Ingredients[1]);
            s.Execute(new CheatItemCommand { ItemId = pair.Ingredients[0] });
            Assert.AreEqual(CommandError.InvalidItem, s.Execute(new CombineItemsCommand { First = pair.Ingredients[0], Second = pair.Ingredients[0] }).Error,
                "one copy cannot pair with itself");
            s.Execute(new CheatItemCommand { ItemId = pair.Ingredients[0] });
            Assert.IsTrue(s.Execute(new CombineItemsCommand { First = pair.Ingredients[0], Second = pair.Ingredients[0] }).Ok);
            Assert.AreEqual(0, p.ItemCount(pair.Ingredients[0]));
            Assert.AreEqual(1, p.ItemCount(pair.Id));

            s.Execute(new CheatItemCommand { ItemId = mixed.Ingredients[0] });
            s.Execute(new CheatItemCommand { ItemId = mixed.Ingredients[1] });
            Assert.AreEqual(CommandError.InvalidItem, s.Execute(new CombineItemsCommand { First = mixed.Ingredients[0], Second = pair.Id }).Error);
            Assert.IsTrue(s.Execute(new CombineItemsCommand { First = mixed.Ingredients[1], Second = mixed.Ingredients[0] }).Ok);
            Assert.AreEqual(1, p.ItemCount(mixed.Id));
        }
    }
}
