using System;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "Stage", menuName = "Stage")]
public class Stage : ScriptableObject {
    public Match[] matches;
}

[Serializable]
public class Match {
    [TableList] public Enemy[] enemies;
    [TableList] public Reward[] rewards;
    [TableList] public Reward[] winRewards;
}

[Serializable]
public class Enemy {
    [StringDropdown(typeof(HeroId)), VerticalGroup("Id")] public string heroId;
    [VerticalGroup("Id")] public HeroRank rank;
    [VerticalGroup("Detail")] public Vector2Int gridPosition;
    [VerticalGroup("Detail")] public Item[] items;
    [VerticalGroup("Detail")] public float hpScale = 1;
    [VerticalGroup("Detail")] public float damageScale = 1;
}

public class RewardType {
    public const string Coin = "coin";
    public const string RandomCoin = "random_coin";
    public const string Hero = "hero";
    public const string RawItem = "raw_item";
    public const string ForgedItem = "forged_item";
    public const string RandomReward = "random_reward";
}

[Serializable]
public class Reward {
    [StringDropdown(typeof(RewardType)), VerticalGroup("Type"), TableColumnWidth(150,false)] 
    public string type;

    [ShowIf("@type == RewardType.Coin"), VerticalGroup("Detail")] 
    public int coins;
    
    [ShowIf("@type == RewardType.RandomCoin"), VerticalGroup("Detail")] 
    public int minCoins;
    
    [ShowIf("@type == RewardType.RandomCoin"), VerticalGroup("Detail")] 
    public int maxCoins;
    
    [ShowIf("@type == RewardType.Hero"), VerticalGroup("Detail")] 
    public Reputation reputation;
    
    [ShowIf("@type == RewardType.RandomReward"), VerticalGroup("Detail"), TableList] 
    public RandomReward[] randomRewards;
}

[Serializable]
public class RandomReward {
    public Reward reward;
    [TableColumnWidth(50,false)]
    public int rate;
}