using DG.Tweening;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Turns the model towards a direction (was HeroRotation).</summary>
    public class UnitRotation : UnitPart {
        const float ROTATE_TIME = 0.25f;

        Tween rotateTween;

        public override void ResetAll(bool home) {
            rotateTween?.Kill();
        }

        public void Rotate(Vector3 direction, bool instantly = false) {
            rotateTween?.Kill();
            var rotation = Quaternion.LookRotation(direction);
            if (instantly) {
                unit.Model.rotation = rotation;
                return;
            }
            rotateTween = unit.Model.DORotateQuaternion(rotation, ROTATE_TIME);
        }
    }
}
