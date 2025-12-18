using System;
using UnityEngine;

public class ScalableVfx : Vfx {
    float originalScale;

    void Awake() {
        originalScale = transform.localScale.x;
    }

    public void SetScale(Vector3 scale) {
        transform.localScale = originalScale * scale;
    }
}