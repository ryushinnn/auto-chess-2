using AutoChess.Core;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class HpText : MonoBehaviour {
        [SerializeField] TMP_Text text;
        [SerializeField] Image critMark;
        [SerializeField] CanvasGroup cg;
        [SerializeField] Color physicalDamageColor;
        [SerializeField] Color magicalDamageColor;
        [SerializeField] Color trueDamageColor;
        [SerializeField] Color healColor;
        [SerializeField] AnimationCurve damageCurve;

        Sequence seq;

        const float NORMAL_SIZE = 0.4f;
        const float CRIT_SIZE = 0.5f;

        public void SetAsDamage(float damage, DamageType type, bool crit) {
            text.SetText("{0}", (int)damage);
            text.fontSize = crit ? CRIT_SIZE : NORMAL_SIZE;
            var color = type switch {
                DamageType.Physical => physicalDamageColor,
                DamageType.Magical => magicalDamageColor,
                _ => trueDamageColor,
            };
            text.color = color;
            critMark.color = color;
            critMark.gameObject.SetActive(crit);

            seq?.Kill();
            transform.localScale = 2f * Vector3.one;
            cg.alpha = 1f;
            var targetX = transform.localPosition.x + 0.25f;
            var targetY = transform.localPosition.y - 0.5f;
            seq = DOTween.Sequence()
                .Append(transform.DOScale(1, 0.25f))
                .Insert(0.1f, transform.DOLocalMoveX(targetX, 0.5f))
                .Join(transform.DOLocalMoveY(targetY, 0.5f).SetEase(damageCurve))
                .Insert(0.4f, cg.DOFade(0, 0.2f));
        }

        public void SetAsHeal(float amount) {
            text.SetText("+{0}", (int)amount);
            text.fontSize = NORMAL_SIZE;
            text.color = healColor;
            critMark.gameObject.SetActive(false);

            seq?.Kill();
            transform.localScale = 2f * Vector3.one;
            cg.alpha = 1f;
            seq = DOTween.Sequence()
                .Append(transform.DOScale(1, 0.25f))
                .Insert(0.1f, transform.DOLocalMoveY(1f, 0.5f))
                .Insert(0.4f, cg.DOFade(0, 0.2f));
        }

        void OnDisable() {
            seq?.Kill();
        }
    }
}
