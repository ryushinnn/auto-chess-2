using System.Collections.Generic;
using System.Linq;
using AutoChess.Core.Combat;
using AutoChess.Core.Net;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    public class CombatTests {
        static BattleSimulation Battle(EventBuffer events, ulong seed, params (string id, int row, int col, TeamSide side)[] units) {
            var setup = new BattleSetup { Seed = seed };
            setup.Home.ApplyTraits = true;
            foreach (var (id, row, col, side) in units) {
                setup.Team(side).Units.Add(new CombatantSetup { UnitId = id, Cell = new HexCoord(row, col) });
            }
            return new BattleSimulation(1, TestData.Database(), setup, events, TestData.Abilities());
        }

        static BattleOutcome RunToEnd(BattleSimulation b, int maxTicks = 30 * 120) {
            for (var i = 0; i < maxTicks && !b.Finished; i++) b.Step();
            return b.Outcome;
        }

        [Test]
        public void MitigationFollowsTheArmorFormula() {
            var b = Battle(new EventBuffer(), 1, (TestData.Brawler, 0, 0, TeamSide.Home), (TestData.Knight, 7, 7, TeamSide.Away));
            var knight = b.Units[1];
            var dealt = knight.TakeDamage(new Damage(160, DamageType.Physical, 0), null, false);
            Assert.AreEqual(160 / (1 + 60 / 100f), dealt, 1e-3);
            var trueDealt = knight.TakeDamage(new Damage(50, DamageType.True, 0), null, false);
            Assert.AreEqual(50, trueDealt, 1e-4);
            var pierced = knight.TakeDamage(new Damage(100, DamageType.Physical, 100), null, false);
            Assert.AreEqual(100 / (1 + 1 / 100f), pierced, 1e-3, "armor never drops below the minimum");
        }

        [Test]
        public void DeathHappensOnceAndEndsTheBattle() {
            var events = new EventBuffer();
            var b = Battle(events, 1, (TestData.Brawler, 0, 0, TeamSide.Home), (TestData.Dummy, 7, 7, TeamSide.Away));
            b.Start();
            var dummy = b.Units[1];
            dummy.TakeDamage(new Damage(10000, DamageType.True, 0), b.Units[0]);
            dummy.TakeDamage(new Damage(10000, DamageType.True, 0), b.Units[0]);
            Assert.IsFalse(dummy.Alive);
            Assert.AreEqual(1, events.Pending.OfType<UnitDiedEvent>().Count());
            Assert.IsTrue(b.Finished);
            Assert.AreEqual(BattleOutcome.HomeWin, b.Outcome);
            Assert.AreEqual(1, events.Pending.OfType<BattleEndedEvent>().Count());
        }

        [Test]
        public void EmptyTeamLosesImmediately() {
            var b = Battle(new EventBuffer(), 1, (TestData.Brawler, 5, 5, TeamSide.Away));
            b.Start();
            Assert.IsTrue(b.Finished);
            Assert.AreEqual(BattleOutcome.AwayWin, b.Outcome);
        }

        [Test]
        public void CrowdControlRespectsTenacityAndUnstoppable() {
            var b = Battle(new EventBuffer(), 1, (TestData.Brawler, 0, 0, TeamSide.Home), (TestData.Brawler, 7, 7, TeamSide.Away));
            var u = b.Units[0];
            u.AddModifierSet(ModifierSet.Permanent(u, "ten", new[] { new StatModifier(StatKind.Tenacity, 0.5f, ModifierType.Flat) }, mark: false));
            u.Stun(2);
            Assert.AreEqual(1f, u.Status.Stun, 1e-5);
            u.Status.Unstoppable = true;
            u.Stun(10);
            u.Airborne(3);
            Assert.AreEqual(1f, u.Status.Stun, 1e-5);
            Assert.IsFalse(u.Status.IsAirborne);
        }

        [Test]
        public void HealingIsCappedAndHalvedByAntiHeal() {
            var b = Battle(new EventBuffer(), 1, (TestData.Brawler, 0, 0, TeamSide.Home), (TestData.Brawler, 7, 7, TeamSide.Away));
            var u = b.Units[0];
            u.TakeDamage(new Damage(500, DamageType.True, 0), null, false);
            u.Heal(100);
            Assert.AreEqual(600, u.Hp, 1e-3);
            u.AntiHeal(5);
            u.Heal(100);
            Assert.AreEqual(650, u.Hp, 1e-3);
            u.Heal(100000);
            Assert.AreEqual(u.MaxHp, u.Hp, 1e-3);
        }

        [Test]
        public void TimedModifiersExpire() {
            var b = Battle(new EventBuffer(), 1, (TestData.Brawler, 0, 0, TeamSide.Home), (TestData.Dummy, 7, 7, TeamSide.Away));
            var u = b.Units[0];
            var baseAs = u.AttackSpeed;
            u.AddModifierSet(ModifierSet.Timed(u, "haste", 1f, new[] { new StatModifier(StatKind.AttackSpeed, 1f, ModifierType.Percent) }));
            Assert.AreEqual(baseAs * 2, u.AttackSpeed, 1e-4);
            Assert.AreEqual(1, u.Marks.Count);
            for (var i = 0; i < 31; i++) b.Step();
            Assert.AreEqual(baseAs, u.AttackSpeed, 1e-4);
            Assert.AreEqual(0, u.Marks.Count);
        }

        [Test]
        public void DamageOverTimeTicksTheRightNumberOfTimes() {
            var events = new EventBuffer();
            var b = Battle(events, 1, (TestData.Dummy, 0, 0, TeamSide.Home), (TestData.Dummy, 7, 7, TeamSide.Away));
            b.Start();
            var u = b.Units[0];
            u.AddDamageOverTime(DamageOverTime.Create("burn", b.Units[1], new Damage(10, DamageType.True, 0), 4, 0.5f, applyInstantly: false));
            for (var i = 0; i < 30 * 5; i++) b.Step();
            Assert.AreEqual(4, events.Pending.OfType<UnitDamagedEvent>().Count(e => e.UnitId == u.Id));
            Assert.AreEqual(u.MaxHp - 40, u.Hp, 1e-3);
        }

        [Test]
        public void UnitsWalkIntoRangeAndFight() {
            var events = new EventBuffer();
            var b = Battle(events, 1, (TestData.Brawler, 0, 0, TeamSide.Home), (TestData.Brawler, 7, 7, TeamSide.Away));
            var outcome = RunToEnd(b);
            Assert.AreNotEqual(BattleOutcome.None, outcome);
            Assert.That(events.Pending.OfType<UnitMoveStartedEvent>().Count(), Is.GreaterThan(0));
            Assert.That(events.Pending.OfType<UnitAttackStartedEvent>().Count(), Is.GreaterThan(5));
            var winner = b.Units.Single(u => u.Alive);
            Assert.GreaterOrEqual(winner.OccupiedCell, 0, "the survivor stands on a cell");
        }

        [Test]
        public void UnitsNeverShareACell() {
            var b = Battle(new EventBuffer(), 9,
                (TestData.Brawler, 0, 1, TeamSide.Home), (TestData.Brawler, 0, 3, TeamSide.Home), (TestData.Archer, 1, 2, TeamSide.Home),
                (TestData.Knight, 2, 4, TeamSide.Home), (TestData.Brawler, 6, 2, TeamSide.Away), (TestData.Brawler, 6, 4, TeamSide.Away),
                (TestData.Archer, 7, 3, TeamSide.Away), (TestData.Knight, 5, 3, TeamSide.Away));
            for (var t = 0; t < 30 * 180 && !b.Finished; t++) {
                b.Step();
                var cells = new HashSet<int>();
                foreach (var u in b.Units) {
                    if (!u.Alive || u.OccupiedCell < 0) continue;
                    Assert.IsTrue(cells.Add(u.OccupiedCell), $"tick {b.Tick}: two units on {b.Grid.CoordOf(u.OccupiedCell)}");
                    Assert.AreSame(u, b.CellOwner(u.OccupiedCell));
                }
            }
            Assert.IsTrue(b.Finished);
        }

        [Test]
        public void MarksmanTraitBuffsAttackSpeed() {
            var b = Battle(new EventBuffer(), 1,
                (TestData.Archer, 0, 0, TeamSide.Home), (TestData.Archer, 0, 1, TeamSide.Home), (TestData.Dummy, 7, 7, TeamSide.Away));
            var before = b.Units[0].AttackSpeed;
            b.Start();
            Assert.AreEqual(1, Traits.Compute(b.Db, new[] { TestData.Archer, TestData.Archer }).First(t => t.Id.Role == Role.Marksman).Count,
                "two copies of one archer count once");
            Assert.AreEqual(before, b.Units[0].AttackSpeed, 1e-5, "one distinct marksman is below the first checkpoint");
        }

        [Test]
        public void SameSeedReplaysIdentically() {
            ulong Run(ulong seed) {
                var b = Battle(new EventBuffer(), seed,
                    (TestData.Brawler, 0, 1, TeamSide.Home), (TestData.Archer, 1, 2, TeamSide.Home), (TestData.Knight, 2, 4, TeamSide.Home),
                    (TestData.Brawler, 6, 2, TeamSide.Away), (TestData.Archer, 7, 3, TeamSide.Away), (TestData.Dragon, 5, 3, TeamSide.Away));
                RunToEnd(b);
                return StateHasher.Hash(b);
            }

            Assert.AreEqual(Run(123), Run(123));
            Assert.AreNotEqual(Run(123), Run(124), "crit rolls depend on the seed");
        }

        [Test]
        public void ProjectilesAndAreasHitTheirTargets() {
            var events = new EventBuffer();
            var b = Battle(events, 1, (TestData.Dummy, 0, 0, TeamSide.Home), (TestData.Dummy, 0, 3, TeamSide.Away), (TestData.Dummy, 1, 3, TeamSide.Away));
            b.Start();
            var archer = b.Units[0];
            var dummy = b.Units[1];
            var hit = false;
            b.LaunchProjectile(archer, dummy, "test", p => hit = p.Target == dummy);
            for (var i = 0; i < 30; i++) b.Step();
            Assert.IsTrue(hit);
            Assert.AreEqual(1, events.Pending.OfType<ProjectileEndedEvent>().Count(e => e.Hit));

            var area = b.CreateArea(archer, dummy.Position, 0.5f, new Damage(10, DamageType.True, 0), 3, 0.2f, false, "rain");
            for (var i = 0; i < 30; i++) b.Step();
            Assert.AreEqual(3, events.Pending.OfType<UnitDamagedEvent>().Count(e => e.UnitId == dummy.Id && e.Amount == 10));
            Assert.AreEqual(0, events.Pending.OfType<UnitDamagedEvent>().Count(e => e.UnitId == b.Units[2].Id && e.Amount == 10),
                "the neighbour cell is out of reach (0.5 + unit radius)");
            Assert.AreEqual(1, events.Pending.OfType<AreaExpiredEvent>().Count(e => e.AreaId == area.Id));
        }
    }
}
