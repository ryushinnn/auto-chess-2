using System;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class TextureChanger : MonoBehaviour {
    Renderer renderer;
    MaterialPropertyBlock propertyBlock;

    void Awake() {
        renderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }


    [Button]
    public void UpdateTexture(Texture texture) {
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture("_BaseMap", texture);
        renderer.SetPropertyBlock(propertyBlock);
    }
}