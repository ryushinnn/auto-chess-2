using System;
using System.Collections;
using AutoChess.Core;
using DG.Tweening;
using RExt.Extensions;
using RExt.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AutoChess.Game {
    /// <summary>
    /// Pick up an owned unit (hold, or start dragging), preview the drop on the bench / board, then submit a
    /// MoveUnitCommand, or a SellUnitCommand when dropped on the HUD's sell zone (<see cref="OverSellZone"/>);
    /// a plain click opens the unit info (was HeroPicker). Driven by EventSystem pointer
    /// events (PhysicsRaycaster on the camera), so mouse, touch and the AutoTest drag driver all work.
    /// </summary>
    public class UnitDragHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler {
        const float HOLD_TIME_THRESHOLD = 0.2f;
        const float DRAG_POS_Y = 1;

        /// <summary>Set by the HUD: is this screen point over the sell zone?</summary>
        public static Func<Vector2, bool> OverSellZone;
        public static event Action<RosterUnitView> PickStarted;
        public static event Action<RosterUnitView> PickEnded;

        RosterUnitView unit;
        LayerMask mapLayerMask;
        bool overSell;
        bool pickable;
        bool isPicking;
        bool pointerDown;
        UnitLocation? nextLocation;
        Vector3 offset;
        Tween liftTween;
        Coroutine holdCoroutine;
        Vector2 lastPointer;

        public void Initialize(RosterUnitView owner, LayerMask mapMask) {
            unit = owner;
            mapLayerMask = mapMask;
        }

        public void SwitchPickable(bool value) {
            pickable = value;
            if (pickable) return;
            StopHold();
            if (isPicking) InterruptPicking();
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (!pickable || eventData.button != PointerEventData.InputButton.Left) return;
            pointerDown = true;
            lastPointer = eventData.position;
            StopHold();
            holdCoroutine = StartCoroutine(DoHold());
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (!pickable || !pointerDown) return;
            lastPointer = eventData.position;
            if (!isPicking) StartPicking(eventData.position);
        }

        public void OnDrag(PointerEventData eventData) {
            if (!pickable || !isPicking) return;
            lastPointer = eventData.position;
            HandlePicking(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData) { }

        public void OnPointerUp(PointerEventData eventData) {
            if (!pointerDown) return;
            pointerDown = false;
            StopHold();
            if (!pickable) return;
            if (isPicking) EndPicking();
            else ArenaScreen.Instance?.HeroInfo.Toggle(unit);
        }

        IEnumerator DoHold() {
            yield return BetterWaitForSeconds.WaitRealtime(HOLD_TIME_THRESHOLD);
            holdCoroutine = null;
            if (pointerDown && !isPicking) StartPicking(lastPointer);
        }

        void StopHold() {
            if (holdCoroutine != null) StopCoroutine(holdCoroutine);
            holdCoroutine = null;
        }

        bool RaycastMap(Vector2 screen, out Vector3 point) {
            var ray = Utils.MainCamera().ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit, Mathf.Infinity, mapLayerMask) && hit.collider != null) {
                point = hit.point;
                return true;
            }
            point = default;
            return false;
        }

        void StartPicking(Vector2 screen) {
            isPicking = true;
            nextLocation = null;
            overSell = false;
            PickStarted?.Invoke(unit);
            liftTween?.Kill();
            liftTween = unit.Model.DOLocalMoveY(DRAG_POS_Y, 0.2f);
            offset = Vector3.zero;
            if (RaycastMap(screen, out var point)) {
                offset = new Vector3(unit.transform.position.x - point.x, 0, unit.transform.position.z - point.z);
            }
        }

        void HandlePicking(Vector2 screen) {
            var map = MapVisual.Instance;
            overSell = OverSellZone != null && OverSellZone(screen);
            if (overSell) {
                map.RemoveHighlight();
                map.RemoveNotAvailable();
                nextLocation = null;
            }
            if (!RaycastMap(screen, out var hitPoint)) return;
            var point = hitPoint.ToZeroY();
            unit.transform.position = point + offset;
            if (overSell) return;

            var session = GameManager.Instance.Session;
            if (BenchSpace.Instance.TryNearestSlot(point, out var slot)) {
                // dropping on the bench is always possible (move or swap)
                nextLocation = UnitLocation.Bench(slot);
                map.RemoveNotAvailable();
                map.SetHighlight(nextLocation.Value);
                return;
            }

            var playerRows = session.Db.Rules.PlayerRows;
            if (BoardSpace.Instance.TryNearestCell(point, c => c.Row < playerRows, out var cell)) {
                nextLocation = UnitLocation.Board(cell);
                if (session.CanMove(unit.UnitId, nextLocation.Value) == CommandError.None) {
                    map.RemoveNotAvailable();
                    map.SetHighlight(nextLocation.Value);
                }
                else {
                    map.RemoveHighlight();
                    map.SetNotAvailable(nextLocation.Value);
                }
                return;
            }

            map.RemoveHighlight();
            map.RemoveNotAvailable();
            nextLocation = null;
        }

        void EndPicking() {
            isPicking = false;
            liftTween?.Kill();
            liftTween = unit.Model.DOLocalMoveY(0, 0.2f);
            MapVisual.Instance.RemoveHighlight();
            MapVisual.Instance.RemoveNotAvailable();
            PickEnded?.Invoke(unit);

            if (overSell) {
                overSell = false;
                GameManager.Instance.Session.Submit(new SellUnitCommand { UnitId = unit.UnitId }, result => {
                    if (!result.Ok) unit.SnapBack();
                });
                return;
            }
            if (nextLocation == null || nextLocation.Value == unit.Location) {
                unit.SnapBack();
                return;
            }
            var target = nextLocation.Value;
            GameManager.Instance.Session.Submit(new MoveUnitCommand { UnitId = unit.UnitId, Target = target }, result => {
                // On success the roster view moves every affected unit from the UnitMoved events.
                if (!result.Ok) unit.SnapBack();
            });
        }

        void InterruptPicking() {
            isPicking = false;
            pointerDown = false;
            overSell = false;
            PickEnded?.Invoke(unit);
            liftTween?.Kill();
            unit.Model.localPosition = Vector3.zero;
            MapVisual.Instance.RemoveHighlight();
            MapVisual.Instance.RemoveNotAvailable();
            unit.PlaceAt(unit.Location, false);
        }
    }
}
