using System;
using System.Collections.Generic;
using System.Linq;
using RExt.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DestiniesUI : BaseUI {
    [SerializeField] Destinies_Destiny destinyPrefab;
    [SerializeField] Transform destinyContainer;
    [SerializeField] Button leftButton;
    [SerializeField] Button rightButton;
    [SerializeField] TMP_Text pageText;
    [SerializeField] GameObject description;
    [SerializeField] Image descriptionIcon;
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] Button closeDescriptionButton;

    DestinyConfig lastConfig;
    List<Destinies_Destiny> destinies = new();
    int currentPage;
    int maxPage;
    
    const int ITEM_PER_PAGE = 5;

    void Awake() {
        leftButton.onClick.AddListener(NavigateLeft);
        rightButton.onClick.AddListener(NavigateRight);
        closeDescriptionButton.onClick.AddListener(HideDescription);
        destinyContainer.ProcessChildren<Destinies_Destiny>(d => destinies.Add(d));
    }

    public void SetData(Dictionary<Role, int> roleNumbers, Dictionary<Realm, int> realmNumbers) {
        HideDescription();
        
        var destinyNumbers = new List<(object destiny, int number)>();
        foreach (var (role,num) in roleNumbers) {
            destinyNumbers.Add((role, num));
        }
        foreach (var (realm,num) in realmNumbers) {
            destinyNumbers.Add((realm, num));
        }
        destinyNumbers.Sort((dnA, dnB) => {
            var cfgA = dnA.destiny switch {
                Role role => DestinyDB.Instance.Find(role),
                Realm realm => DestinyDB.Instance.Find(realm),
            };
            var indexA = cfgA.GetCheckpointIndex(dnA.number);

            var cfgB = dnB.destiny switch {
                Role role => DestinyDB.Instance.Find(role),
                Realm realm => DestinyDB.Instance.Find(realm),
            };
            var indexB = cfgB.GetCheckpointIndex(dnB.number);

            return indexA != indexB ? indexB.CompareTo(indexA) : dnB.number.CompareTo(dnA.number);
        });
        
        var count = 0;
        foreach (var (destiny, num) in destinyNumbers) {
            if (count >= destinies.Count) {
                var newDestiny = Instantiate(destinyPrefab, destinyContainer);
                destinies.Add(newDestiny);
            }

            destinies[count].gameObject.SetActive(true);
            var cfg = destiny switch {
                Role role => DestinyDB.Instance.Find(role),
                Realm realm => DestinyDB.Instance.Find(realm),
            };
            var index = cfg.GetCheckpointIndex(num);
            destinies[count].SetData(cfg.GetName(), cfg.GetIcon(), num, cfg.checkpoints, index,
                () => {
                    ShowDestinyDescription(cfg, index);
                });
            
            count++;
        }

        for (int i = count; i < destinies.Count; i++) {
            destinies[i].gameObject.SetActive(false);
            destinies[i].MarkAsEmpty();
        }
        
        maxPage = (count + ITEM_PER_PAGE - 1) / ITEM_PER_PAGE - 1;
        currentPage = Mathf.Clamp(currentPage, 0, maxPage);
        RefreshCurrentPage();
    }

    void NavigateLeft() {
        currentPage--;
        if (currentPage < 0) currentPage = maxPage;
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
        pageText.text = $"{currentPage+1}/{maxPage+1}";
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