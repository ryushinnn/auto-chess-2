using System.IO;
using System.Linq;
using AutoChess.Core.Combat;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    /// <summary>Hero mechanics on the real data (needs GameData/game_data.json).</summary>
    public class HeroMechanicsTests {
        static GameDatabase LoadOrIgnore() {
            var path = TestData.RealDataPath();
            if (path == null) Assert.Ignore("GameData/game_data.json not found (menu AutoChess/Export Game Data)");
            return GameDataJson.Load(File.ReadAllText(path));
        }

        static BattleSimulation Battle(GameDatabase db, EventBuffer events, params (string id, TeamSide side, HexCoord cell)[] units) {
            var setup = new BattleSetup { Seed = 7 };
            foreach (var (id, side, cell) in units) {
                (side == TeamSide.Home ? setup.Home : setup.Away).Units.Add(new CombatantSetup { UnitId = id, Rank = HeroRank.B, Cell = cell });
            }
            return new BattleSimulation(1, db, setup, events);
        }

        [Test]
        public void LeeSinJumpsToHisTargetInsteadOfWalking() {
            var db = LoadOrIgnore();
            var events = new EventBuffer();
            var battle = Battle(db, events, (HeroIds.LeeSin, TeamSide.Home, new HexCoord(0, 3)), (HeroIds.Malphite, TeamSide.Away, new HexCoord(7, 4)));
            var lee = battle.Units.First(u => u.Def.Id == HeroIds.LeeSin);
            var malphite = battle.Units.First(u => u.Def.Id == HeroIds.Malphite);
            // up to his first swing (later kicks send Malphite away again)
            for (var t = 0; t < 3 * db.Rules.TicksPerSecond; t++) {
                battle.Step();
                if (events.Pending.OfType<UnitAttackStartedEvent>().Any(e => e.UnitId == lee.Id)) break;
            }
            Assert.IsTrue(events.Pending.OfType<UnitAttackStartedEvent>().Any(e => e.UnitId == lee.Id), "he reaches his target");
            Assert.IsFalse(events.Pending.OfType<UnitMoveStartedEvent>().Any(e => e.UnitId == lee.Id), "Lee Sin never walks");
            Assert.LessOrEqual(battle.Grid.Distance(battle.CellOf(lee), battle.CellOf(malphite)), 1, "he landed next to his target");
        }

        [Test]
        public void ZedSummonsShadowsAndRecallsThemWhenCasting() {
            var db = LoadOrIgnore();
            var events = new EventBuffer();
            var battle = Battle(db, events, (HeroIds.Zed, TeamSide.Home, new HexCoord(2, 3)),
                (HeroIds.Malphite, TeamSide.Away, new HexCoord(5, 3)), (HeroIds.Amumu, TeamSide.Away, new HexCoord(5, 5)),
                (HeroIds.Blitzcrank, TeamSide.Away, new HexCoord(6, 2)));
            var zed = battle.Units.First(u => u.Def.Id == HeroIds.Zed);
            var spawned = 0;
            var recalled = 0;
            var drained = new System.Collections.Generic.List<GameEvent>();
            for (var t = 0; t < 40 * db.Rules.TicksPerSecond && !battle.Finished; t++) {
                battle.Step();
                drained.Clear();
                events.DrainTo(drained);
                foreach (var e in drained) {
                    if (e is UnitSpawnedEvent s && battle.FindUnit(s.UnitId).Def.Id == HeroIds.ZedShadow) spawned++;
                    if (e is UnitDiedEvent d && d.KillerId < 0 && battle.FindUnit(d.UnitId).Def.Id == HeroIds.ZedShadow) recalled++;
                }
            }
            Assert.Greater(spawned, 0, "shadows appear");
            Assert.LessOrEqual(battle.Units.Count(u => u.Alive && u.Def.Id == HeroIds.ZedShadow), 3, "at most three at once");
            Assert.Greater(recalled, 0, "casting recalls them");
            Assert.IsTrue(battle.Units.Where(u => u.Def.Id == HeroIds.ZedShadow).All(u => u.Def.Summoned), zed.ToString());
        }

        [Test]
        public void SpellShieldBlocksMagicDamageAndCrowdControlOnly() {
            var db = LoadOrIgnore();
            var events = new EventBuffer();
            var battle = Battle(db, events, (HeroIds.Morgana, TeamSide.Home, new HexCoord(1, 3)), (HeroIds.Zed, TeamSide.Away, new HexCoord(6, 3)));
            battle.Step();
            var morgana = battle.Units.First(u => u.Def.Id == HeroIds.Morgana);
            var zed = battle.Units.First(u => u.Def.Id == HeroIds.Zed);
            morgana.AddShield(morgana, "test", 300, 10, DamageType.Magical, blocksCrowdControl: true);
            var hp = morgana.Hp;
            morgana.TakeDamage(new Damage(200, DamageType.Magical, 0), zed);
            Assert.AreEqual(hp, morgana.Hp, 1e-9, "magic damage absorbed");
            morgana.Stun(2);
            Assert.IsFalse(morgana.Status.IsStunned, "no crowd control while it holds");
            morgana.TakeDamage(new Damage(100, DamageType.Physical, 0), zed);
            Assert.Less(morgana.Hp, hp, "physical damage goes through");
            morgana.TakeDamage(new Damage(500, DamageType.Magical, 0), zed);
            Assert.AreEqual(0, morgana.ShieldAmount, 1e-9, "broken");
            morgana.Stun(2);
            Assert.IsTrue(morgana.Status.IsStunned, "crowd control lands once it breaks");
        }
    }
}
