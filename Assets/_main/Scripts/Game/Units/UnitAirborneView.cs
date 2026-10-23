using DG.Tweening;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Knock-up motion of the model (the visual half of the old HeroStatusEffects).</summary>
    public class UnitAirborneView : UnitPart {
        const float AIRBORNE_MAX_HEIGHT = 2;

        Sequence airborneSequence;

        public override void ResetAll(bool home) {
            airborneSequence?.Kill();
            var p = unit.Model.localPosition;
            unit.Model.localPosition = new Vector3(p.x, 0, p.z);
        }

        public void Airborne(float duration) {
            var currentHeight = unit.Model.localPosition.y;
            var goUpDistance = AIRBORNE_MAX_HEIGHT - currentHeight;
            var goDownDistance = AIRBORNE_MAX_HEIGHT;
            var goUpTime = duration / (goUpDistance + goDownDistance) * goUpDistance;
            var goDownTime = duration - goUpTime;
            airborneSequence?.Kill();
            airborneSequence = DOTween.Sequence()
                .Append(unit.Model.DOLocalMoveY(AIRBORNE_MAX_HEIGHT, goUpTime).SetEase(Ease.OutExpo))
                .Append(unit.Model.DOLocalMoveY(0, goDownTime).SetEase(Ease.InExpo));
        }

        void OnDisable() {
            airborneSequence?.Kill();
        }
    }
}
