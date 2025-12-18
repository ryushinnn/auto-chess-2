using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TextBox : MonoBehaviour {
    [SerializeField] TMP_Text text;

    public void Switch(bool? active = null) {
        if (active.HasValue) {
            gameObject.SetActive(active.Value);
        }
        else {
            gameObject.SetActive(!gameObject.activeSelf);
        }
    }

    public void SetText(string content) {
        text.text = content;
    }
}