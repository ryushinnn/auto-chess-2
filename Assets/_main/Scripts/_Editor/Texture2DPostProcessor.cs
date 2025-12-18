#if UNITY_EDITOR
using UnityEditor;

public class Texture2DPostProcessor : AssetPostprocessor {
    const string TARGET_DIRECTORY = "Assets/_main/Textures/2D";

    void OnPreprocessTexture() {
        if (assetPath.StartsWith(TARGET_DIRECTORY)) {
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
        }
    }
}
#endif