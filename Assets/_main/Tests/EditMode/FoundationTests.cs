using System.Collections.Generic;
using AutoChess.Core.Net;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    public class RandomTests {
        [Test]
        public void SameSeedGivesSameSequence() {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);
            for (var i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void DifferentSeedsDiffer() {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            var same = 0;
            for (var i = 0; i < 100; i++) {
                if (a.NextUInt() == b.NextUInt()) same++;
            }
            Assert.Less(same, 3);
        }

        [Test]
        public void RangeAndFloatStayInBounds() {
            var r = new DeterministicRandom(5);
            var seen = new HashSet<int>();
            for (var i = 0; i < 10000; i++) {
                var v = r.Range(3, 8);
                Assert.That(v, Is.InRange(3, 7));
                seen.Add(v);
                var f = r.NextFloat();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
            Assert.AreEqual(5, seen.Count, "every value of the range shows up");
            Assert.AreEqual(4, r.Range(4, 4), "empty range returns the minimum");
        }

        [Test]
        public void StateRoundTrips() {
            var r = new DeterministicRandom(9);
            r.NextUInt();
            var state = r.GetState();
            var expected = new[] { r.NextUInt(), r.NextUInt(), r.NextUInt() };
            r.SetState(state);
            Assert.AreEqual(expected, new[] { r.NextUInt(), r.NextUInt(), r.NextUInt() });
            Assert.AreEqual(r.GetState(), r.Clone().GetState());
        }
    }

    public class HexGridTests {
        static readonly HexGrid Grid = new(8, 8, 1.88f, 1.89f);

        [Test]
        public void NeighboursAreAtDistanceOne() {
            for (var i = 0; i < Grid.CellCount; i++) {
                var n = Grid.NeighborsOf(i);
                Assert.That(n.Length, Is.InRange(2, 6));
                foreach (var j in n) Assert.AreEqual(1, Grid.Distance(i, j), $"{Grid.CoordOf(i)} -> {Grid.CoordOf(j)}");
            }
        }

        [Test]
        public void DistanceIsSymmetricAndMatchesBreadthFirstSearch() {
            for (var a = 0; a < Grid.CellCount; a += 5) {
                var bfs = Bfs(a);
                for (var b = 0; b < Grid.CellCount; b++) {
                    Assert.AreEqual(Grid.Distance(b, a), Grid.Distance(a, b));
                    Assert.AreEqual(bfs[b], Grid.Distance(a, b), $"{Grid.CoordOf(a)} -> {Grid.CoordOf(b)}");
                }
            }
        }

        static int[] Bfs(int start) {
            var dist = new int[Grid.CellCount];
            for (var i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new Queue<int>();
            dist[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0) {
                var c = queue.Dequeue();
                foreach (var n in Grid.NeighborsOf(c)) {
                    if (dist[n] >= 0) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        [Test]
        public void CentersFollowTheOldMapLayout() {
            // Map.SpawnNodes: x = (col - 4) * w + w / 2 +- w / 4, z = (row - 4) * h * 3/4 + h / 2 - h / 8
            const float w = 1.88f, h = 1.89f;
            var c00 = Grid.Center(new HexCoord(0, 0));
            Assert.AreEqual(-4 * w + w / 2 + w / 4, c00.X, 1e-4);
            Assert.AreEqual(-4 * h * 3 / 4 + h / 2 - h / 8, c00.Y, 1e-4);
            var c13 = Grid.Center(new HexCoord(1, 3));
            Assert.AreEqual(-1 * w + w / 2 - w / 4, c13.X, 1e-4);
            Assert.AreEqual(new HexCoord(5, 6), Grid.Nearest(Grid.Center(new HexCoord(5, 6)) + new Vec2(0.3f, -0.2f)));
        }

        [Test]
        public void MirrorIsAHalfTurn() {
            var a = new HexCoord(1, 2);
            var b = new HexCoord(2, 5);
            var ma = a.Mirror(8, 8);
            Assert.AreEqual(new HexCoord(6, 5), ma);
            Assert.AreEqual(HexCoord.Distance(a, b), HexCoord.Distance(ma, b.Mirror(8, 8)));
        }

        [Test]
        public void PathFinderFindsShortestPathsAndAvoidsBlockedCells() {
            var pf = new PathFinder(Grid);
            var path = new List<int>();
            var from = Grid.IndexOf(new HexCoord(0, 0));
            var to = Grid.IndexOf(new HexCoord(7, 7));
            Assert.IsTrue(pf.FindPath(from, to, path));
            Assert.AreEqual(Grid.Distance(from, to) + 1, path.Count);
            for (var i = 1; i < path.Count; i++) Assert.AreEqual(1, Grid.Distance(path[i - 1], path[i]));

            // Wall across row 3 except column 7.
            var wall = new HashSet<int>();
            for (var c = 0; c < 7; c++) wall.Add(Grid.IndexOf(new HexCoord(3, c)));
            Assert.IsTrue(pf.FindPath(from, Grid.IndexOf(new HexCoord(6, 0)), path, wall.Contains));
            foreach (var cell in path) Assert.IsFalse(wall.Contains(cell));
            Assert.That(path, Does.Contain(Grid.IndexOf(new HexCoord(3, 7))));

            wall.Add(Grid.IndexOf(new HexCoord(3, 7)));
            Assert.IsFalse(pf.FindPath(from, Grid.IndexOf(new HexCoord(6, 0)), path, wall.Contains));
            Assert.AreEqual(0, path.Count);
        }
    }

    public class SerializationTests {
        static IEnumerable<GameCommand> AllCommands() {
            yield return new ChooseStarterCommand { PlayerId = 1, OfferIndex = 2 };
            yield return new RerollStarterCommand { PlayerId = 0, OfferIndex = 1 };
            yield return new BuyUnitCommand { PlayerId = 3, ShopSlot = 4 };
            yield return new RefreshShopCommand { PlayerId = 0 };
            yield return new SetShopLockCommand { PlayerId = 0, Locked = true };
            yield return new BuyXpCommand { PlayerId = 0 };
            yield return new MoveUnitCommand { PlayerId = 0, UnitId = 77, Target = UnitLocation.Board(2, 5) };
            yield return new MoveUnitCommand { PlayerId = 0, UnitId = 78, Target = UnitLocation.Bench(8) };
            yield return new EquipItemCommand { PlayerId = 0, UnitId = 5, ItemId = "armor+crit" };
            yield return new UnequipItemCommand { PlayerId = 2, UnitId = 5, Index = 1 };
            yield return new CombineItemsCommand { PlayerId = 0, First = "_armor", Second = "_crit" };
            yield return new ReadyCommand { PlayerId = 7 };
            yield return new CheatGoldCommand { PlayerId = 0, Amount = -50 };
            yield return new CheatXpCommand { PlayerId = 0, Amount = 12 };
            yield return new CheatItemCommand { PlayerId = 0, ItemId = "_crit", Count = 3 };
            yield return new CheatUnitCommand { PlayerId = 0, UnitId = "Zed", Rank = HeroRank.S };
            yield return new CheatSkipPhaseCommand { PlayerId = 0 };
            yield return new CheatSetShopCommand { PlayerId = 0, UnitIds = new[] { "Ashe", null, "Zed" } };
        }

        [Test]
        public void EveryCommandRoundTrips() {
            foreach (var c in AllCommands()) {
                var decoded = CommandCodec.Decode(CommandCodec.Encode(c));
                Assert.AreEqual(c.GetType(), decoded.GetType());
                Assert.AreEqual(c.PlayerId, decoded.PlayerId);
                Assert.AreEqual(MiniJson.Serialize(Fields(c)), MiniJson.Serialize(Fields(decoded)), c.ToString());
            }
        }

        static Dictionary<string, object> Fields(object o) {
            var d = new Dictionary<string, object>();
            foreach (var f in o.GetType().GetFields()) {
                var v = f.GetValue(o);
                d[f.Name] = v is UnitLocation l ? l.ToString() : v;
            }
            return d;
        }

        [Test]
        public void VarIntsAndStringsRoundTrip() {
            var w = new NetWriter(4);
            var ints = new[] { 0, 1, -1, 127, 128, -129, int.MaxValue, int.MinValue };
            foreach (var i in ints) w.WriteVarInt(i);
            w.WriteString("Thiên Giáng");
            w.WriteString(null);
            w.WriteULong(ulong.MaxValue - 5);
            var r = new NetReader(w.ToArray());
            foreach (var i in ints) Assert.AreEqual(i, r.ReadVarInt());
            Assert.AreEqual("Thiên Giáng", r.ReadString());
            Assert.IsNull(r.ReadString());
            Assert.AreEqual(ulong.MaxValue - 5, r.ReadULong());
            Assert.IsTrue(r.AtEnd);
        }

        [Test]
        public void GameDataJsonRoundTrips() {
            var db = TestData.Database();
            var content = new GameDataJson.Content();
            content.Units.AddRange(db.Units);
            content.Items.AddRange(db.Components);
            content.Items.AddRange(db.CombinedItems);
            content.Traits.AddRange(db.Traits);
            content.Stages.AddRange(db.Stages);
            var json = GameDataJson.Write(content);
            var again = GameDataJson.Write(GameDataJson.Read(json));
            Assert.AreEqual(json, again);
            var loaded = GameDataJson.Load(json);
            Assert.AreEqual(db.Units.Count, loaded.Units.Count);
            Assert.AreEqual(TestData.Blade, loaded.Combine(TestData.Sword, TestData.Sword).Id);
            Assert.AreEqual(0.48f, (float)(double)MiniJson.Parse("0.47999998927116394"));
        }
    }
}
