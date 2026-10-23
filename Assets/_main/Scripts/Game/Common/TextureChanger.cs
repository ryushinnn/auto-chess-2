using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    [RequireComponent(typeof(Renderer))]
    public class TextureChanger : MonoBehaviour {
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        Renderer targetRenderer;
        MaterialPropertyBlock propertyBlock;

        void Awake() {
            targetRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
        }

        [Button]
        public void UpdateTexture(Texture texture) {
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(BaseMap, texture);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
