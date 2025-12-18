using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

public class BattleField : MonoBehaviour {
    [SerializeField] BattleHero heroPrefab;
    
    Dictionary<TeamSide, List<BattleHero>> aliveHeroes = new() {
        { TeamSide.Ally, new List<BattleHero>() },
        { TeamSide.Enemy, new List<BattleHero>() }
    };
    
    Dictionary<TeamSide, List<BattleHero>> deadHeroes = new() {
        { TeamSide.Ally, new List<BattleHero>() },
        { TeamSide.Enemy, new List<BattleHero>() }
    };

    Queue<BattleHero> heroPool = new();
    Dictionary<Hero, MapNode> occupiedNodes = new();
    
    public void SpawnHeroes() {
        var enemies = GameManager.Instance.Progress.GetEnemies();
        foreach (var e in enemies) {
            var trait = HeroTraitDB.Instance.Find(e.heroId);
            var node = Map.Instance.GetNode(e.gridPosition);
            var hero = SpawnHero(trait, e.rank, TeamSide.Enemy, node);
            hero.GetAbility<HeroAttributes>().SetPowerScales(e.hpScale, e.damageScale);
            hero.GetAbility<HeroRotation>().Rotate(-Vector3.forward, true);
            var inventory = hero.GetAbility<HeroInventory>();
            foreach (var item in e.items)
            {
                inventory.Add(item);
            }
        }
                
        var allies = GameManager.Instance.LineUp.GetHeroOnMap();
        if (allies.Count == 0) {
            GameManager.Instance.Progress.EndBattlePhase(MatchResult.Lose);
        }
        else {
            foreach (var (h,n) in allies) {
                var hero = SpawnHero(h.Trait, h.Rank, TeamSide.Ally, n);
                var items = h.GetAbility<HeroInventory>().Get();
                var inventory = hero.GetAbility<HeroInventory>();
                foreach (var item in items) {
                    inventory.Add(item);
                }
                hero.GetAbility<HeroRotation>().Rotate(Vector3.forward, true);
            }
        }
    }

    BattleHero SpawnHero(HeroTrait trait, HeroRank rank, TeamSide side, MapNode node) {
        if (!heroPool.TryDequeue(out var hero)) {
            hero = Instantiate(heroPrefab);
        }
        hero.Activate();
        hero.WorldPosition = node.WorldPosition;
        hero.SetData(trait, rank, side);
        hero.SwitchCanvas(false);
        aliveHeroes[side].Add(hero);
        UpdateOccupiedNode(hero, node);
        return hero;
    }

    public List<BattleHero> GetAliveHeroes(TeamSide side) {
        return aliveHeroes[side];
    }

    public BattleHero GetNearestOpponent(BattleHero hero) {
        var opponents = aliveHeroes[hero.Side == TeamSide.Ally ? TeamSide.Enemy : TeamSide.Ally];
        BattleHero nearestHero = null;
        var minDist = Mathf.Infinity;
        foreach (var h in opponents) {
            var dist = Vector3.Distance(hero.transform.position, h.transform.position);
            if (dist < minDist) {
                nearestHero = h;
                minDist = dist;
            }
        }

        return nearestHero;
    }
    
    public BattleHero GetFurthestOpponent(BattleHero hero) {
        var opponents = aliveHeroes[hero.Side == TeamSide.Ally ? TeamSide.Enemy : TeamSide.Ally];
        BattleHero furthestHero = null;
        var minDist = Mathf.NegativeInfinity;
        foreach (var h in opponents) {
            var dist = Vector3.Distance(hero.transform.position, h.transform.position);
            if (dist > minDist) {
                furthestHero = h;
                minDist = dist;
            }
        }

        return furthestHero;
    }

    public BattleHero GetRandomOpponent(BattleHero hero) {
        var opponents = aliveHeroes[hero.Side == TeamSide.Ally ? TeamSide.Enemy : TeamSide.Ally];
        return opponents.Count == 0 ? null : opponents[Random.Range(0, opponents.Count)];
    }

    public void MarkHeroAsDead(BattleHero hero) {
        if (aliveHeroes[hero.Side].Remove(hero)) {
            deadHeroes[hero.Side].Add(hero);
            UpdateOccupiedNode(hero, null);
            
            if (aliveHeroes[TeamSide.Ally].Count == 0) {
                GameManager.Instance.Progress.EndBattlePhase(MatchResult.Lose);
            }
            else if (aliveHeroes[TeamSide.Enemy].Count == 0) {
                GameManager.Instance.Progress.EndBattlePhase(MatchResult.Win);
            }
        }

    }

    public void UpdateOccupiedNode(BattleHero hero, MapNode node) {
        if (node != null) {
            node.ChangeState(NodeState.Occupied);
            MapVisual.Instance.SetOccupied(node, true);
            occupiedNodes.Add(hero, node);
        }
        else {
            if (occupiedNodes.ContainsKey(hero)) {
                occupiedNodes[hero].SetToEmpty();
                MapVisual.Instance.SetOccupied(occupiedNodes[hero], false);
                occupiedNodes.Remove(hero);
            }
        }
    }

    public MapNode GetOccupiedNode(BattleHero hero) {
        return occupiedNodes.GetValueOrDefault(hero);
    }

    public void ActivateHeroes() {
        foreach (var (_, heroes) in aliveHeroes) {
            foreach (var h in heroes) {
                h.SetBehaviour(new HeroBT(h));
                h.SwitchCanvas(true);
            }
        }
    }

    public void DeactivateHeroes() {
        foreach (var (_, heroes) in aliveHeroes) {
            foreach (var h in heroes) {
                h.SetBehaviour(null);
            }
        }
        foreach (var (_, heroes) in deadHeroes) {
            foreach (var h in heroes) {
                h.SetBehaviour(null);
            }
        }
    }

    public void RemoveHeroes() {
        foreach (var (_, heroes) in aliveHeroes) {
            foreach (var h in heroes) {
                heroPool.Enqueue(h);
                UpdateOccupiedNode(h, null);
                h.Deactivate();
            }
            heroes.Clear();
        }
        
        foreach (var (_, heroes) in deadHeroes) {
            foreach (var h in heroes) {
                heroPool.Enqueue(h);
                h.Deactivate();
            }
            heroes.Clear();
        }
    }

    public void PlayHeroesDiveOutAnimation() {
        var allies = aliveHeroes[TeamSide.Ally];
        foreach (var h in allies) {
            h.Mecanim.DiveOut();
        }
        DOVirtual.DelayedCall(GameConfigs.PORTAL_CLOSE_DELAY, () => {
            MapVisual.Instance.SwitchPortal(false);
        });
    }
}