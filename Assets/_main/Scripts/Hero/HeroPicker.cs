using System;
using System.Collections;
using DG.Tweening;
using RExt.Extensions;
using RExt.Utils;
using UnityEngine;

public class HeroPicker : MonoBehaviour {
    LineUpHero hero;
    LayerMask mapLayerMask;
    Tween tween;
    Node currentNode;
    Node nextNode;
    Vector3 offset;
    bool pickable;
    bool isPicking;
    Coroutine holdCoroutine;
    
    const float HOLD_TIME_THRESHOLD = 0.2f;
    const float DRAG_POS_Y = 1;

    public void Initialize(LineUpHero hero, LayerMask mapLayerMask, bool pickable) {
        this.hero = hero;
        this.mapLayerMask = mapLayerMask;
        this.pickable = pickable;
    }

    public void SwitchPickable(bool value) {
        pickable = value;
        if (!pickable) {
            if (holdCoroutine != null) StopCoroutine(holdCoroutine);
            if (isPicking) {
                InterruptPicking();
            }
        }
    }

    void OnMouseDown() {
        if (!pickable) return;
        holdCoroutine = StartCoroutine(DoPick());
    }

    void OnMouseDrag() {
        if (!pickable || !isPicking) return;
        HandlePicking();
    }

    void OnMouseUp() {
        if (!pickable) return;
        
        if (holdCoroutine != null) StopCoroutine(holdCoroutine);
        if (isPicking) {
            EndPicking();
        }
        else {
            UIManager_Arena.Instance.HeroInfo.Open(hero);
        }
    }

    IEnumerator DoPick() {
        yield return BetterWaitForSeconds.WaitRealtime(HOLD_TIME_THRESHOLD);
        StartPicking();
    }

    void StartPicking() {
        isPicking = true;
        
        tween?.Kill();
        tween = hero.Model.DOLocalMoveY(DRAG_POS_Y, 0.2f);
        var ray = Utils.MainCamera().ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, Mathf.Infinity, mapLayerMask) && hit.collider != null) {
            offset = new Vector3(hero.transform.position.x - hit.point.x, 0, hero.transform.position.z - hit.point.z);
        }
        currentNode = GameManager.Instance.LineUp.FindNodeOfHero(hero);
    }

    void HandlePicking() {
        var ray = Utils.MainCamera().ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, Mathf.Infinity, mapLayerMask) && hit.collider != null) {
            var point = hit.point.ToZeroY();
            hero.transform.position = point + offset;
            var selectedDeckNode = Deck.Instance.GetNearestNode(point);
            if (selectedDeckNode != null) {
                // move to deck is always available
                MapVisual.Instance.RemoveNotAvailable();
                MapVisual.Instance.SetHighlight(selectedDeckNode);
                nextNode = selectedDeckNode;
                return;
            }
            
            var selectedMapNode = Map.Instance.GetNearestNode(point, n => n.X < Map.SIZE / 2);
            if (selectedMapNode != null) {
                // move to self?
                // \---true---> ok
                //  \---false---> move/swap map -> map?
                //                \---true (self is map node)---> ok
                //                 \---false---> swap deck -> map?
                //                               \---true (selected node has a hero)---> ok
                //                                \---false---> move deck -> map?
                //                                              \---true (lineup not full)---> ok
                //                                               \---false---> not available
                if (selectedMapNode == currentNode 
                    || currentNode is MapNode
                    || !selectedMapNode.IsEmpty()
                    || !GameManager.Instance.LineUp.Full) {
                    
                    MapVisual.Instance.RemoveNotAvailable();
                    MapVisual.Instance.SetHighlight(selectedMapNode);
                }
                else {
                    MapVisual.Instance.RemoveHighlight();
                    MapVisual.Instance.SetNotAvailable(selectedMapNode);
                }
                nextNode = selectedMapNode;
                return;
            }

            MapVisual.Instance.RemoveHighlight();
            MapVisual.Instance.RemoveNotAvailable();
            nextNode = null;
        }
    }

    void EndPicking() {
        isPicking = false;
        
        tween?.Kill();
        tween = hero.Model.DOLocalMoveY(0, 0.2f);
        MapVisual.Instance.RemoveHighlight();
        MapVisual.Instance.RemoveNotAvailable();
        
        if (nextNode is DeckNode) {
            // case 0: move to self
            if (nextNode == currentNode) {
                GameManager.Instance.LineUp.UpdateHeroNode(hero, currentNode);
            }
            // case 1: move map/deck -> deck (not self)
            else if (nextNode.IsEmpty()) {
                GameManager.Instance.LineUp.UpdateHeroNode(hero, nextNode);
                GameManager.Instance.LineUp.RecalculateHeroesOnMap();
            }
            // case 2: swap
            else {
                var other = GameManager.Instance.LineUp.FindHeroOnNode(nextNode);
                // invalid, back to current node
                if (other == null) {
                    Debug.Log($"Something is wrong. node {nextNode} is occupied but no hero found");
                    GameManager.Instance.LineUp.UpdateHeroNode(hero, currentNode);
                }
                // valid, swap
                else {
                    GameManager.Instance.LineUp.SwapHeroNodes(hero, other);
                    GameManager.Instance.LineUp.RecalculateHeroesOnMap();
                }
            }
        }
        else if (nextNode is MapNode) {
            // case 0: move to self
            if (nextNode == currentNode) {
                GameManager.Instance.LineUp.UpdateHeroNode(hero, currentNode);
            }
            // case 1: move map -> map (not self)
            else if (currentNode is MapNode && nextNode.IsEmpty()) {
                GameManager.Instance.LineUp.UpdateHeroNode(hero, nextNode);
                GameManager.Instance.LineUp.RecalculateHeroesOnMap();
            }
            // case 2: move deck -> map
            else if (currentNode is DeckNode && nextNode.IsEmpty()) {
                // invalid, back to current node
                if (GameManager.Instance.LineUp.Full) {
                    GameManager.Instance.LineUp.UpdateHeroNode(hero, currentNode);
                }
                // valid, update node
                else {
                    GameManager.Instance.LineUp.UpdateHeroNode(hero, nextNode);
                    GameManager.Instance.LineUp.RecalculateHeroesOnMap();
                }
            }
            // case 3: swap
            else {
                var other = GameManager.Instance.LineUp.FindHeroOnNode(nextNode);
                // invalid, back to current node
                if (other == null) {
                    Debug.Log($"Something is wrong. node {nextNode} is occupied but no hero found");
                    GameManager.Instance.LineUp.UpdateHeroNode(hero, currentNode);
                }
                // valid, swap
                else {
                    GameManager.Instance.LineUp.SwapHeroNodes(hero, other);
                    GameManager.Instance.LineUp.RecalculateHeroesOnMap();
                }
            }
        }
    }

    void InterruptPicking() {
        isPicking = false;
        
        tween?.Kill();
        hero.Model.localPosition = Vector3.zero;
        MapVisual.Instance.RemoveHighlight();
        MapVisual.Instance.RemoveNotAvailable();
        hero.WorldPosition = currentNode.WorldPosition;
    }
}