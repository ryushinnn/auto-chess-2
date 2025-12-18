using System;
using System.Collections.Generic;
using RExt.Patterns.Singleton;
using Sirenix.OdinInspector;
using UnityEngine;

public class GameManager : Singleton<GameManager> {
    public Shop Shop => shop;
    public LineUp LineUp => lineUp;
    public Destinies Destinies => destinies;
    public Inventory Inventory => inventory;
    public BattleField BattleField => battleField;
    public Level Level => level;
    public Progress Progress => progress;
    
    [SerializeField] Shop shop;
    [SerializeField] LineUp lineUp;
    [SerializeField] Destinies destinies;
    [SerializeField] Inventory inventory;
    [SerializeField] BattleField battleField;
    [SerializeField] Level level;
    [SerializeField] Progress progress;
    
    void Start() {
        MapVisual.Instance.Initialize();
        level.Initialize();
        destinies.Initialize();
        lineUp.Initialize();
        inventory.Initialize();
    }

    public void StartGame() {
        progress.Initialize();
    }
}