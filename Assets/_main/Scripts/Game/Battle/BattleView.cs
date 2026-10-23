using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AutoChess.Core;
using AutoChess.Core.Combat;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Shows the battle of the viewed player (<see cref="GameManager.ViewedPlayerId"/>: the local player, or the one
    /// being scouted) (was BattleField): spawns a <see cref="BattleUnitView"/> per combat unit once the portal opens,
    /// routes battle events to them, and plays projectiles, areas and effect VFX. When the viewed player visits
    /// (Away team of a PvP battle) the battle is drawn rotated so their units stand on the near half.
    /// </summary>
    public class BattleView : MonoBehaviour, ISessionView, ISessionResettable {
        [SerializeField] BattleUnitView heroPrefab;

        readonly Dictionary<int, BattleUnitView> views = new();
        readonly Queue<BattleUnitView> pool = new();
        readonly Dictionary<int, Projectile> projectiles = new();
        readonly Dictionary<int, (AreaView area, Vfx vfx)> areas = new();
        readonly Dictionary<int, Vfx> effects = new();
        readonly Dictionary<int, SkillVfx> visuals = new();

        GameManager game;
        BattleSimulation pending;
        BattleSimulation visible;
        Coroutine spawnRoutine;

        /// <summary>The battle currently drawn, or null.</summary>
        public BattleSimulation VisibleBattle => visible;
        /// <summary>Side of the viewed player in <see cref="VisibleBattle"/>.</summary>
        public TeamSide AllySide { get; private set; }
        public IReadOnlyDictionary<int, BattleUnitView> Views => views;

        public void Bind(GameManager gameManager) {
            game = gameManager;
            game.EventRaised += OnEvent;
            game.ViewChanged += OnViewChanged;
        }

        void OnDestroy() {
            if (game == null) return;
            game.EventRaised -= OnEvent;
            game.ViewChanged -= OnViewChanged;
        }

        /// <summary>Scouting another player mid-round: show their battle right away.</summary>
        void OnViewChanged(int playerId) {
            var phase = game.Session.State.Phase;
            if (phase != MatchPhase.Transition && phase != MatchPhase.Battle) return;
            var battle = game.Session.State.BattleOf(playerId);
            if (battle == null || battle == visible) return;
            Clear();
            if (phase == MatchPhase.Transition) pending = battle;
            else {
                Spawn(battle);
                foreach (var view in views.Values) {
                    if (view.CombatUnit != null && view.CombatUnit.Alive) view.SwitchCanvas(true);
                }
            }
        }

        public void OnSessionReset() => Clear();

        public BattleUnitView FindView(int unitId) => views.TryGetValue(unitId, out var v) ? v : null;

        void OnEvent(GameEvent e) {
            switch (e) {
                case BattleCreatedEvent created when created.PlayerId == game.ViewedPlayerId:
                    pending = game.Session.State.BattleOf(created.PlayerId);
                    break;
                case PhaseChangedEvent phase:
                    OnPhase(phase.Phase);
                    break;
                case BattleEvent battleEvent when visible != null && battleEvent.BattleId == visible.Id:
                    OnBattleEvent(battleEvent);
                    break;
            }
        }

        void OnPhase(MatchPhase phase) {
            switch (phase) {
                case MatchPhase.Transition:
                    if (spawnRoutine != null) StopCoroutine(spawnRoutine);
                    spawnRoutine = StartCoroutine(SpawnAfterPortal());
                    break;
                case MatchPhase.Battle:
                    // bars and marks show once the fight starts (hidden while the units come out of the portal)
                    foreach (var view in views.Values) {
                        if (view.CombatUnit != null && view.CombatUnit.Alive) view.SwitchCanvas(true);
                    }
                    break;
                case MatchPhase.Preparation:
                case MatchPhase.Finished:
                    Clear();
                    MapVisual.Instance.SetHalfMapVisibility(false);
                    break;
            }
        }

        IEnumerator SpawnAfterPortal() {
            var settings = game.Settings;
            yield return new WaitForSeconds(settings.spawnEnemiesDelay);
            spawnRoutine = null;
            if (pending == null) yield break;
            Spawn(pending);
            MapVisual.Instance.SetHalfMapVisibility(true);
            yield return new WaitForSeconds(settings.portalCloseDelay);
            MapVisual.Instance.SwitchPortal(false);
        }

        void Spawn(BattleSimulation battle) {
            Clear();
            visible = battle;
            var away = battle.Setup.Away;
            AllySide = away.PlayerId == game.ViewedPlayerId && !away.Ghost ? TeamSide.Away : TeamSide.Home;
            BoardSpace.BattleFlipped = AllySide == TeamSide.Away;
            foreach (var unit in battle.Units) {
                if (!pool.TryDequeue(out var view)) view = Instantiate(heroPrefab, transform);
                view.Activate();
                view.Bind(game.Session, unit, AllySide);
                views[unit.Id] = view;
                if (unit.Side == AllySide) view.Mecanim.DiveOut();
            }
        }

        void Clear() {
            if (spawnRoutine != null) {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
            foreach (var view in views.Values) {
                view.Unbind();
                view.Deactivate();
                pool.Enqueue(view);
            }
            views.Clear();
            foreach (var p in projectiles.Values) {
                if (p != null) VfxPool.Instance.DestroyVfx(p);
            }
            projectiles.Clear();
            foreach (var (area, vfx) in areas.Values) {
                if (area != null) Destroy(area.gameObject);
                if (vfx != null) VfxPool.Instance.DestroyVfx(vfx);
            }
            areas.Clear();
            foreach (var vfx in effects.Values) {
                if (vfx != null) VfxPool.Instance.DestroyVfx(vfx);
            }
            effects.Clear();
            foreach (var vfx in visuals.Values.ToList()) {
                if (vfx != null) vfx.Release();
            }
            visuals.Clear();
            visible = null;
            pending = null;
            BoardSpace.BattleFlipped = false;
        }

        void OnBattleEvent(BattleEvent e) {
            switch (e) {
                case VisualEvent visual:
                    PlayVisual(visual);
                    break;
                case VisualStoppedEvent stopped:
                    if (visuals.TryGetValue(stopped.VisualId, out var stopping) && stopping != null) stopping.Stop();
                    break;
                case UnitSpawnedEvent spawned:
                    // A synergy's summon joining mid-battle (Chaos' Apocalypse Warrior).
                    var unit = visible.FindUnit(spawned.UnitId);
                    if (unit == null || views.ContainsKey(unit.Id)) break;
                    if (!pool.TryDequeue(out var summoned)) summoned = Instantiate(heroPrefab, transform);
                    summoned.Activate();
                    summoned.Bind(game.Session, unit, AllySide);
                    summoned.SwitchCanvas(true);
                    views[unit.Id] = summoned;
                    break;
                case UnitBattleEvent unitEvent:
                    if (views.TryGetValue(unitEvent.UnitId, out var view)) {
                        view.Handle(unitEvent, this);
                        if (unitEvent is EffectAddedEvent added) AddEffect(view, added);
                        else if (unitEvent is EffectRemovedEvent removed) RemoveEffect(removed.EffectId);
                        else if (unitEvent is UnitDamagedEvent damaged) PlayHit(view, damaged);
                    }
                    break;
                case ProjectileLaunchedEvent launched:
                    LaunchProjectile(launched);
                    break;
                case ProjectileEndedEvent ended:
                    if (projectiles.Remove(ended.ProjectileId, out var projectile) && projectile != null) {
                        VfxPool.Instance.DestroyVfx(projectile);
                    }
                    break;
                case AreaCreatedEvent created:
                    CreateArea(created);
                    break;
                case AreaExpiredEvent expired:
                    if (areas.Remove(expired.AreaId, out var entry)) {
                        if (entry.area != null) Destroy(entry.area.gameObject);
                        if (entry.vfx != null) VfxPool.Instance.DestroyVfx(entry.vfx);
                    }
                    break;
                case BattleEndedEvent _:
                    foreach (var v in views.Values) v.Freeze();
                    break;
            }
        }

        void LaunchProjectile(ProjectileLaunchedEvent e) {
            Core.Combat.Projectile model = null;
            foreach (var p in visible.Projectiles) {
                if (p.Id == e.ProjectileId) model = p;
            }
            var view = VfxPool.Instance.GetVfx<Projectile>(e.Visual);
            if (view == null || model == null) return;
            view.Track(game.Session, model);
            projectiles[e.ProjectileId] = view;
        }

        void CreateArea(AreaCreatedEvent e) {
            var position = BoardSpace.BattleToWorld(e.Position);
            var area = Instantiate(PrefabDB.Instance.DotArea, position, Quaternion.identity, transform);
            area.Show((float)e.Radius);
            var vfx = VfxPool.Instance.GetVfx(e.Visual);
            if (vfx != null) vfx.transform.position = position;
            areas[e.AreaId] = (area, vfx);
        }

        /// <summary>Hit flash of an attack or a skill on the damaged unit, colored by damage type (DoTs and items show none).</summary>
        void PlayHit(BattleUnitView view, UnitDamagedEvent e) {
            var kind = e.Kind switch {
                HitKind.Attack => "hit_attack_",
                HitKind.Ability => "hit_skill_",
                _ => null,
            };
            if (kind == null || e.Amount <= 0) return;
            var type = e.Type switch {
                DamageType.Physical => "physical",
                DamageType.Magical => "magical",
                _ => "true",
            };
            var vfx = VfxPool.Instance.GetVfx<SkillVfx>(kind + type);
            if (vfx == null) return;
            var position = view.transform.position;
            vfx.Play(VisualMode.Burst, position, position, 0.3f, e.Crit ? 1.35f : 1f, null);
        }

        void PlayVisual(VisualEvent e) {
            var vfx = VfxPool.Instance.GetVfx<SkillVfx>(e.Visual);
            if (vfx == null) return;
            var follow = e.UnitId >= 0 && views.TryGetValue(e.UnitId, out var view) ? view.transform : null;
            var id = e.VisualId;
            visuals[id] = vfx;
            vfx.Released += _ => visuals.Remove(id);
            // authored for a radius of one cell (3 cells across)
            var scale = (float)(2 * e.Radius + 1) / 3f;
            vfx.Play(e.Mode, BoardSpace.BattleToWorld(e.From), BoardSpace.BattleToWorld(e.To), (float)e.Duration, scale, follow);
        }

        void AddEffect(BattleUnitView view, EffectAddedEvent e) {
            var binding = EffectVisuals.Find(e.Visual);
            var vfx = VfxPool.Instance.GetVfx<ScalableVfx>(e.Visual);
            if (vfx == null) return;
            if (binding.ScaleWithModel) vfx.SetScale(view.Model.localScale);
            var bodyParts = view.Mecanim.GetComponent<BodyParts>();
            var bone = binding.Bone != null && bodyParts != null ? bodyParts.GetBone(binding.Bone) : null;
            var bind = vfx.GetComponent<PositionBind>();
            if (bind != null) bind.SetTarget(bone != null ? bone : view.transform, binding.OffsetY);
            effects[e.EffectId] = vfx;
        }

        void RemoveEffect(int effectId) {
            if (effects.Remove(effectId, out var vfx) && vfx != null) VfxPool.Instance.DestroyVfx(vfx);
        }
    }

    /// <summary>How a status effect's VFX attaches to its unit.</summary>
    public static class EffectVisuals {
        public readonly struct Binding {
            public readonly string Bone;
            public readonly float OffsetY;
            public readonly bool ScaleWithModel;

            public Binding(string bone, float offsetY, bool scaleWithModel) {
                Bone = bone;
                OffsetY = offsetY;
                ScaleWithModel = scaleWithModel;
            }
        }

        static readonly Dictionary<string, Binding> Bindings = new() {
            { Core.Combat.Heroes.MalphiteSkill.SkinVisual, new Binding("chest", -2f, true) },
        };

        public static Binding Find(string visual) =>
            visual != null && Bindings.TryGetValue(visual, out var b) ? b : new Binding(null, 0, false);
    }
}
