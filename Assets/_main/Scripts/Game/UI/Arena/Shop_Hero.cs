using AutoChess.Core;
using DG.Tweening;
using RExt.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>One shop offer card. Clicking buys the unit of its slot.</summary>
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

        static readonly Role[] RoleBuffer = new Role[6];

        Button button;
        Tween tween;
        Sequence revealSeq;
        GameManager game;
        int slot;
        bool sold = true;

        public int Slot => slot;
        public bool Sold => sold;

        void Awake() {
            button = GetComponent<Button>();
            button.onClick.AddListener(Purchase);
        }

        public void Bind(GameManager gameManager, int slotIndex) {
            game = gameManager;
            slot = slotIndex;
        }

        public void SetData(HeroTrait trait, float revealDelay) {
            if (button == null) button = GetComponent<Button>();
            if (trait == null) {
                MarkAsSold();
                return;
            }

            thumbnailImage.sprite = trait.thumbnail;
            thumbnailImage.transform.SetUniformScale();
            nameText.text = trait.DisplayName();
            priceText.text = $"<sprite name=coin>{game.Db.Rules.PriceOf(trait.reputation)}";
            destinies[0].Initialize(trait.realm);
            var roles = trait.role.GetRoles(RoleBuffer);
            for (int i = 1; i <= 2; i++) {
                if (i - 1 >= roles) {
                    destinies[i].gameObject.SetActive(false);
                    continue;
                }
                destinies[i].gameObject.SetActive(true);
                destinies[i].Initialize(RoleBuffer[i - 1]);
            }

            sold = false;
            soldMark.SetActive(false);
            button.interactable = true;

            backgroundImage.sprite = trait.reputation switch {
                Reputation.Elite => eliteSprite,
                Reputation.Legendary => legendarySprite,
                _ => unknownSprite,
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
            if (sold || game == null) return;
            game.Submit(new BuyUnitCommand { ShopSlot = slot });
        }

        public void MarkAsSold() {
            sold = true;
            soldMark.SetActive(true);
            if (button != null) button.interactable = false;
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
                        transform.localScale = new Vector3((val - 0.5f) / 0.5f, 1, 1);
                    }
                }).OnComplete(() => transform.SetUniformScale()));
        }
    }
}
