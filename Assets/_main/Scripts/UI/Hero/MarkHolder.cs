using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MarkHolder : MonoBehaviour {
    public string Id => id;
    
    [SerializeField] Image iconImage;
    [SerializeField] Image timeLeftImage;
    [SerializeField] TMP_Text stacksText;

    string id;
    Tween tween;
    
    public void SetData(string id, Sprite icon, int stacks, float duration) {
        this.id = id;
        iconImage.sprite = icon;
        stacksText.text = stacks.ToString();
        stacksText.gameObject.SetActive(stacks > 1);
        
        timeLeftImage.fillAmount = 0;
        tween?.Kill();
        tween = timeLeftImage.DOFillAmount(1, duration).SetEase(Ease.Linear);
    }

    public void SetData(string id, Sprite icon, int stacks) {
        this.id = id;
        iconImage.sprite = icon;
        stacksText.text = stacks.ToString();
        stacksText.gameObject.SetActive(stacks > 1);
        
        timeLeftImage.fillAmount = 0;
        tween?.Kill();
    }
}