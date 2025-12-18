using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

public enum MatchPhase {
    None,
    Preparation,
    Transition,
    Battle,
    Summary,
}

public enum MatchResult {
    None,
    Win,
    Lose,
}

public class Progress : MonoBehaviour {
    public MatchPhase Phase => phase;
    
    [SerializeField] Stage[] stages;

    [SerializeField, ReadOnly] MatchPhase phase;
    float timeLeft;
    float totalTime;
    int currentStage;
    int currentMatch;
    List<Reward> rewards = new();

    Dictionary<MatchPhase, MatchPhase> nexts = new() {
        { MatchPhase.None, MatchPhase.Preparation},
        { MatchPhase.Preparation, MatchPhase.Transition },
        { MatchPhase.Transition, MatchPhase.Battle },
        { MatchPhase.Battle, MatchPhase.Summary },
        { MatchPhase.Summary, MatchPhase.Preparation }
    };

    void Update() {
        CountDown();
        
        //test
        if (Input.GetKeyDown(KeyCode.Space)) {
            if (phase == MatchPhase.Preparation || phase == MatchPhase.Battle) {
                timeLeft = 1;
            }
        }
    }
    
    public void Initialize() {
        ChangePhase();
    }

    void CountDown() {
        if (phase == MatchPhase.None) return;
        
        if (timeLeft > 0) {
            timeLeft -= Time.deltaTime;
            UIManager_Arena.Instance.Arena.UpdateTimeLeft(timeLeft, totalTime);
        }
        else {
            ChangePhase();
        }
    }

    void ChangePhase() {
        var nextPhase = nexts[phase];
        switch (nextPhase) {
            case MatchPhase.Preparation:
                if (phase != MatchPhase.None) {
                    currentMatch++;
                    if (currentMatch >= stages[currentStage].matches.Length) {
                        currentMatch = 0;
                        currentStage++;
                        if (currentStage >= stages.Length) {
                            phase = MatchPhase.None;
                            break;
                        }
                    }
                    GameManager.Instance.Level.GainXp(GameConfigs.XP_GAIN_PER_MATCH);
                }

                phase = nextPhase;
                timeLeft = totalTime = GameConfigs.MATCH_PHASE_DURATIONS[phase];
                UIManager_Arena.Instance.Arena.UpdateStageAndMatch(currentStage, currentMatch);
                GameManager.Instance.BattleField.RemoveHeroes();
                GameManager.Instance.LineUp.ShowHeroesOnMap();
                GameManager.Instance.LineUp.SetHeroesPickable(true);
                foreach (var r in rewards) {
                    ClaimReward(r);
                }
                MapVisual.Instance.SetHalfMapVisibility(false);
                GameManager.Instance.Shop.AutoRefresh();
                break;
            
            case MatchPhase.Transition:
                phase = nextPhase;
                timeLeft = totalTime = GameConfigs.MATCH_PHASE_DURATIONS[phase];
                GameManager.Instance.LineUp.SetHeroesPickable(false);
                GameManager.Instance.LineUp.FillHeroesOnMap();
                GameManager.Instance.LineUp.PlayHeroesDiveInAnimation();
                DOVirtual.DelayedCall(GameConfigs.SPAWN_ENEMIES_DELAY, () => {
                    GameManager.Instance.LineUp.HideHeroesOnMap();
                    GameManager.Instance.BattleField.SpawnHeroes();
                    GameManager.Instance.BattleField.PlayHeroesDiveOutAnimation();
                    MapVisual.Instance.SetHalfMapVisibility(true);
                });
                break;
            
            case MatchPhase.Battle:
                phase = nextPhase;
                timeLeft = totalTime = GameConfigs.MATCH_PHASE_DURATIONS[phase];
                GameManager.Instance.Destinies.Activate();
                GameManager.Instance.BattleField.ActivateHeroes();
                break;
            
            case MatchPhase.Summary:
                Debug.Log(MatchResult.Lose);
                phase = nextPhase;
                timeLeft = totalTime = GameConfigs.MATCH_PHASE_DURATIONS[phase];
                GameManager.Instance.BattleField.DeactivateHeroes();
                rewards = stages[currentStage].matches[currentMatch].rewards.ToList();
                break;
        }
    }

    public Enemy[] GetEnemies() {
        return stages[currentStage].matches[currentMatch].enemies;
    }

    public void EndBattlePhase(MatchResult result) {
        if (phase != MatchPhase.Battle) return;
        Debug.Log(result);
        phase = MatchPhase.Summary;
        timeLeft = totalTime = GameConfigs.MATCH_PHASE_DURATIONS[phase];
        GameManager.Instance.BattleField.DeactivateHeroes();
        rewards = stages[currentStage].matches[currentMatch].rewards.ToList();
        if (result == MatchResult.Win) {
            rewards.AddRange(stages[currentStage].matches[currentMatch].winRewards);
        }
    }

    void ClaimReward(Reward reward) {
        switch (reward.type) {
            case RewardType.Coin:
                GameManager.Instance.Inventory.GainCoins(reward.coins);
                break;
            
            case RewardType.RandomCoin:
                var coins = Random.Range(reward.minCoins, reward.maxCoins + 1);
                GameManager.Instance.Inventory.GainCoins(coins);
                break;
            
            case RewardType.Hero:
                var matchedHeroes = HeroTraitDB.Instance.FindAll(e => e.reputation == reward.reputation && !e.summoned);
                var randomHero = matchedHeroes[Random.Range(0, matchedHeroes.Count)];
                GameManager.Instance.LineUp.Add(randomHero);
                break;
            
            case RewardType.RawItem:
                var rItem = ItemDB.Instance.GetRandomRawItem();
                GameManager.Instance.Inventory.AddItem(rItem);
                break;
            
            case RewardType.ForgedItem:
                var fItem = ItemDB.Instance.GetRandomForgedItem();
                GameManager.Instance.Inventory.AddItem(fItem);
                break;
            
            case RewardType.RandomReward:
                var random = Random.value;
                var totalRate = 0f;
                var rs = reward.randomRewards;
                Reward randomReward = null;
                foreach (var r in rs) {
                    totalRate += r.rate / 100f;
                    if (random <= totalRate) {
                        randomReward = r.reward;
                        break;
                    }
                }

                randomReward ??= rs[^1].reward;
                ClaimReward(randomReward);
                break;
        }
    }
}