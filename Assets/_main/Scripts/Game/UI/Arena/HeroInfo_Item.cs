using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class HeroInfo_Item : MonoBehaviour {
        [SerializeField] Image image;

        public void SetData(Sprite sprite) {
            image.enabled = sprite != null;
            if (sprite != null) image.sprite = sprite;
        }
    }
}
