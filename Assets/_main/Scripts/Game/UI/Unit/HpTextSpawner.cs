using System.Collections.Generic;
using AutoChess.Core;
using DG.Tweening;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Pool of floating damage / heal numbers.</summary>
    public class HpTextSpawner : Singleton<HpTextSpawner> {
        [SerializeField] HpText hpTextPrefab;

        readonly Queue<HpText> pool = new();

        const int POOL_INIT_SIZE = 10;
        const float LEFT_POS = -0.5f;
        const float RIGHT_POS = 0.5f;
        const float POS_Y_GAP = 0.5f;
        const float LIFETIME = 1f;

        protected override void OnAwake() {
            for (int i = 0; i < POOL_INIT_SIZE; i++) {
                var hpText = Instantiate(hpTextPrefab, transform);
                hpText.gameObject.SetActive(false);
                pool.Enqueue(hpText);
            }
        }

        /// <param name="stackIndex">Part of a multi-part hit; each part is drawn one row higher.</param>
        public HpText SpawnDamage(Transform parent, float amount, DamageType type, bool crit, int stackIndex = 0) {
            var hpText = Spawn(parent, new Vector3(RIGHT_POS, POS_Y_GAP * stackIndex, 0));
            hpText.SetAsDamage(amount, type, crit);
            return hpText;
        }

        public HpText SpawnHeal(Transform parent, float amount) {
            var hpText = Spawn(parent, new Vector3(LEFT_POS, 0, 0));
            hpText.SetAsHeal(amount);
            return hpText;
        }

        HpText Spawn(Transform parent, Vector3 localPosition) {
            if (!pool.TryDequeue(out var hpText)) hpText = Instantiate(hpTextPrefab, parent);
            hpText.transform.SetParent(parent, false);
            hpText.gameObject.SetActive(true);
            hpText.transform.localPosition = localPosition;
            hpText.transform.localEulerAngles = Vector3.zero;
            hpText.transform.SetAsLastSibling();
            DOVirtual.DelayedCall(LIFETIME, () => Recycle(hpText));
            return hpText;
        }

        void Recycle(HpText hpText) {
            if (hpText == null) return;
            hpText.transform.SetParent(transform, false);
            hpText.gameObject.SetActive(false);
            pool.Enqueue(hpText);
        }
    }
}
