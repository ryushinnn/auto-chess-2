using AutoChess.Core;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>An owned unit standing on the bench or on the board (was LineUpHero).</summary>
    public class RosterUnitView : UnitView {
        [SerializeField] UnitDragHandler picker;

        [ShowInInspector, ReadOnly] public int UnitId { get; private set; }
        public UnitLocation Location { get; private set; }

        Tween snapTween;

        public void Bind(UnitInstance unit) {
            UnitId = unit.Id;
            SetHero(HeroTraitDB.Instance.Find(unit.DefinitionId), unit.Rank, true);
            name = $"[LineUp]({unit.Rank}){unit.DefinitionId}#{unit.Id}";
            picker.Initialize(this, 1 << MapVisual.Instance.Layer);
            Refresh(unit);
        }

        /// <summary>Rank and items from the model.</summary>
        public void Refresh(UnitInstance unit) {
            if (unit.Rank != Rank) {
                SetRank(unit.Rank);
                name = $"[LineUp]({unit.Rank}){unit.DefinitionId}#{unit.Id}";
            }
            ItemsView.SetItems(unit.Items);
        }

        /// <summary>Places the view on its slot or cell, sliding when <paramref name="animate"/>.</summary>
        public void PlaceAt(UnitLocation location, bool animate) {
            Location = location;
            var target = MapVisual.Instance.LocationToWorld(location);
            snapTween?.Kill();
            if (animate) snapTween = transform.DOMove(target, 0.1f).SetEase(Ease.Linear);
            else WorldPosition = target;
        }

        /// <summary>Back to the current spot after a rejected drop.</summary>
        public void SnapBack() => PlaceAt(Location, true);

        public void SwitchPickable(bool value) {
            picker.SwitchPickable(value);
        }

        public override void Deactivate() {
            snapTween?.Kill();
            base.Deactivate();
        }
    }
}
