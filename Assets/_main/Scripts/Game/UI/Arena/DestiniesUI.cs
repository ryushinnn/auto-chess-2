using System.Collections.Generic;
using AutoChess.Core;
using RExt.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>Synergies of the units on the board (roles and realms), paged, with descriptions.</summary>
    public class DestiniesUI : BaseUI, ISessionView, ISessionResettable {
        [SerializeField] Destinies_Destiny destinyPrefab;
        [SerializeField] Transform destinyContainer;
        [SerializeField] Button leftButton;
        [SerializeField] Button rightButton;
        [SerializeField] TMP_Text pageText;
        [SerializeField] GameObject description;
        [SerializeField] Image descriptionIcon;
        [SerializeField] TMP_Text descriptionText;
        [SerializeField] Button closeDescriptionButton;

        const int ITEM_PER_PAGE = 5;

        DestinyConfig lastConfig;
        List<Destinies_Destiny> destinies;
        int currentPage;
        int maxPage;
        GameManager game;

        void Awake() {
            leftButton.onClick.AddListener(NavigateLeft);
            rightButton.onClick.AddListener(NavigateRight);
            closeDescriptionButton.onClick.AddListener(HideDescription);
            EnsureList();
        }

        void EnsureList() {
            if (destinies != null) return;
            destinies = new List<Destinies_Destiny>();
            destinyContainer.ProcessChildren<Destinies_Destiny>(d => destinies.Add(d));
        }

        public void Bind(GameManager gameManager) {
            game = gameManager;
            game.EventRaised += OnEvent;
            Refresh();
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        void OnEvent(GameEvent e) {
            if (e is TraitsChangedEvent t && t.PlayerId == game.Session.LocalPlayerId) Refresh();
        }

        public void OnSessionReset() {
            currentPage = 0;
            Refresh();
        }

        void Refresh() {
            SetData(Traits.ForBoard(game.Db, game.Session.LocalPlayer));
        }

        public void SetData(List<TraitStatus> traits) {
            EnsureList();
            HideDescription();

            var count = 0;
            foreach (var status in traits) {
                var cfg = DestinyDB.Instance.Find(status.Id);
                if (cfg == null) continue;
                if (count >= destinies.Count) {
                    destinies.Add(Instantiate(destinyPrefab, destinyContainer));
                }

                var tier = status.Tier;
                destinies[count].gameObject.SetActive(true);
                destinies[count].SetData(cfg.GetName(), cfg.GetIcon(), status.Count, cfg.checkpoints, tier,
                    () => ShowDestinyDescription(cfg, tier));
                count++;
            }

            for (int i = count; i < destinies.Count; i++) {
                destinies[i].gameObject.SetActive(false);
                destinies[i].MarkAsEmpty();
            }

            maxPage = (count + ITEM_PER_PAGE - 1) / ITEM_PER_PAGE - 1;
            currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, maxPage));
            RefreshCurrentPage();
        }

        void NavigateLeft() {
            currentPage--;
            if (currentPage < 0) currentPage = Mathf.Max(0, maxPage);
            RefreshCurrentPage();
        }

        void NavigateRight() {
            currentPage++;
            if (currentPage > maxPage) currentPage = 0;
            RefreshCurrentPage();
        }

        void RefreshCurrentPage() {
            var index = 0;
            foreach (var d in destinies) {
                if (d.Empty) continue;
                d.gameObject.SetActive(index / ITEM_PER_PAGE == currentPage);
                index++;
            }
            pageText.text = $"{currentPage + 1}/{Mathf.Max(0, maxPage) + 1}";
        }

        void ShowDestinyDescription(DestinyConfig cfg, int stage) {
            if (lastConfig == cfg) {
                HideDescription();
                return;
            }

            lastConfig = cfg;
            description.gameObject.SetActive(true);
            descriptionIcon.sprite = lastConfig.GetIcon();
            descriptionText.text = cfg.Description(stage);
        }

        void HideDescription() {
            lastConfig = null;
            description.gameObject.SetActive(false);
        }
    }
}
