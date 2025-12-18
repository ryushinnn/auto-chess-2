#if UNITY_EDITOR
using UnityEditor;

public class TexturePostprocessor : AssetPostprocessor {
    const string TARGET_FOLDER = "Assets/_main/Textures/2D";

    void OnPreprocessTexture() {
        if (assetPath.StartsWith(TARGET_FOLDER)) {
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
        }
    }
}
#endif