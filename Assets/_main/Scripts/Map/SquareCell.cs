using TMPro;
using UnityEngine;

public class SquareCell : Cell {
    [SerializeField] TMP_Text dev_label;
    
    public int dev_Index { get; private set; }

    public void dev_SaveIndex(int index) {
        dev_Index = index;
        dev_label.text = $"({index})";
        dev_label.gameObject.SetActive(true);
    }

    public void dev_SwitchLabel(bool state) {
        dev_label.gameObject.SetActive(state);
    }
}