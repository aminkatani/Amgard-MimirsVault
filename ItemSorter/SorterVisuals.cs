using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ItemSorter
{
    /// <summary>Colours and effect settings for one kind of magical box.</summary>
    internal class Palette
    {
        public string Name;
        public Color WoodTint;
        public float TintAmount;
        public bool SkipMetal;            // leave iron bands untinted
        public Color Glow;
        public float GlowEmission;
        public Color ParticleTint;        // shader tint of the particle material
        public Gradient ParticleColor;    // colour/alpha over a particle's life
        public float ParticleRate;
        public float ParticleRateActive;
        public Vector2 Lifetime;
        public Vector2 Speed;
        public Vector2 Size;
        public float Gravity;
        public float NoiseStrength;
        public bool EmitUpward;
        public Vector3 EmitterPosition;
        public Vector3 EmitterScale;
        public float LightRange;
        public float LightIntensity;
        public bool Pulse;                // emissive glow breathes
        public bool Flicker;              // light flickers like fire
        public bool RoundParticles;       // soft dots instead of stars

        // No C# tuples in this project: they need System.ValueTuple.dll, which Valheim doesn't ship.
        private static Gradient Fade(params GradientColorKey[] colors)
        {
            var g = new Gradient();
            g.SetKeys(colors,
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private static GradientColorKey Key(Color color, float time) => new GradientColorKey(color, time);

        /// <summary>The Item Sorter: light-blue wood with drifting blue sparkles.</summary>
        public static readonly Palette Frost = new Palette
        {
            Name = "Frost",
            WoodTint = new Color(0.62f, 0.84f, 1f),
            TintAmount = 0.7f,
            Glow = new Color(0.25f, 0.55f, 1f),
            GlowEmission = 0.35f,
            ParticleTint = new Color(0.6f, 0.8f, 1f, 0.8f),
            ParticleColor = Fade(Key(new Color(0.75f, 0.92f, 1f), 0f), Key(Color.white, 1f)),
            ParticleRate = 14f,
            ParticleRateActive = 14f,
            Lifetime = new Vector2(1.5f, 3f),
            Speed = new Vector2(0.02f, 0.12f),
            Size = new Vector2(0.04f, 0.1f),
            Gravity = -0.02f,
            EmitterPosition = new Vector3(0f, 0.55f, 0f),
            EmitterScale = new Vector3(1.4f, 0.8f, 0.9f),
            LightRange = 3f,
            LightIntensity = 0.9f,
        };

        /// <summary>The Smelter Box: crimson wood breathing like hot coals, rising embers, forge-fire light.</summary>
        public static readonly Palette Ember = new Palette
        {
            Name = "Ember",
            WoodTint = new Color(0.75f, 0.12f, 0.08f),
            TintAmount = 0.75f,
            SkipMetal = true,
            Glow = new Color(1f, 0.3f, 0.08f),
            GlowEmission = 0.45f,
            ParticleTint = new Color(0.5f, 0.5f, 0.5f, 0.5f),
            ParticleColor = Fade(Key(new Color(1f, 0.8f, 0.35f), 0f), Key(new Color(1f, 0.3f, 0.05f), 0.5f), Key(new Color(0.6f, 0.05f, 0f), 1f)),
            ParticleRate = 20f,
            ParticleRateActive = 32f,
            Lifetime = new Vector2(0.8f, 1.4f),
            Speed = new Vector2(0.3f, 0.7f),
            Size = new Vector2(0.03f, 0.07f),
            Gravity = -0.05f,
            NoiseStrength = 0.25f,
            EmitUpward = true,
            EmitterPosition = new Vector3(0f, 0.95f, 0f),
            EmitterScale = new Vector3(1.4f, 0.7f, 0.05f), // after rotating Z up: width, depth, height
            LightRange = 3f,
            LightIntensity = 1.1f,
            Pulse = true,
            Flicker = true,
            RoundParticles = true,
        };
    }

    /// <summary>Live effects on one box, animated every frame by its behaviour.</summary>
    internal class VisualHandle
    {
        public Palette Palette;
        public ParticleSystem Particles;
        public Light Light;
        public readonly List<Material> GlowMaterials = new List<Material>();
        private readonly float _seed = Random.value * 100f;

        /// <param name="active">Whether the box is currently working (e.g. a smelter in range is smelting).</param>
        public void Tick(bool active)
        {
            float t = Time.time;
            if (Palette.Pulse && GlowMaterials.Count > 0)
            {
                float breathe = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 3f);
                var glow = Palette.Glow * Palette.GlowEmission * (0.5f + breathe) * (active ? 1.4f : 1f);
                foreach (var material in GlowMaterials) material.SetColor("_EmissionColor", glow);
            }
            if (Palette.Flicker && Light != null)
            {
                float flicker = 0.8f + 0.4f * Mathf.PerlinNoise(t * 6f, _seed);
                Light.intensity = Palette.LightIntensity * flicker * (active ? 1.25f : 1f);
            }
            if (Particles != null && Palette.ParticleRateActive != Palette.ParticleRate)
            {
                var emission = Particles.emission;
                emission.rateOverTime = active ? Palette.ParticleRateActive : Palette.ParticleRate;
            }
        }
    }

    /// <summary>
    /// Makes a box look magical: tinted glowing wood, floating particles and a soft light.
    /// Applied per instance when the box loads, so boxes built with older versions update too.
    /// </summary>
    internal static class SorterVisuals
    {
        private static readonly Dictionary<string, Material> ParticleMaterials = new Dictionary<string, Material>();
        private static bool _particleShaderMissing;

        /// <summary>
        /// Never throws: a problem with the looks is logged and the box keeps working without them.
        /// </summary>
        internal static VisualHandle Apply(GameObject root, Func<Palette> palette)
        {
            try
            {
                var p = palette();
                var handle = new VisualHandle { Palette = p };
                TintWood(root, p, handle);
                if (Plugin.Sparkles.Value) handle.Particles = AddParticles(root.transform, p);
                if (Plugin.Glow.Value) handle.Light = AddLight(root.transform, p);
                return handle;
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Jotunn.Logger.LogError($"Box visuals failed, boxes will work without them: {e}");
                }
                return null;
            }
        }

        private static bool _loggedFailure;

        private static void TintWood(GameObject root, Palette palette, VisualHandle handle)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                foreach (var material in renderer.materials)
                {
                    string name = material.name.ToLowerInvariant();
                    if (palette.SkipMetal && (name.Contains("metal") || name.Contains("iron"))) continue;

                    if (material.HasProperty("_Color"))
                    {
                        material.color = Color.Lerp(material.color, palette.WoodTint, palette.TintAmount);
                    }
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", palette.Glow * palette.GlowEmission);
                        handle.GlowMaterials.Add(material);
                    }
                }
            }
        }

        private static ParticleSystem AddParticles(Transform parent, Palette palette)
        {
            var material = GetParticleMaterial(palette);
            if (material == null) return null;

            // Build inactive so the system doesn't start playing with default settings.
            var go = new GameObject($"ItemSorter_{palette.Name}Particles");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = palette.EmitterPosition;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 80;
            main.startLifetime = new ParticleSystem.MinMaxCurve(palette.Lifetime.x, palette.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(palette.Speed.x, palette.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(palette.Size.x, palette.Size.y);
            main.startColor = Color.white;
            main.gravityModifier = palette.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = palette.ParticleRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = palette.EmitterScale;
            if (palette.EmitUpward) shape.rotation = new Vector3(-90f, 0f, 0f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = palette.ParticleColor;

            // Twinkle: size pulses over the particle's life.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(0.5f, 0.5f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.2f)));

            if (palette.NoiseStrength > 0f)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = palette.NoiseStrength;
                noise.frequency = 0.8f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            go.SetActive(true);
            return ps;
        }

        private static Light AddLight(Transform parent, Palette palette)
        {
            var go = new GameObject($"ItemSorter_{palette.Name}Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = palette.Glow;
            light.range = palette.LightRange;
            light.intensity = palette.LightIntensity;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Material GetParticleMaterial(Palette palette)
        {
            if (ParticleMaterials.TryGetValue(palette.Name, out var cached)) return cached;
            if (_particleShaderMissing) return null;

            string[] preferred =
            {
                "Legacy Shaders/Particles/Additive",
                "Particles/Additive",
                "Mobile/Particles/Additive",
                "Particles/Standard Unlit",
            };
            var shader = preferred.Select(Shader.Find).FirstOrDefault(s => s != null)
                         ?? Resources.FindObjectsOfTypeAll<Shader>()
                             .FirstOrDefault(s => s.name.Contains("Particles") && s.name.Contains("Add"));
            if (shader == null)
            {
                _particleShaderMissing = true;
                Jotunn.Logger.LogWarning("No particle shader found; box particles disabled.");
                return null;
            }

            var material = new Material(shader)
            {
                mainTexture = palette.RoundParticles ? MakeDotTexture() : MakeSparkleTexture(),
                color = Color.white,
            };
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", palette.ParticleTint);
            ParticleMaterials[palette.Name] = material;
            return material;
        }

        /// <summary>A small four-pointed star with a soft round core.</summary>
        private static Texture2D MakeSparkleTexture()
        {
            return MakeTexture((dx, dy) =>
            {
                float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                float rays = Mathf.Clamp01(1f - dx * 8f) * (1f - dy) + Mathf.Clamp01(1f - dy * 8f) * (1f - dx);
                return Mathf.Clamp01(core + rays * 0.8f);
            });
        }

        /// <summary>A soft glowing dot, used for embers.</summary>
        private static Texture2D MakeDotTexture()
        {
            return MakeTexture((dx, dy) =>
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) + Mathf.Clamp01(1f - d * 3f);
            });
        }

        private static Texture2D MakeTexture(System.Func<float, float, float> alpha)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a = Mathf.Clamp01(alpha(Mathf.Abs(x - c) / c, Mathf.Abs(y - c) / c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
