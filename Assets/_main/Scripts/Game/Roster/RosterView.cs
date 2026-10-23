using System.Collections;
using System.Collections.Generic;
using AutoChess.Core;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Shows the viewed player's owned units on the bench and the board (was LineUp): the local player's, or
    /// another player's while scouting (read-only). Every roster event reconciles the views with the player
    /// state, so a view can never disagree with the model.
    /// </summary>
    public class RosterView : MonoBehaviour, ISessionView, ISessionResettable {
        [SerializeField] RosterUnitView heroPrefab;

        readonly Dictionary<int, RosterUnitView> views = new();
        readonly Queue<RosterUnitView> pool = new();
        readonly List<int> stale = new();

        GameManager game;
        bool pickable;
        bool diving;      // board units are jumping into the portal (still drawn, no longer on their tiles)
        bool boardHidden; // board units are hidden while their battle copies fight
        Coroutine hideRoutine;

        /// <summary>Board units are standing on the board (not diving / hidden during a battle).</summary>
        public bool BoardVisible => !diving && !boardHidden;
        public IReadOnlyDictionary<int, RosterUnitView> Views => views;

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

        /// <summary>Another player's roster (scouting) or back home: rebuild every view for the new player.</summary>
        void OnViewChanged(int playerId) {
            OnSessionReset();
            var phase = game.Session.State.Phase;
            if (phase == MatchPhase.Preparation || phase == MatchPhase.Setup || phase == MatchPhase.Finished) {
                ShowBoardUnits();
                SetPickable(phase == MatchPhase.Preparation);
            }
            else {
                boardHidden = true;
                Sync(false);
            }
        }

        public void OnSessionReset() {
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = null;
            foreach (var view in views.Values) {
                view.Deactivate();
                pool.Enqueue(view);
            }
            views.Clear();
            diving = false;
            boardHidden = false;
            pickable = false;
        }

        public RosterUnitView FindView(int unitId) => views.TryGetValue(unitId, out var v) ? v : null;

        void OnEvent(GameEvent e) {
            if (e is PlayerEvent pe && pe.PlayerId != game.ViewedPlayerId) return;
            switch (e) {
                case UnitAddedEvent _:
                case UnitRemovedEvent _:
                case UnitRankChangedEvent _:
                case UnitItemsChangedEvent _:
                    Sync(false);
                    break;
                case UnitMovedEvent moved:
                    Sync(game.Session.State.Phase == MatchPhase.Preparation, moved.UnitId);
                    break;
                case PhaseChangedEvent phase:
                    OnPhase(phase.Phase);
                    break;
            }
        }

        /// <summary>Creates, removes, refreshes and places views to match the player state.</summary>
        void Sync(bool animateMoves, int movedUnit = -1) {
            var player = game.ViewedPlayer;
            stale.Clear();
            foreach (var id in views.Keys) {
                if (player.FindUnit(id) == null) stale.Add(id);
            }
            foreach (var id in stale) {
                var view = views[id];
                views.Remove(id);
                view.Deactivate();
                pool.Enqueue(view);
            }

            foreach (var unit in player.Units) {
                if (!views.TryGetValue(unit.Id, out var view)) {
                    if (!pool.TryDequeue(out view)) view = Instantiate(heroPrefab, transform);
                    view.Activate();
                    view.Bind(unit);
                    view.PlaceAt(unit.Location, false);
                    views[unit.Id] = view;
                }
                else {
                    view.Refresh(unit);
                    if (view.Location != unit.Location) view.PlaceAt(unit.Location, animateMoves);
                }
                ApplyVisibility(view, unit);
                view.SwitchPickable(pickable);
            }
        }

        void ApplyVisibility(RosterUnitView view, UnitInstance unit) {
            var hidden = boardHidden && unit.Location.IsBoard;
            if (hidden == !view.gameObject.activeSelf) return;
            if (hidden) view.Deactivate();
            else view.Activate();
        }

        void OnPhase(MatchPhase phase) {
            switch (phase) {
                case MatchPhase.Preparation:
                    if (hideRoutine != null) StopCoroutine(hideRoutine);
                    hideRoutine = null;
                    ShowBoardUnits();
                    SetPickable(true);
                    break;

                case MatchPhase.Transition:
                    SetPickable(false);
                    DiveIn();
                    break;

                default:
                    SetPickable(false);
                    break;
            }
        }

        /// <summary>Only the local player's own units can be picked up, never a scouted roster.</summary>
        void SetPickable(bool value) {
            pickable = value && !game.IsScouting && !game.LocalPlayer.Eliminated;
            foreach (var view in views.Values) view.SwitchPickable(pickable);
        }

        void ShowBoardUnits() {
            diving = false;
            boardHidden = false;
            var player = game.ViewedPlayer;
            foreach (var unit in player.Units) {
                if (!views.TryGetValue(unit.Id, out var view)) continue;
                view.Activate();
                view.SwitchCanvas(true);
                view.Mecanim.SetRendererVisibility(true);
                view.Mecanim.ResetPosition();
                view.PlaceAt(unit.Location, false);
            }
            Sync(false);
        }

        /// <summary>Board units jump into the portal, then are hidden while the battle copies fight.</summary>
        void DiveIn() {
            var player = game.ViewedPlayer;
            diving = true; // the tiles stop showing them as occupied right away
            var anyone = false;
            foreach (var unit in player.Units) {
                if (!unit.Location.IsBoard || !views.TryGetValue(unit.Id, out var view)) continue;
                view.PlaceAt(unit.Location, false);
                view.Activate();
                view.Mecanim.DiveIn();
                view.SwitchCanvas(false);
                anyone = true;
            }
            MapVisual.Instance.SwitchPortal(true);
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(HideBoardUnitsLater(anyone));
        }

        IEnumerator HideBoardUnitsLater(bool anyone) {
            yield return new WaitForSeconds(game.Settings.spawnEnemiesDelay);
            hideRoutine = null;
            boardHidden = true;
            if (!anyone) yield break;
            var player = game.ViewedPlayer;
            foreach (var unit in player.Units) {
                if (unit.Location.IsBoard && views.TryGetValue(unit.Id, out var view)) view.Deactivate();
            }
        }
    }
}
