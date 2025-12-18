using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

public class HexCell : Cell {
    [SerializeField] TMP_Text dev_label;
    
    public int dev_X { get; private set; }

    public int dev_Y { get; private set; }

    public void dev_SaveIndex(int x, int y) {
        dev_X = x;
        dev_Y = y;
        dev_label.text = $"({x},{y})";
        dev_label.gameObject.SetActive(true);
    }

    public void dev_SwitchLabel(bool state) {
        dev_label.gameObject.SetActive(state);
    }
}