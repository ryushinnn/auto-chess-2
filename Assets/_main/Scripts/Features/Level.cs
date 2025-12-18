using System;
using Sirenix.OdinInspector;
using UnityEngine;

public class Level : MonoBehaviour {
    public event Action<int> OnLevelUp;
    public LevelConfig LevelConfig { get; private set; }
    
    [SerializeField, ReadOnly] int level;
    [SerializeField, ReadOnly] int xp;
    
    public void Initialize() {
        level = 1;
        xp = 0;
        LevelConfig = GameConfigs.LEVEL_CONFIGS[0];
        UIManager_Arena.Instance.Arena.UpdateLevelText(1);
        UIManager_Arena.Instance.Arena.UpdateXpText(0, LevelConfig.xpToNextLevel, false);
    }
    
    [Button]
    public bool GainXp(int amount) {
        if (MaxLevel()) return false;
        
        xp += amount;
        UIManager_Arena.Instance.Arena.UpdateXpText(xp, LevelConfig.xpToNextLevel, false);
        while (xp >= LevelConfig.xpToNextLevel && !MaxLevel()) {
            LevelUp();
        }
        return true;
    }
    
    void LevelUp() {
        level++;
        xp -= LevelConfig.xpToNextLevel;
        LevelConfig = GameConfigs.LEVEL_CONFIGS[level - 1];
        UIManager_Arena.Instance.Arena.UpdateLevelText(level);
        UIManager_Arena.Instance.Arena.UpdateXpText(xp, LevelConfig.xpToNextLevel, MaxLevel());
        GameManager.Instance.LineUp.SetHeroesLimit(LevelConfig.maxHeroesOnMap);
        OnLevelUp?.Invoke(level);
    }

    bool MaxLevel() {
        return level == GameConfigs.LEVEL_CONFIGS.Length;
    }

    [Button]
    void dev_nextLevel() {
        if (MaxLevel()) return;
        GainXp(LevelConfig.xpToNextLevel - xp);
    }
}