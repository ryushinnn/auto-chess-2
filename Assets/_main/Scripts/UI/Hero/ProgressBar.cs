using System;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

public class ProgressBar : MonoBehaviour {
    [SerializeField] Image main;
    [SerializeField] Image sub;
    [SerializeField] Color positiveColor;
    [SerializeField] Color negativeColor;
    [SerializeField] bool ignoreAnimation;

    [SerializeField, ReadOnly]float amount;
    Sequence sequence;
    
    public void UpdateAmount(float value, bool instantly = false) {
        if (instantly || ignoreAnimation) {
            amount = value;
            sequence?.Kill();
            main.fillAmount = amount;
            sub.fillAmount = amount;
            return;
        }

        if (value > amount) {
            amount = value;
            sub.color = positiveColor;
            sequence?.Kill();
            sequence = DOTween.Sequence();
            sequence.Append(sub.DOFillAmount(amount, 0.2f))
                .Append(main.DOFillAmount(amount,0.3f));
        }
        else if (value < amount) {
            amount = value;
            sub.color = negativeColor;
            sequence?.Kill();
            sequence = DOTween.Sequence();
            sequence.Append(main.DOFillAmount(amount, 0.2f))
                .Append(sub.DOFillAmount(amount,0.3f));
        }
    }

    public void SetMainColor(Color color) {
        main.color = color;
    }
    
    [Button]
    void Dev_UpdateAmount(float value) {
        UpdateAmount(value);
    }
}