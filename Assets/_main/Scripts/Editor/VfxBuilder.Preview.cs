using System.IO;
using AutoChess.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoChess.Editor {
    public static partial class VfxBuilder {
        /// <summary>Temp/vfx_status_preview.png: each status mark on a hero (Yasuo), seen from the board camera's angle,
        /// its particles simulated for 0.6 s.</summary>
        [MenuItem("AutoChess/VFX/Render Status Preview")]
        public static void RenderStatusPreview() {
            var keys = new[] { StatusVisuals.Stun, StatusVisuals.Silence, StatusVisuals.AntiHeal, StatusVisuals.Invulnerable };
            const int size = 320;
            var sheet = new Texture2D(size * keys.Length, size, TextureFormat.RGB24, false);
            var hero = HeroTraitDB.Instance.Find("Yasuo").mecanim.gameObject;
            for (var k = 0; k < keys.Length; k++) {
                var scene = EditorSceneManager.NewPreviewScene();
                try {
                    var camera = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                    camera.scene = scene;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.45f, 0.42f, 0.25f, 1);
                    camera.fieldOfView = 30;
                    camera.transform.position = new Vector3(0, 1.4f, 0) + Quaternion.Euler(50, 0, 0) * Vector3.back * 9;
                    camera.transform.LookAt(new Vector3(0, 1.4f, 0));
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(hero, scene);
                    model.transform.rotation = Quaternion.Euler(0, 180, 0);
                    var top = 1.8f;
                    foreach (var r in model.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{keys[k]}.prefab");
                    var vfx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    vfx.transform.position = keys[k] == StatusVisuals.Invulnerable ? Vector3.zero : Vector3.up * top;
                    foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(0.6f, false, true);
                    var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                    camera.targetTexture = rt;
                    camera.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    RenderTexture.active = previous;
                    camera.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                    sheet.SetPixels(k * size, 0, size, size, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }
                finally {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllBytes("Temp/vfx_status_preview.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }
    }
}
