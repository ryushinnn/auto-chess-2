using System.Collections.Generic;
using AutoChess.Core;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// Visual of a hero (was Hero): spawns the trait's animated model and owns the overhead parts (bars,
    /// items, marks...) found under <see cref="abilitiesContainer"/>. Holds no game rules; subclasses bind it
    /// to an owned unit (<see cref="RosterUnitView"/>) or to a battle unit (<see cref="BattleUnitView"/>).
    /// </summary>
    public class UnitView : MonoBehaviour {
        [SerializeField] protected Transform model;
        [SerializeField] protected Transform abilitiesContainer;
        [SerializeField] protected Canvas canvas;
        [SerializeField] protected Image rankIcon;

        [SerializeField, ReadOnly] protected HeroTrait trait;
        [SerializeField, ReadOnly] protected HeroRank rank;

        public HeroTrait Trait => trait;
        public HeroRank Rank => rank;
        public Transform Model => model;
        public Mecanim Mecanim => mecanim;
        public UnitStatusBars Bars { get; private set; }
        public UnitItemsView ItemsView { get; private set; }
        public UnitMarksView Marks { get; private set; }
        public UnitRotation Rotation { get; private set; }
        public UnitAirborneView AirborneView { get; private set; }

        public Vector3 WorldPosition {
            get => transform.position;
            set => transform.position = value.ToZeroY();
        }

        protected Mecanim mecanim;
        readonly List<UnitPart> parts = new();
        bool initialized;

        protected virtual void Awake() {
            EnsureInitialized();
        }

        void EnsureInitialized() {
            if (initialized) return;
            initialized = true;
            foreach (Transform child in abilitiesContainer) {
                if (child.TryGetComponent(out UnitPart part)) parts.Add(part);
            }
            foreach (var part in parts) {
                switch (part) {
                    case UnitStatusBars b: Bars = b; break;
                    case UnitItemsView i: ItemsView = i; break;
                    case UnitMarksView m: Marks = m; break;
                    case UnitRotation r: Rotation = r; break;
                    case UnitAirborneView a: AirborneView = a; break;
                }
                part.Initialize(this);
            }
        }

        /// <summary>Shows <paramref name="heroTrait"/> at <paramref name="heroRank"/>; resets every part.</summary>
        protected void SetHero(HeroTrait heroTrait, HeroRank heroRank, bool home) {
            EnsureInitialized();
            trait = heroTrait;
            rank = heroRank;
            SetUpModel();
            foreach (var part in parts) part.ResetAll(home);
            mecanim.Idle();
            ApplyRank();
        }

        public void SetRank(HeroRank heroRank) {
            rank = heroRank;
            ApplyRank();
        }

        void ApplyRank() {
            rankIcon.sprite = AssetDB.Instance.GetRankIcon(rank);
            model.SetUniformScale(GameManager.Instance.Settings.ScaleOf(rank));
        }

        void SetUpModel() {
            foreach (Transform child in model) {
                if (child.TryGetComponent(out Mecanim _)) Destroy(child.gameObject);
            }
            mecanim = Instantiate(trait.mecanim, model);
        }

        public virtual void Activate() {
            gameObject.SetActive(true);
        }

        public virtual void Deactivate() {
            gameObject.SetActive(false);
        }

        public virtual void SwitchCanvas(bool value) {
            canvas.enabled = value;
        }

        public void FaceTowards(Vector3 worldPoint, bool instantly = false) {
            var direction = (worldPoint - transform.position).ToZeroY();
            if (direction.sqrMagnitude > 1e-6f) Rotation.Rotate(direction, instantly);
        }
    }
}
