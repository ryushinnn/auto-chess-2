using System;
using System.Collections.Generic;
using DG.Tweening;
using RExt.Extensions;
using UnityEngine;

public class HeroMovement : HeroAbility {
    public bool ReachedDestination => reachedDestination;

    HeroRotation rotation;
    HeroAttributes attributes;
    HeroStatusEffects statusEffects;
    
    Sequence moveSequence;
    MapNode currentDestination;
    MapNode currentTargetNode;
    MapNode nextNode;
    bool reachedDestination;
    
    public override void ResetAll() {
        moveSequence?.Kill();
        currentDestination = null;
    }

    protected override void FindReferences() {
        rotation = hero.GetAbility<HeroRotation>();
        attributes = hero.GetAbility<HeroAttributes>();
        statusEffects = hero.GetAbility<HeroStatusEffects>();
    }
    
    public void StartMove() {
        if (statusEffects.IsStun || statusEffects.IsAirborne) return;
        
        var targetNode = Map.Instance.GetNearestNode(((BattleHero)hero).Target.WorldPosition);
        
        // if target not move, no need to calculate new destination
        if (targetNode == currentTargetNode) return;
        
        currentTargetNode = targetNode;
        // if target in range, no need to calculate new destination
        var occupiedNode = GameManager.Instance.BattleField.GetOccupiedNode((BattleHero)hero);
        if ((occupiedNode != null && Map.Instance.CheckAdjacency(occupiedNode, currentTargetNode, hero.Trait.attackRange))) {
            if (!reachedDestination) reachedDestination = true;
            return;
        }
        if (nextNode != null && Map.Instance.CheckAdjacency(nextNode, currentTargetNode, hero.Trait.attackRange)) return;

        var myNode = Map.Instance.GetNearestNode(hero.WorldPosition);
        var destination = Map.Instance.GetNeighbors(currentTargetNode, hero.Trait.attackRange).Filter(x => x.IsEmpty()).GetNearestFrom(myNode)
                      ?? Map.Instance.GetNearestNode(currentTargetNode, x => x.IsEmpty());
        
        // if destination not changed, no need to calculate new path
        if (destination == currentDestination) return;

        // update destination, calculate new path
        GameManager.Instance.BattleField.UpdateOccupiedNode((BattleHero)hero, null);
        currentDestination?.SetToEmpty();
        currentDestination = destination;
        currentDestination.ChangeState(NodeState.Targeted);
        
        hero.Mecanim.Run();
        reachedDestination = false;
        var path = Map.Instance.FindPath(myNode, currentDestination);
        moveSequence?.Kill();
        moveSequence = DOTween.Sequence();
        if (path.Count > 1) {
            // path has at least 2 nodes, move self -> node 1 -> ... -> node N (ignore node 0)
            for (int i = 1; i < path.Count; i++) {
                var node = path[i];
                moveSequence.AppendCallback(() => {
                    nextNode = node;
                    rotation.Rotate(nextNode.WorldPosition - hero.WorldPosition);
                });

                var dist = Vector3.Distance(node.WorldPosition, hero.WorldPosition);
                var time = 1 / attributes.MovementSpeed * (dist / Map.Instance.GetAverageNodeDistance());
                moveSequence.Append(hero.transform.DOMove(node.WorldPosition, time).SetEase(Ease.Linear))
                .AppendCallback(() => {
                    // check if target in range BEFORE reach destination and standing on empty node, can early cancel move
                    var targetInRange = Map.Instance.CheckAdjacency(Map.Instance.GetNearestNode(((BattleHero)hero).Target.WorldPosition), node, hero.Trait.attackRange);
                    var isNotDestination = node != currentDestination;
                    var standingOnEmpty = node.IsEmpty();
                    if (!targetInRange || !isNotDestination || !standingOnEmpty) return;
                    
                    rotation.Rotate(((BattleHero)hero).Target.WorldPosition - hero.WorldPosition);
                    
                    // only reset it if it is targeted (by self)
                    if (currentDestination.State == NodeState.Targeted) {
                        currentDestination.SetToEmpty();
                    }
                    GameManager.Instance.BattleField.UpdateOccupiedNode((BattleHero)hero, node);
                    currentDestination = null;
                    nextNode = null;
                    
                    StopMove();
                    reachedDestination = true;
                });
            }
        }
        else if (path.Count == 1) {
            // path has only 1 node (destination). if the distance is far enough, move self -> node 0, else skip 
            var node = path[0];
            var dist = Vector3.Distance(node.WorldPosition, hero.WorldPosition);
            if (dist > 0.01f) {
                moveSequence.AppendCallback(() => {
                    nextNode = node;
                    rotation.Rotate(nextNode.WorldPosition - hero.WorldPosition);
                });

                var time = 1 / attributes.MovementSpeed * (dist / Map.Instance.GetAverageNodeDistance());
                moveSequence.Append(hero.transform.DOMove(node.WorldPosition, time).SetEase(Ease.Linear));
            }
        }

        moveSequence.AppendCallback(() => {
            rotation.Rotate(((BattleHero)hero).Target.WorldPosition - hero.WorldPosition);
            
            GameManager.Instance.BattleField.UpdateOccupiedNode((BattleHero)hero, currentDestination);
            currentDestination = null;
            nextNode = null;
            
            StopMove();
            reachedDestination = true;
        });
    }

    public void StopMove(bool reset = false) {
        hero.Mecanim.Idle();
        moveSequence?.Kill();

        if (reset) {
            currentTargetNode = null;
            currentDestination?.SetToEmpty();
            currentDestination = null;
            nextNode = null;
        }
    }
}