using UnityEngine;
using UnityEngine.UI;

public class HeroInfo_Item : MonoBehaviour {
    [SerializeField] Image image;

    public void SetData(Sprite sprite) {
        if (sprite != null) {
            image.enabled = true;
            image.sprite = sprite;
        }
        else {
            image.enabled = false;
        }
    }
}