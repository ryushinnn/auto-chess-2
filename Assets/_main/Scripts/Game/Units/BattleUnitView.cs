using System;
using AutoChess.Core;
using AutoChess.Core.Combat;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// A unit inside a battle (was BattleHero). Follows its <see cref="CombatUnit"/> (interpolated between
    /// simulation ticks) and plays animations / numbers from the battle events routed by <see cref="BattleView"/>.
    /// </summary>
    public class BattleUnitView : UnitView {
        [ShowInInspector, ReadOnly] public int UnitId => combatUnit?.Id ?? -1;
        public CombatUnit CombatUnit => combatUnit;

        CombatUnit combatUnit;
        IGameSession session;
        int lastNextCell = -1;
        Transform head;
        readonly SkillVfx[] statusVfx = new SkillVfx[StatusVisuals.All.Length];

        /// <param name="allySide">Side of the player watching (their units are allies, drawn on the near half).</param>
        public void Bind(IGameSession gameSession, CombatUnit unit, TeamSide allySide) {
            session = gameSession;
            combatUnit = unit;
            lastNextCell = -1;
            var ally = unit.Side == allySide;
            SetHero(HeroTraitDB.Instance.Find(unit.Def.Id), unit.Rank, ally);
            name = $"({unit.Rank}){unit.Def.Id}#{unit.Id}";
            WorldPosition = BoardSpace.BattleToWorld(unit.Position);
            Rotation.Rotate(ally ? Vector3.forward : -Vector3.forward, true);
            ItemsView.SetItems(unit.Items);
            SwitchCanvas(false);
            PlaceHead();
        }

        public void Unbind() {
            StopStatusVisuals();
            combatUnit = null;
            session = null;
        }

        void Update() {
            if (combatUnit == null) return;
            var t = session.TickProgress;
            WorldPosition = BoardSpace.BattleToWorld(Vec2.Lerp(combatUnit.PreviousPosition, combatUnit.Position, t));

            var next = combatUnit.Movement.NextCell;
            if (next != lastNextCell) {
                lastNextCell = next;
                if (next >= 0) FaceTowards(BoardSpace.BattleToWorld(BoardSpace.Instance.Grid.Center(next)));
            }

            UpdateStatusVisuals();
            if (!combatUnit.Alive) return;
            Bars.SetHealth(combatUnit);
            Bars.SetEnergy((float)(combatUnit.Energy / session.Db.Rules.MaxEnergy));
        }

        /// <summary>Anchor of the status marks: the top of the model (bind pose, rank scale included).</summary>
        void PlaceHead() {
            if (head == null) {
                head = new GameObject("Head").transform;
                head.SetParent(transform, false);
            }
            var top = float.NegativeInfinity;
            foreach (var r in mecanim.GetComponentsInChildren<Renderer>()) {
                if (r is SkinnedMeshRenderer || r is MeshRenderer) top = Mathf.Max(top, r.bounds.max.y);
            }
            var height = float.IsInfinity(top) ? 1.8f : Mathf.Clamp(top - transform.position.y, 0.8f, 4f);
            head.localPosition = Vector3.up * height;
        }

        /// <summary>Keeps a mark on the unit for each crowd control / status it has right now.</summary>
        void UpdateStatusVisuals() {
            var status = combatUnit.Status;
            for (var i = 0; i < StatusVisuals.All.Length; i++) {
                var (key, active, onHead) = StatusVisuals.All[i];
                var on = combatUnit.Alive && active(status);
                if (on && statusVfx[i] == null) {
                    var vfx = VfxPool.Instance.GetVfx<SkillVfx>(key);
                    if (vfx == null) continue;
                    var anchor = onHead ? head : transform;
                    var slot = i;
                    vfx.Released += released => {
                        if (statusVfx[slot] == released) statusVfx[slot] = null;
                    };
                    vfx.Play(VisualMode.Attach, anchor.position, anchor.position, float.MaxValue, 1, anchor);
                    statusVfx[i] = vfx;
                }
                else if (!on && statusVfx[i] != null) {
                    statusVfx[i].Stop();
                    statusVfx[i] = null;
                }
            }
        }

        void StopStatusVisuals() {
            for (var i = 0; i < statusVfx.Length; i++) {
                if (statusVfx[i] != null) statusVfx[i].Stop();
                statusVfx[i] = null;
            }
        }

        void FaceUnit(int unitId, BattleView battle) {
            var target = battle.FindView(unitId);
            if (target != null) FaceTowards(target.transform.position);
        }

        public void Handle(BattleEvent e, BattleView battle) {
            switch (e) {
                case UnitMoveStartedEvent _:
                    mecanim.Run();
                    break;
                case UnitMoveStoppedEvent _:
                    mecanim.Idle();
                    if (combatUnit?.Target != null) FaceUnit(combatUnit.Target.Id, battle);
                    break;
                case UnitAttackStartedEvent a:
                    FaceUnit(a.TargetId, battle);
                    mecanim.SetAttackSpeed((float)a.AnimationSpeed);
                    mecanim.Attack();
                    break;
                case UnitAttackInterruptedEvent _:
                    mecanim.InterruptAttack();
                    break;
                case UnitSkillStartedEvent s:
                    FaceUnit(s.TargetId, battle);
                    mecanim.UseSkill(s.Variant);
                    break;
                case UnitSkillInterruptedEvent _:
                    mecanim.InterruptSkill();
                    break;
                case UnitDamagedEvent d:
                    Bars.ShowDamage((float)d.Amount, d.Type, d.Crit, d.StackIndex);
                    if (combatUnit != null) Bars.SetHealth(combatUnit);
                    break;
                case UnitShieldChangedEvent _:
                    if (combatUnit != null) Bars.SetHealth(combatUnit);
                    break;
                case UnitHealedEvent h:
                    Bars.ShowHeal((float)h.Amount);
                    break;
                case UnitEnergyDrainEvent drain:
                    Bars.PlayEnergyDrain((float)drain.Delay, (float)drain.Duration);
                    break;
                case UnitAirborneEvent air:
                    AirborneView.Airborne((float)air.Duration);
                    break;
                case UnitDiedEvent _:
                    Bars.SetHealth(0);
                    mecanim.Death();
                    SwitchCanvas(false);
                    name = "(DEAD) " + name;
                    break;
                case MarkAddedEvent m:
                    Marks.AddMark(m.MarkId, m.Key, m.Stacks, (float)m.Duration);
                    break;
                case MarkRemovedEvent m:
                    Marks.RemoveMark(m.MarkId);
                    break;
            }
        }

        /// <summary>The battle is over: everybody still standing stops.</summary>
        public void Freeze() {
            if (combatUnit != null && combatUnit.Alive) mecanim.Idle();
            StopStatusVisuals();
        }
    }

    /// <summary>VFX keys of the status marks (built by VfxBuilder) and when a unit shows them.</summary>
    public static class StatusVisuals {
        public const string Stun = "status_stun";
        public const string Silence = "status_silence";
        public const string AntiHeal = "status_antiheal";
        public const string Invulnerable = "status_invulnerable";

        /// <summary>(key, shown while, anchored on the head rather than the feet).</summary>
        public static readonly (string key, Func<StatusState, bool> active, bool onHead)[] All = {
            (Stun, s => s.IsStunned, true),
            (Silence, s => s.IsSilenced, true),
            (AntiHeal, s => s.IsAntiHeal, true),
            (Invulnerable, s => s.IsInvulnerable, false),
        };
    }
}
