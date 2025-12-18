using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using RExt.Extensions;
using RExt.Patterns.Singleton;
using Sirenix.OdinInspector;
using UnityEngine;

public class MapVisual : Singleton<MapVisual> {
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
    HexCell[,] hexCells;
    [SerializeField, ReadOnly] int deckSize;
    SquareCell[] squareCells;
    
    Cell highlightCell;
    Cell notAvailableCell;
    
    Tween portalTween;
    
    const float PORTAL_ANIMATION_DURATION = 0.5f;

    public void Initialize() {
        Map.Instance.SpawnNodes(SpawnHexCells);
        Deck.Instance.SpawnNodes(SpawnSquareCells);
        portal.SetActive(false);
    }

    void SpawnHexCells(MapNode[,] nodes, float width, float height) {
        mapRow = nodes.GetLength(0);
        mapColumn = nodes.GetLength(1);
        hexCells = new HexCell[mapRow, mapColumn];
        for (int i = 0; i < mapRow; i++) {
            for (int j = 0; j < mapColumn; j++) {
                var cell = Instantiate(hexCell, hexParent);
                cell.transform.localPosition = new Vector3(nodes[i, j].WorldPosition.x, 0, nodes[i, j].WorldPosition.z);
                cell.transform.localScale = new Vector3(width, 1, height);
                cell.name = $"Hex[{i},{j}]";
                cell.dev_SaveIndex(i,j);
                hexCells[i, j] = cell;
            }
        }
    }

    void SpawnSquareCells(DeckNode[] nodes, float width, float height) {
        deckSize = nodes.Length;
        squareCells = new SquareCell[deckSize];
        for (int i = 0; i < deckSize; i++) {
            var cell = Instantiate(squareCell, squareParent);
            cell.transform.localPosition = new Vector3(nodes[i].WorldPosition.x, 0, nodes[i].WorldPosition.z);
            cell.transform.localScale = new Vector3(width, 1, height);
            cell.name = $"Square[{i}]";
            cell.dev_SaveIndex(i);
            squareCells[i] = cell;
        }
    }

    public void SetOccupied(Node node, bool occupied) {
        if (node is MapNode mNode) {
            hexCells[mNode.X, mNode.Y].SetOccupied(occupied);
        }
        else if (node is DeckNode dNode) {
            squareCells[dNode.LinePosition].SetOccupied(occupied);
        }
    }

    public void SetNotAvailable(Node node) {
        RemoveNotAvailable();
        switch (node) {
            case MapNode mn:
                notAvailableCell = hexCells[mn.X, mn.Y];
                notAvailableCell.SetNotAvailable(true);
                break;
            
            case DeckNode dn:
                notAvailableCell = squareCells[dn.LinePosition];
                notAvailableCell.SetNotAvailable(true);
                break;
        }
    }

    public void RemoveNotAvailable() {
        notAvailableCell?.SetNotAvailable(false);
        notAvailableCell = null;
    }

    public void SetHighlight(Node node) {
        RemoveHighlight();
        switch (node) {
            case MapNode mn:
                highlightCell = hexCells[mn.X, mn.Y];
                highlightCell.SetHighlight(true);
                break;
            
            case DeckNode dn:
                highlightCell = squareCells[dn.LinePosition];
                highlightCell.SetHighlight(true);
                break;
        }
    }

    public void RemoveHighlight() {
        highlightCell?.SetHighlight(false);
        highlightCell = null;
    }

    [Button]
    void TurnOnLabels() {
        for (int i=0; i<mapRow; i++) {
            for (int j=0; j<mapColumn; j++) {
                hexCells[i, j].dev_SwitchLabel(true);
            }
        }
        for (int i=0;i<deckSize;i++) {
            squareCells[i].dev_SwitchLabel(true);
        }
    }
    
    [Button]
    void TurnOffLabels() {
        for (int i=0; i<mapRow; i++) {
            for (int j=0; j<mapColumn; j++) {
                hexCells[i, j].dev_SwitchLabel(false);
            }
        }
        for (int i=0;i<deckSize;i++) {
            squareCells[i].dev_SwitchLabel(false);
        }
    }

    public HexCell GetHexCell(int x, int y) {
        return hexCells[x, y];
    }

    public void SetHalfMapVisibility(bool value) {
        for (int i=mapRow/2; i<mapRow; i++) {
            for (int j=0; j<mapColumn; j++) {
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
            portalTween = portal.transform.DOScale(0, PORTAL_ANIMATION_DURATION).OnComplete(() => {
                portal.SetActive(false);
            });
        }
    }
}