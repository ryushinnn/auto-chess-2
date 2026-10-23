using AutoChess.Core;
using DG.Tweening;
using RExt.Extensions;
using RExt.Patterns.Singleton;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Board and bench tiles: occupied (blue), drop preview (green / red), the enemy half and the portal.
    /// Occupancy is pulled from the session every frame, so it can never drift from the game state.
    /// </summary>
    public class MapVisual : Singleton<MapVisual>, ISessionResettable {
        public int Layer => raycastTarget.layer;
        public Vector3 PortalPosition => portal.transform.position.ToZeroY();

        [SerializeField] Transform hexParent;
        [SerializeField] HexCell hexCell;
        [SerializeField] Transform squareParent;
        [SerializeField] SquareCell squareCell;
        [SerializeField] GameObject raycastTarget;
        [SerializeField] GameObject portal;

        [SerializeField, ReadOnly] int mapRow;
        [SerializeField, ReadOnly] int mapColumn;
        [SerializeField, ReadOnly] int deckSize;
        HexCell[,] hexCells;
        SquareCell[] squareCells;

        Cell highlightCell;
        Cell notAvailableCell;
        Tween portalTween;
        bool halfMapVisible = true;

        const float PORTAL_ANIMATION_DURATION = 0.5f;

        public bool Initialized => hexCells != null;

        public void Initialize(BoardSpace board, BenchSpace bench) {
            var grid = board.Grid;
            mapRow = grid.Rows;
            mapColumn = grid.Cols;
            hexCells = new HexCell[mapRow, mapColumn];
            for (int i = 0; i < mapRow; i++) {
                for (int j = 0; j < mapColumn; j++) {
                    var position = board.CellToWorld(new HexCoord(i, j));
                    var cell = Instantiate(hexCell, hexParent);
                    cell.transform.localPosition = new Vector3(position.x, 0, position.z);
                    cell.transform.localScale = new Vector3(board.CellWidth, 1, board.CellHeight);
                    cell.name = $"Hex[{i},{j}]";
                    cell.dev_SaveIndex(i, j);
                    hexCells[i, j] = cell;
                }
            }

            deckSize = bench.Size;
            squareCells = new SquareCell[deckSize];
            for (int i = 0; i < deckSize; i++) {
                var position = bench.SlotToWorld(i);
                var cell = Instantiate(squareCell, squareParent);
                cell.transform.localPosition = new Vector3(position.x, 0, position.z);
                cell.transform.localScale = new Vector3(bench.SlotWidth, 1, bench.SlotHeight);
                cell.name = $"Square[{i}]";
                cell.dev_SaveIndex(i);
                squareCells[i] = cell;
            }

            portal.SetActive(false);
        }

        public void OnSessionReset() {
            RemoveHighlight();
            RemoveNotAvailable();
            portalTween?.Kill();
            portal.SetActive(false);
            SetHalfMapVisibility(true);
        }

        void LateUpdate() {
            if (!Initialized) return;
            var game = GameManager.Instance;
            if (game == null || !game.IsReady) return;
            RefreshOccupancy(game);
        }

        void RefreshOccupancy(GameManager game) {
            var player = game.ViewedPlayer;
            var battle = game.Battle.VisibleBattle;
            var flipped = BoardSpace.BattleFlipped;
            var roster = game.Roster;
            var grid = game.Db.Grid;
            for (int i = 0; i < mapRow; i++) {
                for (int j = 0; j < mapColumn; j++) {
                    var index = grid.IndexOf(new HexCoord(i, j));
                    bool occupied;
                    if (battle != null) occupied = battle.IsCellOccupied(flipped ? grid.IndexOf(new HexCoord(i, j).Mirror(grid.Rows, grid.Cols)) : index);
                    else occupied = roster.BoardVisible && player.Board[index] != null;
                    hexCells[i, j].SetOccupied(occupied);
                }
            }
            for (int i = 0; i < deckSize; i++) {
                squareCells[i].SetOccupied(i < player.Bench.Length && player.Bench[i] != null);
            }
        }

        Cell CellAt(UnitLocation location) {
            switch (location.Kind) {
                case LocationKind.Board:
                    var c = location.Cell;
                    return c.Row >= 0 && c.Row < mapRow && c.Col >= 0 && c.Col < mapColumn ? hexCells[c.Row, c.Col] : null;
                case LocationKind.Bench:
                    return location.Slot >= 0 && location.Slot < deckSize ? squareCells[location.Slot] : null;
                default:
                    return null;
            }
        }

        public Vector3 LocationToWorld(UnitLocation location) {
            return location.Kind == LocationKind.Bench
                ? BenchSpace.Instance.SlotToWorld(location.Slot)
                : BoardSpace.Instance.CellToWorld(location.Cell);
        }

        public void SetNotAvailable(UnitLocation location) {
            RemoveNotAvailable();
            notAvailableCell = CellAt(location);
            notAvailableCell?.SetNotAvailable(true);
        }

        public void RemoveNotAvailable() {
            if (notAvailableCell != null) notAvailableCell.SetNotAvailable(false);
            notAvailableCell = null;
        }

        public void SetHighlight(UnitLocation location) {
            RemoveHighlight();
            highlightCell = CellAt(location);
            highlightCell?.SetHighlight(true);
        }

        public void RemoveHighlight() {
            if (highlightCell != null) highlightCell.SetHighlight(false);
            highlightCell = null;
        }

        [Button]
        void TurnOnLabels() => SwitchLabels(true);

        [Button]
        void TurnOffLabels() => SwitchLabels(false);

        void SwitchLabels(bool on) {
            if (!Initialized) return;
            foreach (var cell in hexCells) cell.dev_SwitchLabel(on);
            foreach (var cell in squareCells) cell.dev_SwitchLabel(on);
        }

        public HexCell GetHexCell(int row, int col) => hexCells[row, col];
        public SquareCell GetSquareCell(int slot) => squareCells[slot];

        /// <summary>Shows or hides the enemy half of the board.</summary>
        public void SetHalfMapVisibility(bool value) {
            if (halfMapVisible == value && Initialized) return;
            halfMapVisible = value;
            for (int i = mapRow / 2; i < mapRow; i++) {
                for (int j = 0; j < mapColumn; j++) {
                    hexCells[i, j].gameObject.SetActive(value);
                }
            }
        }

        public void SwitchPortal(bool value) {
            portalTween?.Kill();
            if (value) {
                portal.SetActive(true);
                portal.transform.SetUniformScale(0);
                portalTween = portal.transform.DOScale(1, PORTAL_ANIMATION_DURATION);
            }
            else {
                portalTween = portal.transform.DOScale(0, PORTAL_ANIMATION_DURATION).OnComplete(() => portal.SetActive(false));
            }
        }
    }
}
