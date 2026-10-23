using System.Collections;
using System.Collections.Generic;
using AutoChess.Core;
using AutoChess.Core.Combat;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Shows the local player's battle (was BattleField): spawns a <see cref="BattleUnitView"/> per combat unit
    /// once the portal opens, routes battle events to them, and plays projectiles, areas and effect VFX.
    /// </summary>
    public class BattleView : MonoBehaviour, ISessionView, ISessionResettable {
        [SerializeField] BattleUnitView heroPrefab;

        readonly Dictionary<int, BattleUnitView> views = new();
        readonly Queue<BattleUnitView> pool = new();
        readonly Dictionary<int, Projectile> projectiles = new();
        readonly Dictionary<int, (AreaView area, Vfx vfx)> areas = new();
        readonly Dictionary<int, Vfx> effects = new();

        GameManager game;
        BattleSimulation pending;
        BattleSimulation visible;
        Coroutine spawnRoutine;

        /// <summary>The battle currently drawn, or null.</summary>
        public BattleSimulation VisibleBattle => visible;
        public IReadOnlyDictionary<int, BattleUnitView> Views => views;

        public void Bind(GameManager gameManager) {
            game = gameManager;
            game.EventRaised += OnEvent;
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        public void OnSessionReset() => Clear();

        public BattleUnitView FindView(int unitId) => views.TryGetValue(unitId, out var v) ? v : null;

        void OnEvent(GameEvent e) {
            switch (e) {
                case BattleCreatedEvent created when created.PlayerId == game.Session.LocalPlayerId:
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
            foreach (var unit in battle.Units) {
                if (!pool.TryDequeue(out var view)) view = Instantiate(heroPrefab, transform);
                view.Activate();
                view.Bind(game.Session, unit);
                views[unit.Id] = view;
                if (unit.Side == TeamSide.Home) view.Mecanim.DiveOut();
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
            visible = null;
            pending = null;
        }

        void OnBattleEvent(BattleEvent e) {
            switch (e) {
                case UnitBattleEvent unitEvent:
                    if (views.TryGetValue(unitEvent.UnitId, out var view)) {
                        view.Handle(unitEvent, this);
                        if (unitEvent is EffectAddedEvent added) AddEffect(view, added);
                        else if (unitEvent is EffectRemovedEvent removed) RemoveEffect(removed.EffectId);
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
            var position = BoardSpace.ToWorld(e.Position);
            var area = Instantiate(PrefabDB.Instance.DotArea, position, Quaternion.identity, transform);
            area.Show((float)e.Radius);
            var vfx = VfxPool.Instance.GetVfx(e.Visual);
            if (vfx != null) vfx.transform.position = position;
            areas[e.AreaId] = (area, vfx);
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
