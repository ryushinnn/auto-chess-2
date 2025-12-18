using System;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Destinies_Destiny : MonoBehaviour {
    [SerializeField] Image iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text currentNumberText;
    [SerializeField] TMP_Text[] stageTexts;
    [SerializeField] TMP_Text[] separators;
    [SerializeField] Button button;
    
    public bool Empty { get; private set; }

    Action onSelect;

    void Awake() {
        button.onClick.AddListener(() => onSelect?.Invoke());
    }

    public void SetData(string name, Sprite icon, int current, int[] checkpoints, int index, Action onSelect) {
        this.onSelect = onSelect;
        nameText.text = name;
        iconImage.sprite = icon;
        
        for (int i = 0; i < stageTexts.Length; i++) {
            if (i >= checkpoints.Length) {
                stageTexts[i].gameObject.SetActive(false);
                if (i > 0) { 
                    separators[i-1].gameObject.SetActive(false);
                }
                continue;
            }

            stageTexts[i].gameObject.SetActive(true);
            stageTexts[i].text = checkpoints[i].ToString();
            if (index >= i) {
                stageTexts[i].color = i == index ? Color.red : Color.white;
                stageTexts[i].fontSize = i == index ? 30 : 20;

                if (i > 0) {
                    separators[i-1].gameObject.SetActive(true);
                    separators[i-1].color = Color.white;
                }
            }
            else {
                stageTexts[i].color = Color.gray;
                stageTexts[i].fontSize = 20;
                if (i > 0) {
                    separators[i-1].gameObject.SetActive(true);
                    separators[i-1].color = Color.gray;
                }
            }
        }
        currentNumberText.text = current.ToString();
        iconImage.color = index >= 0 ? Color.white : Color.gray;
        nameText.color = index >= 0 ? Color.white : Color.gray;
        
        Empty = false;
    }
    
    public void MarkAsEmpty() {
        Empty = true;
    }
}