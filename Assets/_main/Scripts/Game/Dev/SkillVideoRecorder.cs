#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoChess.Core;
using AutoChess.Core.Combat;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Records one MP4 per hero using its skill (Recordings/skills/&lt;id&gt;.mp4, git-ignored), for reviewing the
    /// skills and their VFX: in Play mode, each hero (rank B) fights alone on a training stage of sturdy but harmless
    /// creeps; the battle is filmed by a second camera (no HUD, a little closer than the board camera) at 30 fps game
    /// time until a few seconds after its first cast. AutoTest ops: record-skills [heroes=Id,Id] / record-status.
    /// </summary>
    public sealed class SkillVideoRecorder : MonoBehaviour {
        const int Width = 1280;
        const int Height = 720;
        const int Fps = 30;
        const float AfterCast = 4f;
        const float MaxBattleSeconds = 25f;
        const float MinSeconds = 10f;
        const int ThumbWidth = 320;
        const int ThumbHeight = 180;
        const int SheetColumns = 6;
        public static readonly string OutputDir = Path.GetFullPath("Recordings/skills");

        static SkillVideoRecorder running;
        static readonly List<string> Done = new();
        static string current;

        public static bool IsRunning => running != null;
        public static object Status => new { running = IsRunning, current, done = Done.ToList(), folder = OutputDir };

        /// <summary>Starts recording <paramref name="heroes"/> (every shop hero when empty); returns the list.</summary>
        public static List<string> Start(GameManager game, IEnumerable<string> heroes) {
            if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("enter Play mode in the Arena scene first");
            if (running != null) throw new System.InvalidOperationException($"already recording ({current})");

            var list = heroes?.Where(h => !string.IsNullOrEmpty(h)).ToList();
            if (list == null || list.Count == 0) {
                list = game.Db.Units.Where(u => !u.Summoned && u.Reputation != Reputation.None).Select(u => u.Id).ToList();
            }
            Done.Clear();
            running = new GameObject("SkillVideoRecorder").AddComponent<SkillVideoRecorder>();
            running.StartCoroutine(running.RecordAll(game, list));
            return list;
        }

        IEnumerator RecordAll(GameManager game, List<string> heroes) {
            Directory.CreateDirectory(OutputDir);
            var db = TrainingDatabase();
            var previousRate = Time.captureFramerate;
            Time.captureFramerate = Fps;
            var cam = new GameObject("RecorderCamera").AddComponent<Camera>();
            cam.enabled = false;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try {
                foreach (var hero in heroes) {
                    current = hero;
                    yield return RecordOne(game, db, hero, cam, rt, frame);
                    Done.Add(hero);
                }
            }
            finally {
                current = null;
                Time.captureFramerate = previousRate;
                Destroy(cam.gameObject);
                rt.Release();
                Destroy(frame);
                running = null;
                Destroy(gameObject);
            }
        }

        IEnumerator RecordOne(GameManager game, GameDatabase db, string hero, Camera cam, RenderTexture rt, Texture2D frame) {
            game.RestartOn(db, 7);
            yield return null;
            game.Submit(new CheatUnitCommand { UnitId = hero, Rank = HeroRank.B });
            yield return null;
            var unit = game.LocalPlayer.Units.FirstOrDefault(u => u.DefinitionId == hero);
            if (unit == null) yield break;
            var ranged = db.Unit(hero).AttackRange >= 3;
            game.Submit(new MoveUnitCommand { UnitId = unit.Id, Target = UnitLocation.Board(ranged ? 0 : 2, 3) });
            game.Submit(new ReadyCommand());

            for (var i = 0; i < 30 * Fps && game.Session.State.Phase != MatchPhase.Battle; i++) yield return null;
            if (game.Session.State.Phase != MatchPhase.Battle) yield break;

            var casts = 0;
            var lastCast = -1f;
            void OnEvent(GameEvent e) {
                if (e is UnitSkillStartedEvent s && game.Battle.VisibleBattle != null && game.Battle.VisibleBattle.FindUnit(s.UnitId)?.Def.Id == hero) casts++;
            }
            game.EventRaised += OnEvent;
            var main = Camera.main;
            cam.CopyFrom(main);
            cam.enabled = false;
            // same angle as the board camera, half as far, following the hero (smoothed)
            var forward = main.transform.forward;
            var distance = Mathf.Abs(forward.y) > 1e-3f ? -main.transform.position.y / forward.y * 0.4f : 10f;
            Vector3? focus = null;
            void Follow() {
                BattleUnitView view = null;
                foreach (var v in game.Battle.Views.Values) {
                    if (v.CombatUnit != null && v.CombatUnit.Def.Id == hero) view = v;
                }
                var target = view != null ? view.transform.position + Vector3.up * 0.8f : Vector3.zero;
                focus = focus.HasValue ? Vector3.Lerp(focus.Value, target, 0.08f) : target;
                cam.transform.position = focus.Value - forward * distance;
            }
            var path = Path.Combine(OutputDir, $"{hero}.mp4");
            var attributes = new VideoTrackAttributes {
                frameRate = new MediaRational(Fps), width = Width, height = Height, includeAlpha = false,
                bitRateMode = VideoBitrateMode.High,
            };
            var thumbs = new List<Color32[]>();
            var thumbRt = RenderTexture.GetTemporary(ThumbWidth, ThumbHeight, 0, RenderTextureFormat.ARGB32);
            var thumb = new Texture2D(ThumbWidth, ThumbHeight, TextureFormat.RGBA32, false);
            var frames = 0;
            using (var encoder = new MediaEncoder(path, attributes)) {
                var time = 0f;
                var seenCasts = 0;
                while (game.Session.State.Phase == MatchPhase.Battle && time < MaxBattleSeconds) {
                    yield return new WaitForEndOfFrame();
                    Follow();
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    frame.Apply(false);
                    RenderTexture.active = previous;
                    encoder.AddFrame(frame);
                    if (frames++ % (Fps / 2) == 0) {
                        Graphics.Blit(rt, thumbRt);
                        RenderTexture.active = thumbRt;
                        thumb.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), 0, 0);
                        RenderTexture.active = previous;
                        thumbs.Add(thumb.GetPixels32());
                    }
                    time += 1f / Fps;
                    if (casts != seenCasts) {
                        seenCasts = casts;
                        lastCast = time;
                    }
                    if (lastCast >= 0 && time - lastCast >= AfterCast && casts >= 1 && time >= MinSeconds) break;
                }
            }
            game.EventRaised -= OnEvent;
            WriteSheet(Path.Combine(OutputDir, $"{hero}_sheet.png"), thumbs);
            RenderTexture.ReleaseTemporary(thumbRt);
            Destroy(thumb);
            Debug.Log($"[SkillVideoRecorder] {hero}: {casts} cast(s) -> {path}");
        }

        /// <summary>Every half second of the video in a grid (left to right, top to bottom), to review it at a glance.</summary>
        static void WriteSheet(string path, List<Color32[]> thumbs) {
            if (thumbs.Count == 0) return;
            var rows = (thumbs.Count + SheetColumns - 1) / SheetColumns;
            var sheet = new Texture2D(ThumbWidth * SheetColumns, ThumbHeight * rows, TextureFormat.RGBA32, false);
            var blank = new Color32[sheet.width * sheet.height];
            sheet.SetPixels32(blank);
            for (var i = 0; i < thumbs.Count; i++) {
                sheet.SetPixels32(i % SheetColumns * ThumbWidth, (rows - 1 - i / SheetColumns) * ThumbHeight, ThumbWidth, ThumbHeight, thumbs[i]);
            }
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>The real data with one training stage: four creeps with lots of health that barely hurt.</summary>
        static GameDatabase TrainingDatabase() {
            var content = GameDataJson.Read(GameDataJson.Write(ClientConfig.Instance.Content));
            var enemies = new[] {
                ("Malphite", 5, 3), ("Yasuo", 5, 4), ("Ashe", 7, 3), ("Jinx", 7, 5),
            }.Select(e => new EnemyDefinition {
                UnitId = e.Item1, Rank = HeroRank.B, Cell = new HexCoord(e.Item2, e.Item3), Items = new string[0], HpScale = 5, DamageScale = 0.12,
            }).ToArray();
            var round = new RoundDefinition { Enemies = enemies };
            content.Stages.Clear();
            for (var i = 0; i < 3; i++) content.Stages.Add(new StageDefinition { Name = $"Training {i}", Rounds = new[] { round } });
            return content.ToDatabase();
        }
    }
}
#endif
