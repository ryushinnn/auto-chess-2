using UnityEditor;

namespace AutoChess.Game.Editor {
    /// <summary>Every texture under Textures/2D imports as a sprite (100 ppu, no mipmaps).</summary>
    public class Texture2DPostProcessor : AssetPostprocessor {
        const string TARGET_DIRECTORY = "Assets/_main/Textures/2D";

        void OnPreprocessTexture() {
            if (!assetPath.StartsWith(TARGET_DIRECTORY)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
        }
    }
}
