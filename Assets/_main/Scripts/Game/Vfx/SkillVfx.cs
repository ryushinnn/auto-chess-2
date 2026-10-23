using System;
using System.Collections;
using AutoChess.Core.Combat;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// A skill VFX driven by a <see cref="VisualEvent"/>: a burst at a point, a shot travelling between two points, a
    /// beam stretched between them (its quad along +Z, unit length) or an aura following a unit. Its particle systems
    /// restart on every play; when it ends they stop emitting and it returns to the pool after <see cref="linger"/>.
    /// </summary>
    public class SkillVfx : Vfx {
        [SerializeField] float linger = 0.6f;
        [SerializeField] float height = 0.9f;
        [SerializeField] Renderer beam;
        [SerializeField] float beamFadeIn = 0.08f;
        [SerializeField] float beamFadeOut = 0.25f;

        static readonly int ColorId = Shader.PropertyToID("_Color");

        ParticleSystem[] systems;
        MaterialPropertyBlock block;
        Color beamColor = Color.white;
        Coroutine routine;
        Transform follow;

        /// <summary>Raised once when the VFX goes back to the pool.</summary>
        public event Action<SkillVfx> Released;

        void Awake() {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            if (beam != null) {
                block = new MaterialPropertyBlock();
                beamColor = beam.sharedMaterial.GetColor(ColorId);
            }
        }

        /// <param name="scale">Size of bursts and auras (1 = authored for a radius of one cell).</param>
        /// <param name="followed">Unit transform an aura follows.</param>
        public void Play(VisualMode mode, Vector3 from, Vector3 to, float duration, float scale, Transform followed) {
            if (routine != null) StopCoroutine(routine);
            follow = mode == VisualMode.Attach ? followed : null;
            var t = transform;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var up = Vector3.up * height;
            var flat = to - from;
            flat.y = 0;
            switch (mode) {
                case VisualMode.Burst:
                    t.position = from;
                    t.localScale = Vector3.one * scale;
                    break;
                case VisualMode.Attach:
                    t.position = follow != null ? follow.position : from;
                    t.localScale = Vector3.one * scale;
                    break;
                case VisualMode.Travel:
                    t.position = from + up;
                    if (flat.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(flat);
                    break;
                case VisualMode.Beam:
                    t.position = from + up;
                    if (flat.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(flat);
                    t.localScale = new Vector3(1, 1, Mathf.Max(flat.magnitude, 0.01f));
                    break;
            }
            if (beam != null) {
                beam.enabled = mode == VisualMode.Beam;
                SetBeamAlpha(0);
            }
            foreach (var s in systems) {
                s.Clear(true);
                s.Play(true);
            }
            routine = StartCoroutine(Run(mode, from + up, to + up, Mathf.Max(duration, 0.01f)));
        }

        IEnumerator Run(VisualMode mode, Vector3 from, Vector3 to, float duration) {
            var time = 0f;
            while (time < duration) {
                time += Time.deltaTime;
                if (mode == VisualMode.Travel) transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(time / duration));
                if (follow != null) transform.position = follow.position;
                if (beam != null && mode == VisualMode.Beam) {
                    var fadeIn = beamFadeIn > 0 ? Mathf.Clamp01(time / beamFadeIn) : 1;
                    var fadeOut = beamFadeOut > 0 ? Mathf.Clamp01((duration - time) / beamFadeOut) : 1;
                    SetBeamAlpha(Mathf.Min(fadeIn, fadeOut));
                }
                yield return null;
            }
            Stop();
        }

        /// <summary>Stops emitting; the VFX returns to the pool once its particles have faded.</summary>
        public void Stop() {
            if (routine != null) StopCoroutine(routine);
            foreach (var s in systems) s.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (beam != null) beam.enabled = false;
            routine = StartCoroutine(ReleaseLater());
        }

        IEnumerator ReleaseLater() {
            var time = 0f;
            while (time < linger) {
                time += Time.deltaTime;
                if (follow != null) transform.position = follow.position;
                yield return null;
            }
            routine = null;
            Release();
        }

        /// <summary>Back to the pool right away.</summary>
        public void Release() {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            follow = null;
            var released = Released;
            Released = null;
            released?.Invoke(this);
            VfxPool.Instance.DestroyVfx(this);
        }

        void SetBeamAlpha(float alpha) {
            if (beam == null) return;
            var c = beamColor;
            c.a *= alpha;
            block.SetColor(ColorId, c);
            beam.SetPropertyBlock(block);
        }
    }
}
