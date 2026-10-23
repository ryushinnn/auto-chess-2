using System.Collections;
using RExt.Utils;
using UnityEngine;

namespace AutoChess.Game {
    public class VfxAutoDestroy : MonoBehaviour {
        [SerializeField] float time;

        Vfx vfx;

        void OnEnable() {
            vfx = GetComponent<Vfx>();
            StartCoroutine(DoDestroy());
        }

        IEnumerator DoDestroy() {
            yield return BetterWaitForSeconds.Wait(time);
            VfxPool.Instance.DestroyVfx(vfx);
        }
    }
}
