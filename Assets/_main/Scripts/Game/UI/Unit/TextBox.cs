using TMPro;
using UnityEngine;

namespace AutoChess.Game {
    public class TextBox : MonoBehaviour {
        [SerializeField] TMP_Text text;

        public void Switch(bool? active = null) {
            gameObject.SetActive(active ?? !gameObject.activeSelf);
        }

        public void SetText(string content) {
            text.text = content;
        }
    }
}
