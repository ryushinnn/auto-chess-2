using System;
using System.Linq;
using DG.Tweening;
using RExt.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Shop_Hero : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    [SerializeField] Image thumbnailImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text priceText;
    [SerializeField] Shop_Hero_Destiny[] destinies;
    [SerializeField] GameObject soldMark;
    [SerializeField] Image backgroundImage;
    [SerializeField] Sprite unknownSprite, eliteSprite, legendarySprite;
    [SerializeField] GameObject preview;
    [SerializeField] GameObject backside;
    
    Button button;
    Tween tween;
    HeroTrait trait;
    bool sold;

    Sequence revealSeq;

    void Awake() {
        button = GetComponent<Button>();
    }

    void Start() {
        button.onClick.AddListener(Purchase);
    }

    public void SetData(HeroTrait trait, float revealDelay) {
        this.trait = trait;
        thumbnailImage.sprite = trait.thumbnail;
        thumbnailImage.transform.SetUniformScale();
        nameText.text = trait.DisplayName();
        priceText.text = $"<sprite name=coin>{GameConfigs.HERO_PRICES[this.trait.reputation]}";
        destinies[0].Initialize(trait.realm);
        var roles = trait.role.GetAllFlags().Where(x=>x != 0).ToArray();
        for (int i = 1; i <= 2; i++) {
            if (i-1>=roles.Length) {
                destinies[i].gameObject.SetActive(false);
                continue;
            }
            
            destinies[i].gameObject.SetActive(true);
            destinies[i].Initialize(roles[i-1]);
        }

        sold = false;
        soldMark.SetActive(false);
        button.interactable = true;
        
        backgroundImage.sprite = trait.reputation switch {
            Reputation.Unknown => unknownSprite,
            Reputation.Elite => eliteSprite,
            Reputation.Legendary => legendarySprite,
        };
        
        PlayRevealAnimation(revealDelay);
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (sold) return;
        
        tween?.Kill();
        tween = thumbnailImage.transform.DOScale(1.1f, 0.2f);
    }
    public void OnPointerExit(PointerEventData eventData) {
        if (sold) return;
        
        tween?.Kill();
        tween = thumbnailImage.transform.DOScale(1f, 0.2f);
    }

    void Purchase() {
        if (sold) return;

        if (GameManager.Instance.Shop.Purchase(trait)) {
            MarkAsSold();
        }
    }
    
    void MarkAsSold() {
        sold = true;
        soldMark.SetActive(true);
        button.interactable = false;
    }

    void PlayRevealAnimation(float delay) {
        preview.SetActive(false);
        backside.SetActive(true);
        transform.SetUniformScale();
        var revealed = false;
        revealSeq?.Kill();
        revealSeq = DOTween.Sequence();
        revealSeq.AppendInterval(delay)
            .Append(DOVirtual.Float(0, 1, 0.25f, val => {
                if (val < 0.5f) {
                    transform.localScale = new Vector3(1 - val / 0.5f, 1, 1);
                }
                else {
                    if (!revealed) {
                        preview.SetActive(true);
                        backside.SetActive(false);
                        revealed = true;
                    }
                    transform.localScale = new Vector3((val-0.5f)/0.5f, 1, 1);
                }
            }).OnComplete(() => {
                transform.SetUniformScale();
            }));
    }
}