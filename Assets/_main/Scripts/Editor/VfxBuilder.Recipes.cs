using AutoChess.Core.Combat.Heroes;
using AutoChess.Game;
using UnityEngine;

namespace AutoChess.Editor {
    /// <summary>Hit VFX of every attack / skill and the skill VFX of the heroes besides the new ten.</summary>
    public static partial class VfxBuilder {
        static void BuildMore() {
            // generic hits: BattleView plays one on every attack / skill damage (by damage type)
            Build("hit_attack_physical", (r, v) => Hit(r, v, new Color(1f, 0.72f, 0.38f), 0.8f, false));
            Build("hit_attack_magical", (r, v) => Hit(r, v, new Color(0.62f, 0.52f, 1f), 0.8f, false));
            Build("hit_attack_true", (r, v) => Hit(r, v, new Color(1f, 1f, 1f), 0.8f, false));
            Build("hit_skill_physical", (r, v) => Hit(r, v, new Color(1f, 0.58f, 0.22f), 1.4f, true));
            Build("hit_skill_magical", (r, v) => Hit(r, v, new Color(0.55f, 0.42f, 1f), 1.4f, true));
            Build("hit_skill_true", (r, v) => Hit(r, v, new Color(1f, 0.96f, 0.85f), 1.4f, true));

            Build(AkaliSkill.SmokeVisual, AkaliSmoke);
            Build(AkaliSkill.StrikeVisual, (r, v) => SlashBurst(r, v, new Color(0.85f, 0.4f, 1f), 2.2f));
            Build(AatroxDarkSkill.SlashVisual, (r, v) => SlashBeam(r, v, new Color(1.4f, 0.2f, 0.12f, 1f)));
            Build(AatroxLightSkill.SlashVisual, (r, v) => SlashBeam(r, v, new Color(0.6f, 0.9f, 1.4f, 1f)));
            Build(BlitzcrankSkill.PunchVisual, (r, v) => Impact(r, v, new Color(1f, 0.85f, 0.3f)));
            Build(MorganaSkill.ShieldVisual, (r, v) => Bubble(r, v, new Color(0.62f, 0.25f, 1f)));
            Build(ZedSkill.ShurikenVisual, ZedShuriken);
            Build(ZedSkill.RecallVisual, (r, v) => Bolt(r, v, new Color(0.55f, 0.2f, 0.85f), 0.6f, 0.45f));
            Build(ZedSkill.CloneVisual, ZedClone);
            Build(MissFortuneSkill.BulletVisual, (r, v) => Bolt(r, v, new Color(1f, 0.85f, 0.5f), 0.4f, 0.28f));
            Build("caitlyn_tea", (r, v) => RisingAura(r, v, new Color(1f, 0.6f, 0.8f), new Color(0.6f, 1f, 0.7f)));
            Build("haste", (r, v) => RisingAura(r, v, new Color(1f, 0.65f, 0.25f), new Color(1f, 0.95f, 0.55f)));
            Build("jinx_rocket", (r, v) => Bolt(r, v, new Color(1f, 0.4f, 0.8f), 0.55f, 0.45f));
            Build("teemo_bomb", (r, v) => Bolt(r, v, new Color(0.6f, 1f, 0.3f), 0.5f, 0.4f));
            Build("irelia_dragon", IreliaDragon);
            Build("katarina_spin", KatarinaSpin);
            Build("malphite_slam", MalphiteSlam);
            Build("yasuo_tornado", YasuoTornado);
            Build("yasuo_slash", (r, v) => SlashBurst(r, v, new Color(0.85f, 0.95f, 1f), 2.4f));
            Build("yone_divine", (r, v) => SlashBurst(r, v, new Color(0.45f, 0.75f, 1f), 2.4f));
            Build("yone_devil", (r, v) => SlashBurst(r, v, new Color(1f, 0.25f, 0.3f), 2.4f));

            // status marks (BattleUnitView keeps them on while the unit has the status)
            Build(StatusVisuals.Stun, StatusStun);
            Build(StatusVisuals.Silence, (r, v) => StatusIcon(r, v, "ban", new Color(0.78f, 0.5f, 1f), new Vector3(-0.6f, -0.25f, 0)));
            Build(StatusVisuals.AntiHeal, (r, v) => StatusIcon(r, v, "cross", new Color(1f, 0.22f, 0.2f), new Vector3(0.6f, -0.25f, 0)));
            Build(StatusVisuals.Invulnerable, (r, v) => Bubble(r, v, new Color(1f, 0.85f, 0.35f)));
        }

        // ------------------------------------------------------------------ status marks (origin = top of the head;
        // the health bar covers what is just above it from the board camera, so they sit around the head)

        /// <summary>Three yellow stars circling over the head.</summary>
        static void StatusStun(GameObject root, SkillVfx vfx) {
            var stars = Emitter(root, "Stars", Mat("star"), loop: true);
            var main = stars.main;
            main.startLifetime = 1f;
            main.startSize = 0.45f;
            main.startColor = new Color(1f, 0.9f, 0.25f);
            var emission = stars.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 3) });
            var shape = stars.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;
            shape.radiusThickness = 0;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.rotation = new Vector3(90, 0, 0);
            var velocity = stars.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.y = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.z = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(5, 5);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0, 0);
            Steady(stars);
            stars.transform.localPosition = Vector3.up * -0.3f;
            var halo = Emitter(root, "Halo", Mat("ring"), loop: true);
            main = halo.main;
            main.startLifetime = 1f;
            main.startSize = 1.2f;
            main.startColor = new Color(1f, 0.85f, 0.3f, 0.45f);
            emission = halo.emission;
            emission.rateOverTime = 2;
            halo.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            Steady(halo);
            halo.transform.localPosition = Vector3.up * -0.3f;
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.3f);
        }

        /// <summary>A pulsing icon over the head (silence, anti-heal), alpha-blended so it reads on any ground.</summary>
        static void StatusIcon(GameObject root, SkillVfx vfx, string texture, Color color, Vector3 position) {
            var glow = Emitter(root, "Glow", Mat("glow"), loop: true);
            var main = glow.main;
            main.startLifetime = 1f;
            main.startSize = 1.2f;
            main.startColor = new Color(color.r, color.g, color.b, 0.5f);
            var emission = glow.emission;
            emission.rateOverTime = 2;
            Steady(glow);
            glow.transform.localPosition = position;
            var icon = Emitter(root, "Icon", Mat(texture, additive: false), loop: true);
            main = icon.main;
            main.startLifetime = 1f;
            main.startSize = 0.7f;
            main.startColor = color;
            emission = icon.emission;
            emission.rateOverTime = 2;
            var pulse = icon.sizeOverLifetime;
            pulse.enabled = true;
            pulse.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.85f), new Keyframe(0.5f, 1.05f), new Keyframe(1, 0.85f)));
            Steady(icon);
            icon.transform.localPosition = position;
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.3f);
        }

        /// <summary>Fades in / out at the ends of each particle only: with two particles a second (lifetime 1) the mark
        /// stays on without blinking.</summary>
        static void Steady(ParticleSystem ps) {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.85f), new GradientAlphaKey(0, 1) });
            col.color = gradient;
        }

        // ------------------------------------------------------------------ shared shapes

        /// <summary>A flash and sparks at chest height; skills add an expanding ring.</summary>
        static void Hit(GameObject root, SkillVfx vfx, Color color, float size, bool big) {
            var flash = Burst(root, "Flash", Mat("spark"), 1, 0.16f, size * 1.3f, color, speed: 0, radius: 0, up: 0);
            flash.transform.localPosition = Vector3.up * 1f;
            var sparks = Burst(root, "Sparks", Mat("spark"), big ? 10 : 5, 0.3f, size * 0.22f, color, speed: big ? 5 : 3.5f, radius: 0.1f, up: 0);
            sparks.transform.localPosition = Vector3.up * 1f;
            if (big) {
                var ring = Burst(root, "Ring", Mat("ring"), 1, 0.28f, size * 1.6f, color, speed: 0, radius: 0, up: 0);
                ring.transform.localPosition = Vector3.up * 1f;
                var grow = ring.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.3f, 1, 1));
            }
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.4f);
        }

        /// <summary>A crescent cut (random angle) with sparks at chest height.</summary>
        static void SlashBurst(GameObject root, SkillVfx vfx, Color color, float size) {
            var slash = Burst(root, "Slash", Mat("slash"), 2, 0.26f, size, color, speed: 0, radius: 0, up: 0);
            slash.transform.localPosition = Vector3.up * 1f;
            var sparks = Burst(root, "Sparks", Mat("spark"), 10, 0.35f, 0.28f, Color.white, speed: 5, radius: 0.2f, up: 0);
            sparks.transform.localPosition = Vector3.up * 1f;
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.4f);
        }

        /// <summary>A fast blade wave along the beam line (Aatrox's cuts).</summary>
        static void SlashBeam(GameObject root, SkillVfx vfx, Color color) {
            Beam(root, vfx, Mat("beam", scroll: new Vector2(0, -9), color: color), 1.1f);
            var flash = Burst(root, "Flash", Mat("slash"), 1, 0.3f, 2.6f, (Color)(Vector4)color, speed: 0, radius: 0, up: 0);
            var main = flash.main;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            Set(vfx, "beamFadeIn", 0.04f);
            Set(vfx, "beamFadeOut", 0.3f);
        }

        /// <summary>A heavy hit: big star, ring on the ground, sparks.</summary>
        static void Impact(GameObject root, SkillVfx vfx, Color color) {
            var star = Burst(root, "Impact", Mat("spark"), 1, 0.3f, 2.6f, color, speed: 0, radius: 0, up: 0);
            star.transform.localPosition = Vector3.up * 1f;
            Ground(root, "Ring", Mat("ring"), 0.4f, Cell * 1.6f, color, grow: true);
            var sparks = Burst(root, "Sparks", Mat("spark"), 14, 0.4f, 0.3f, color, speed: 6, radius: 0.2f, up: 0);
            sparks.transform.localPosition = Vector3.up * 1f;
            Set(vfx, "height", 0f);
        }

        /// <summary>A pulsing shield bubble around a unit (looping while attached).</summary>
        static void Bubble(GameObject root, SkillVfx vfx, Color color) {
            var ring = Emitter(root, "Bubble", Mat("ring"), loop: true);
            var main = ring.main;
            main.startLifetime = 1.1f;
            main.startSize = 2.8f;
            main.startColor = color;
            var emission = ring.emission;
            emission.rateOverTime = 1.8f;
            Fade(ring, Color.white, Color.white, 0.3f);
            ring.transform.localPosition = Vector3.up * 1f;
            var glow = Emitter(root, "Glow", Mat("glow"), loop: true);
            main = glow.main;
            main.startLifetime = 1f;
            main.startSize = 3.2f;
            main.startColor = new Color(color.r, color.g, color.b, 0.35f);
            emission = glow.emission;
            emission.rateOverTime = 2;
            Fade(glow, Color.white, Color.white, 0.3f);
            glow.transform.localPosition = Vector3.up * 1f;
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.8f);
        }

        /// <summary>A glowing shot with a trail (travels).</summary>
        static void Bolt(GameObject root, SkillVfx vfx, Color color, float core, float trail) {
            Single(root, "Core", Mat("glow"), 0.6f, core, color);
            Trail(root, "Trail", Mat("glow"), 30, 0.3f, trail, color, color * 0.6f);
            Set(vfx, "linger", 0.35f);
        }

        /// <summary>Sparkles rising around a unit (looping while attached).</summary>
        static void RisingAura(GameObject root, SkillVfx vfx, Color a, Color b) {
            var sparkles = Emitter(root, "Sparkles", Mat("spark"), loop: true);
            var main = sparkles.main;
            main.startLifetime = 1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            var emission = sparkles.emission;
            emission.rateOverTime = 16;
            var shape = sparkles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.7f;
            shape.rotation = new Vector3(90, 0, 0);
            Rise(sparkles, 1.2f, 2.2f);
            Fade(sparkles, Color.white, Color.white, 0.2f);
            var pool = Emitter(root, "Glow", Mat("glow"), loop: true);
            main = pool.main;
            main.startLifetime = 1f;
            main.startSize = 2.2f;
            main.startColor = new Color(a.r, a.g, a.b, 0.3f);
            emission = pool.emission;
            emission.rateOverTime = 2;
            pool.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            pool.transform.localPosition = Vector3.up * 0.05f;
            Fade(pool, Color.white, Color.white, 0.3f);
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.8f);
        }

        // ------------------------------------------------------------------ hero recipes

        static void AkaliSmoke(GameObject root, SkillVfx vfx) {
            Burst(root, "Smoke", Mat("smoke", additive: false), 16, 1.2f, 1.8f, new Color(0.25f, 0.12f, 0.35f, 0.8f), speed: 1.5f,
                radius: Cell * 0.8f, up: 0.4f);
            Ground(root, "Ring", Mat("ring"), 0.5f, Across, new Color(0.7f, 0.35f, 1f), grow: true);
            Set(vfx, "height", 0f);
        }

        static void ZedShuriken(GameObject root, SkillVfx vfx) {
            var star = Single(root, "Star", Mat("spark"), 0.5f, 1.1f, new Color(1f, 0.25f, 0.25f));
            var spin = star.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(Mathf.PI * 8);
            Single(root, "Core", Mat("glow"), 0.5f, 0.7f, new Color(0.6f, 0.1f, 0.2f));
            Trail(root, "Trail", Mat("glow"), 30, 0.25f, 0.35f, new Color(0.9f, 0.15f, 0.2f), new Color(0.3f, 0.05f, 0.3f));
            Set(vfx, "linger", 0.3f);
        }

        static void ZedClone(GameObject root, SkillVfx vfx) {
            Burst(root, "Smoke", Mat("smoke", additive: false), 10, 0.9f, 1.4f, new Color(0.12f, 0.05f, 0.18f, 0.85f), speed: 1.5f,
                radius: 0.4f, up: 0.8f);
            var flash = Burst(root, "Flash", Mat("glow"), 1, 0.4f, 2.4f, new Color(0.6f, 0.2f, 1f), speed: 0, radius: 0, up: 0);
            flash.transform.localPosition = Vector3.up * 1f;
            Set(vfx, "height", 0f);
        }

        static void IreliaDragon(GameObject root, SkillVfx vfx) {
            var flames = Burst(root, "Flames", Mat("flame"), 22, 0.8f, 1.4f, new Color(0.7f, 0.3f, 1f), speed: 1.5f, radius: Cell * 0.6f,
                up: 2.5f, endColor: new Color(0.2f, 0.05f, 0.4f));
            flames.transform.localPosition = Vector3.up * 0.2f;
            Ground(root, "Ring", Mat("ring"), 0.5f, Cell * 2.2f, new Color(0.75f, 0.4f, 1f), grow: true);
            var flash = Burst(root, "Flash", Mat("spark"), 1, 0.3f, 3f, new Color(0.85f, 0.6f, 1f), speed: 0, radius: 0, up: 0);
            flash.transform.localPosition = Vector3.up * 1.2f;
            Set(vfx, "height", 0f);
        }

        static void KatarinaSpin(GameObject root, SkillVfx vfx) {
            var blades = Emitter(root, "Blades", Mat("slash"), loop: true);
            var main = blades.main;
            main.startLifetime = 0.35f;
            main.startSize = 3.6f;
            main.startColor = new Color(1f, 0.3f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = blades.emission;
            emission.rateOverTime = 10;
            var spin = blades.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-Mathf.PI * 6);
            blades.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            blades.transform.localPosition = Vector3.up * 0.8f;
            Fade(blades, Color.white, Color.white, 0.2f);
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.4f);
        }

        static void MalphiteSlam(GameObject root, SkillVfx vfx) {
            Ground(root, "Shockwave", Mat("ring"), 0.6f, Across, new Color(1f, 0.75f, 0.45f), grow: true);
            Burst(root, "Dust", Mat("smoke", additive: false), 14, 1f, 1.4f, new Color(0.5f, 0.42f, 0.33f, 0.75f), speed: 3f, radius: 0.5f,
                up: 0.4f);
            var flash = Burst(root, "Flash", Mat("spark"), 1, 0.25f, 3f, new Color(1f, 0.85f, 0.5f), speed: 0, radius: 0, up: 0);
            flash.transform.localPosition = Vector3.up * 0.6f;
            Set(vfx, "height", 0f);
        }

        static void YasuoTornado(GameObject root, SkillVfx vfx) {
            var swirl = Burst(root, "Tornado", Mat("swirl"), 8, 0.9f, 2.2f, new Color(0.75f, 0.9f, 1f), speed: 0, radius: 0.2f, up: 3f);
            var spin = swirl.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(Mathf.PI * 4);
            Ground(root, "Ring", Mat("ring"), 0.5f, Cell * 1.8f, new Color(0.8f, 0.95f, 1f), grow: true);
            Set(vfx, "height", 0f);
        }
    }
}
