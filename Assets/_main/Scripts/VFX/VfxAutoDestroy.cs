using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class VfxAutoDestroy : MonoBehaviour {
    [SerializeField] float time;

    Vfx vfx;
    Coroutine destroyCoroutine;

    void Start() {
        vfx = GetComponent<Vfx>();
        StartCoroutine(DoDestroy());
    }

    IEnumerator DoDestroy() {
        yield return BetterWaitForSeconds.Wait(time);
        VfxPool.Instance.DestroyVfx(vfx);
    }
}