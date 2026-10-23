using System;
using System.Collections.Generic;
using AutoChess.Core;
using RExt.Patterns.Singleton;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>A scene component that draws or drives part of the game; bound once the session exists.</summary>
    public interface ISessionView {
        void Bind(GameManager game);
    }

    /// <summary>A view that keeps state of its own and must drop it when a new match starts.</summary>
    public interface ISessionResettable {
        /// <summary>Called after a restart, right before the new session starts (and raises its first events).</summary>
        void OnSessionReset();
    }

    [Serializable]
    public class PresentationSettings {
        [Tooltip("Seconds into the transition before the battle units come out of the portal.")]
        public float spawnEnemiesDelay = 2f;
        [Tooltip("Seconds after the battle units appeared before the portal closes.")]
        public float portalCloseDelay = 1f;
        [Tooltip("Model scale per rank (B, A, S).")]
        public float[] rankScale = { 1f, 1.15f, 1.4f };

        public float ScaleOf(HeroRank rank) => rankScale != null && (int)rank < rankScale.Length ? rankScale[(int)rank] : 1f;
    }

    /// <summary>
    /// Composition root of the Arena scene (was the feature hub). Builds the game data from the
    /// ScriptableObjects, starts a <see cref="LocalGameSession"/> running the engine-free simulation, binds
    /// every <see cref="ISessionView"/> and feeds the session with frame time. All gameplay changes go through
    /// <see cref="Submit"/>; views listen to <see cref="EventRaised"/>, which outlives sessions, so a restart (or
    /// swapping the local session for a networked one) leaves them untouched.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : Singleton<GameManager> {
        /// <summary>One-shot seed for the next session (PlayerPrefs, so it survives the editor's domain reload).</summary>
        public const string SeedOverrideKey = "AutoChess.SeedOverride";

        [SerializeField] RosterView lineUp;
        [SerializeField] BattleView battleField;
        [SerializeField] Stage[] stages;
        [SerializeField] PresentationSettings settings = new();
        [SerializeField] bool randomSeed = true;
        [SerializeField, HideIf(nameof(randomSeed))] int seed = 1;
        [Tooltip("Accept cheat commands (always on in the editor and development builds).")]
        [SerializeField] bool allowCheats;
        [SerializeField] Item[] hackItems;

        LocalGameSession session;
        GameDatabase database;
        readonly List<ISessionView> views = new();

        /// <summary>Events of the current session (subscriptions survive restarts).</summary>
        public event Action<GameEvent> EventRaised;

        public IGameSession Session => session;
        public LocalGameSession LocalSession => session;
        public GameDatabase Db => session?.Db;
        public PresentationSettings Settings => settings;
        public RosterView Roster => lineUp;
        public BattleView Battle => battleField;
        public PlayerState LocalPlayer => session?.LocalPlayer;
        public bool IsReady { get; private set; }
        public bool CheatsAllowed => allowCheats || Application.isEditor || Debug.isDebugBuild;
        public ulong Seed { get; private set; }

        void Start() {
            var board = BoardSpace.Instance;
            database = GameDataBuilder.Build(stages, board.CellWidth, board.CellHeight);
            CreateSession(ResolveSeed());

            board.Bind(database.Grid);
            BenchSpace.Instance.Bind(database.Rules.BenchSize);
            MapVisual.Instance.Initialize(board, BenchSpace.Instance);

            views.Clear();
            foreach (var behaviour in FindObjectsOfType<MonoBehaviour>(true)) {
                if (behaviour is ISessionView view) views.Add(view);
            }
            foreach (var view in views) view.Bind(this);

            IsReady = true;
            session.Start();
        }

        void CreateSession(ulong seed) {
            if (session != null) session.EventRaised -= Relay;
            Seed = seed;
            session = new LocalGameSession(database, new MatchConfig { Seed = seed, AllowCheats = CheatsAllowed });
            session.EventRaised += Relay;
        }

        void Relay(GameEvent e) => EventRaised?.Invoke(e);

        /// <summary>Throws the current match away and starts a new one in place (same scene, same views).</summary>
        public void Restart(ulong? seed = null) {
            if (!IsReady) return;
            CreateSession(seed ?? (ulong)DateTime.UtcNow.Ticks);
            foreach (var behaviour in FindObjectsOfType<MonoBehaviour>(true)) {
                if (behaviour is ISessionResettable resettable) resettable.OnSessionReset();
            }
            session.Start();
        }

        ulong ResolveSeed() {
            if (PlayerPrefs.HasKey(SeedOverrideKey)) {
                var forced = PlayerPrefs.GetInt(SeedOverrideKey);
                PlayerPrefs.DeleteKey(SeedOverrideKey);
                return (ulong)(uint)forced;
            }
            return randomSeed ? (ulong)DateTime.UtcNow.Ticks : (ulong)(uint)seed;
        }

        void Update() {
            if (!IsReady) return;
            if (Input.GetKeyDown(KeyCode.Space)) OnSpace();
            session.Advance(Time.deltaTime);
        }

        /// <summary>Space: ready up during preparation; skip the battle in development.</summary>
        void OnSpace() {
            switch (session.State.Phase) {
                case MatchPhase.Preparation:
                    Submit(new ReadyCommand());
                    break;
                case MatchPhase.Battle when CheatsAllowed:
                    Submit(new CheatSkipPhaseCommand());
                    break;
            }
        }

        public void Submit(GameCommand command, Action<CommandResult> onResult = null) {
            session.Submit(command, onResult);
        }

        // ---------------------------------------------------------------- development shortcuts

        [Button, FoldoutGroup("Dev")]
        void dev_AddGold(int amount = 1000) => Submit(new CheatGoldCommand { Amount = amount });

        [Button, FoldoutGroup("Dev")]
        void dev_NextLevel() {
            var p = LocalPlayer;
            var next = Db.Rules.Level(p.Level).XpToNext;
            if (next > 0) Submit(new CheatXpCommand { Amount = next - p.Xp });
        }

        [Button, FoldoutGroup("Dev")]
        void dev_AddHackItems() {
            foreach (var item in hackItems) {
                if (item != null) Submit(new CheatItemCommand { ItemId = item.Id, Count = 3 });
            }
        }

        [Button, FoldoutGroup("Dev")]
        void dev_SkipPhase() => Submit(new CheatSkipPhaseCommand());
    }
}
