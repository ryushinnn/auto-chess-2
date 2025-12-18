using DG.Tweening;
using Unity.Mathematics;
using UnityEngine;

public class HeroRotation : HeroAbility {
    Tween rotateTween;
    const float ROTATE_TIME = 0.25f;

    public override void ResetAll() {
        rotateTween?.Kill();
    }

    public void Rotate(Vector3 direction, bool instantly = false) {
        rotateTween?.Kill();
        var rot = Quaternion.LookRotation(direction);
        if (instantly) {
            hero.Model.rotation = rot;
            return;
        }

        rotateTween = hero.Model.DORotateQuaternion(rot, ROTATE_TIME);
    }
}