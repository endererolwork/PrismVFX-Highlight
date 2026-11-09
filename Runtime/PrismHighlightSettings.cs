using System;
using UnityEngine;
using UnityEngine.Audio;

namespace PrismFX
{
    public enum PrismVisibility { Normal = 0, AlwaysOnTop = 1, OnlyWhenOccluded = 2 }
    public enum PrismWidthMode { Pixels = 0, WorldCentimeters = 1 }
    public enum PrismNormalMode { MeshNormals = 0, FromCenter = 1 }
    public enum PrismHullCull { Off = 0, Front = 1 }
    public enum PrismDashMode { Solid = 0, Radial = 1, LinearScreen = 2, LinearWorld = 3 }
    public enum PrismOverlayMapping { MeshUV = 0, TriplanarWorld = 1, Screen = 2 }
    public enum PrismHologramMode { Overlay = 0, Replace = 1 }
    public enum PrismHologramActivation { WithHighlight = 0, Always = 1 }
    public enum PrismScanSpace { World = 0, Object = 1, Screen = 2 }
    public enum PrismSeeThroughMode { Never = 0, WhenHighlighted = 1, AlwaysWhenOccluded = 2 }
    public enum PrismHitMode { Flash = 0, Rim = 1, FlashAndRim = 2 }
    public enum PrismTrigger { Manual = 0, MouseHover = 1 }
    public enum PrismPreset { SciFiSelect, Danger, GhostHologram, SubtleHover, Objective }

    // Shared by PrismHighlight and PrismHighlightProfile, so keep it plain serializable data.
    [Serializable]
    public class PrismHighlightSettings
    {
        // General
        [Tooltip("Include MeshRenderers / SkinnedMeshRenderers in children.")]
        public bool includeChildren = true;
        [Tooltip("Seconds.")]
        [Min(0)] public float fadeInDuration = 0.15f;
        [Tooltip("Seconds.")]
        [Min(0)] public float fadeOutDuration = 0.25f;
        [Tooltip("Pixels keeps the same thickness at any distance (what you want in VR).")]
        public PrismWidthMode widthMode = PrismWidthMode.Pixels;
        [Tooltip("Use FromCenter if the outline breaks at hard edges.")]
        public PrismNormalMode normalMode = PrismNormalMode.MeshNormals;
        [Tooltip("Front for closed meshes. Use Off for planes / open meshes.")]
        public PrismHullCull hullCull = PrismHullCull.Front;
        [Tooltip("Applies to overlay, inner glow and hit flash.")]
        public PrismVisibility surfaceVisibility = PrismVisibility.Normal;

        // Outline
        [Range(0, 1)] public float outline = 1f;
        [ColorUsage(true, true)] public Color outlineColor = new Color(0.25f, 0.9f, 1f, 1f);
        [Tooltip("Pixels modunda piksel, World modunda santimetre.")]
        [Range(0, 20)] public float outlineWidth = 2.5f;
        public PrismVisibility outlineVisibility = PrismVisibility.Normal;
        [Tooltip("Radial dashes run around the silhouette, Linear ones are straight stripes.")]
        public PrismDashMode outlineDashMode = PrismDashMode.Solid;
        [Tooltip("Dashes per loop (Radial) or density (Linear).")]
        [Range(1, 128)] public float dashCount = 32f;
        [Tooltip("How much of each dash period is filled.")]
        [Range(0.05f, 0.95f)] public float dashFill = 0.55f;
        [Tooltip("Negative flows the other way.")]
        [Range(-10, 10)] public float dashSpeed = 0.5f;
        [Tooltip("Stripe angle in degrees (Linear modes).")]
        [Range(0, 180)] public float dashAngle = 45f;
        [Range(0, 1)] public float dashSoftness = 0.05f;

        // Outer Glow
        [Range(0, 5)] public float glow = 0.6f;
        [ColorUsage(true, true)] public Color glowColor = new Color(0.25f, 0.9f, 1f, 1f);
        [Tooltip("Pixels modunda piksel, World modunda santimetre.")]
        [Range(0, 40)] public float glowWidth = 8f;
        [Tooltip("Each pass is one more draw per submesh. 2 is fine on Quest.")]
        [Range(1, 4)] public int glowPasses = 4;
        public PrismVisibility glowVisibility = PrismVisibility.Normal;
        [Tooltip("Reuse the outline dash pattern.")]
        public bool glowDashed = false;
        [Tooltip("Hides banding on 8-bit targets.")]
        public bool glowDithering = true;
        [Range(0, 1)] public float glowDitherStrength = 0.5f;
        [Tooltip("Hz, 0 = no pulse.")]
        [Range(0, 10)] public float glowPulseSpeed = 1f;
        [Range(0, 1)] public float glowPulseAmount = 0.3f;

        // Inner Glow
        [Range(0, 5)] public float innerGlow = 0.6f;
        [ColorUsage(true, true)] public Color innerGlowColor = new Color(0.6f, 0.95f, 1f, 1f);
        [Tooltip("Higher = thinner rim.")]
        [Range(0.5f, 8f)] public float innerGlowPower = 2.5f;

        // Overlay
        [Range(0, 1)] public float overlay = 0f;
        [ColorUsage(true, true)] public Color overlayColor = new Color(1f, 0.9f, 0.1f, 0.5f);
        public Texture2D overlayTexture;
        public PrismOverlayMapping overlayMapping = PrismOverlayMapping.TriplanarWorld;
        [Min(0.001f)] public float overlayTextureScale = 1f;
        public Vector2 overlayScroll = Vector2.zero;
        [Tooltip("0 = alpha blend, 1 = additive.")]
        [Range(0, 1)] public float overlayBlending = 0.5f;
        [Tooltip("Lowest intensity while pulsing.")]
        [Range(0, 1)] public float overlayMinIntensity = 0.5f;
        [Tooltip("Hz, 0 = no pulse.")]
        [Range(0, 10)] public float overlayAnimationSpeed = 1f;

        // Hologram
        public bool hologram = false;
        [Tooltip("Replace swaps the renderer materials in play mode (good for ghost previews).")]
        public PrismHologramMode hologramMode = PrismHologramMode.Overlay;
        [Tooltip("Always = on while the component is enabled.")]
        public PrismHologramActivation hologramActivation = PrismHologramActivation.WithHighlight;
        [ColorUsage(true, true)] public Color hologramColor = new Color(0.2f, 0.8f, 1f, 1f);
        [ColorUsage(true, true)] public Color hologramRimColor = new Color(0.6f, 1f, 1f, 1f);
        public Texture2D hologramTexture;
        [Range(0, 1)] public float hologramTextureInfluence = 0f;
        [Range(0, 1)] public float hologramAlpha = 0.35f;
        [Range(0.5f, 8f)] public float hologramFresnelPower = 2.5f;
        [Range(0, 5)] public float hologramRimIntensity = 1.5f;
        public PrismScanSpace hologramScanSpace = PrismScanSpace.World;
        [Tooltip("Lines per meter (World/Object space).")]
        [Min(0)] public float scanlineDensity = 40f;
        public float scanlineSpeed = 1f;
        [Range(0.01f, 1f)] public float scanlineThickness = 0.25f;
        [Range(0, 3)] public float scanlineIntensity = 1f;
        [Min(0)] public float fineLineDensity = 300f;
        [Range(0, 1)] public float fineLineIntensity = 0.15f;
        [Tooltip("The bright band that sweeps over the object.")]
        [Min(0)] public float bandFrequency = 0.5f;
        public float bandSpeed = 0.5f;
        [Range(0.001f, 0.5f)] public float bandWidth = 0.08f;
        [Range(0, 5)] public float bandIntensity = 2f;
        [Min(0)] public float flickerSpeed = 12f;
        [Range(0, 1)] public float flickerAmount = 0.15f;
        [Tooltip("Keep it small in VR, big offsets are uncomfortable.")]
        [Range(0, 0.5f)] public float glitchAmount = 0.02f;
        [Min(0)] public float glitchFrequency = 15f;
        [Min(0)] public float glitchSpeed = 6f;
        [Range(0, 1)] public float glitchProbability = 0.08f;
        [Tooltip("Stops inner faces from stacking up in Replace mode.")]
        public bool hologramDepthPrepass = true;
        public bool hologramDoubleSided = false;
        [Tooltip("Off = regular alpha blending.")]
        public bool hologramAdditive = true;

        // Target
        public bool target = false;
        [Tooltip("Leave empty to use the built-in reticle.")]
        public Texture2D targetTexture;
        [ColorUsage(true, true)] public Color targetColor = new Color(0.4f, 1f, 1f, 1f);
        [Tooltip("Defaults to the bounds center.")]
        public Transform targetCenter;
        public float targetRotationSpeed = 50f;
        [Min(0)] public float targetInitialScale = 4f;
        [Min(0)] public float targetEndScale = 1.5f;
        public bool targetScaleToBounds = true;
        [Tooltip("Raycasts down and lays the reticle on the ground.")]
        public bool targetAlignToGround = false;
        [Min(0)] public float targetGroundOffset = 0.02f;
        public LayerMask groundMask = ~0;
        [Min(0)] public float targetTransitionDuration = 0.5f;
        [Tooltip("0 = stay while highlighted.")]
        [Min(0)] public float targetStayDuration = 0f;
        public PrismVisibility targetVisibility = PrismVisibility.AlwaysOnTop;
        [Range(0, 1)] public float targetPulse = 0.15f;

        // See-Through
        public PrismSeeThroughMode seeThroughMode = PrismSeeThroughMode.Never;
        [Range(0, 1)] public float seeThroughIntensity = 0.8f;
        [ColorUsage(true, true)] public Color seeThroughColor = new Color(1f, 0.25f, 0.2f, 1f);
        [Tooltip("0 = flat color, 1 = shaded so the shape reads.")]
        [Range(0, 1)] public float seeThroughColorBlend = 0.5f;
        [Range(0, 1)] public float seeThroughNoise = 0.5f;
        [Tooltip("Rim highlight.")]
        [Range(0, 1)] public float seeThroughBorder = 0.5f;

        // Hit FX
        [Range(0, 1)] public float hitIntensity = 1f;
        public PrismHitMode hitMode = PrismHitMode.FlashAndRim;
        [Min(0.01f)] public float hitDuration = 0.25f;
        [ColorUsage(true, true)] public Color hitColor = Color.white;
        [Tooltip("Used by Error().")]
        [ColorUsage(true, true)] public Color errorColor = new Color(1f, 0.15f, 0.1f, 1f);

        // Audio
        public bool audioEnabled = true;
        public AudioClip highlightOnClip;
        public AudioClip highlightOffClip;
        public AudioClip hitClip;
        public AudioClip selectClip;
        public AudioClip errorClip;
        [Tooltip("Loops while the hologram is visible.")]
        public AudioClip hologramLoopClip;
        [Range(0, 1)] public float volume = 0.7f;
        [Range(0, 1)] public float loopVolume = 0.35f;
        [Range(0, 0.5f)] public float pitchVariation = 0.05f;
        [Tooltip("1 = fully 3D (use this in VR).")]
        [Range(0, 1)] public float spatialBlend = 1f;
        public AudioMixerGroup outputGroup;
        [Tooltip("Stops sound spam when hovering quickly.")]
        [Min(0)] public float minSoundInterval = 0.08f;

        public void CopyFrom(PrismHighlightSettings other)
        {
            if (other == null || ReferenceEquals(other, this)) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }

        // Presets only touch the look. Audio and general settings are left alone.
        public void ApplyPreset(PrismPreset preset)
        {
            outlineDashMode = PrismDashMode.Solid; glowDashed = false; overlay = 0f; hologram = false;
            target = false; seeThroughMode = PrismSeeThroughMode.Never; outlineVisibility = PrismVisibility.Normal;
            glowVisibility = PrismVisibility.Normal; targetAlignToGround = false; widthMode = PrismWidthMode.Pixels;

            switch (preset)
            {
                case PrismPreset.SciFiSelect:
                    var cyan = new Color(0.25f, 0.9f, 1f, 1f);
                    outline = 1f; outlineColor = cyan; outlineWidth = 2.5f;
                    glow = 0.8f; glowColor = cyan * 1.5f; glowWidth = 10f; glowPasses = 4; glowPulseSpeed = 0.8f; glowPulseAmount = 0.25f;
                    innerGlow = 0.6f; innerGlowColor = new Color(0.6f, 0.95f, 1f, 1f); innerGlowPower = 2.5f;
                    target = true; targetColor = cyan; targetInitialScale = 3f; targetEndScale = 1.3f; targetStayDuration = 0f;
                    seeThroughMode = PrismSeeThroughMode.WhenHighlighted; seeThroughColor = cyan; seeThroughIntensity = 0.6f;
                    hitColor = Color.white;
                    break;

                case PrismPreset.Danger:
                    var red = new Color(1f, 0.18f, 0.12f, 1f);
                    outline = 1f; outlineColor = red; outlineWidth = 3f;
                    outlineDashMode = PrismDashMode.Radial; dashCount = 24f; dashFill = 0.6f; dashSpeed = 1.2f;
                    glow = 1.2f; glowColor = red * 2f; glowWidth = 12f; glowPasses = 4; glowPulseSpeed = 2.5f; glowPulseAmount = 0.6f;
                    innerGlow = 0.5f; innerGlowColor = red; innerGlowPower = 2f;
                    overlay = 0.35f; overlayColor = new Color(1f, 0.1f, 0.05f, 0.6f); overlayAnimationSpeed = 2.5f; overlayMinIntensity = 0.2f; overlayBlending = 0.7f;
                    hitColor = red;
                    break;

                case PrismPreset.GhostHologram:
                    var holo = new Color(0.2f, 0.8f, 1f, 1f);
                    outline = 0.8f; outlineColor = holo; outlineWidth = 1.5f;
                    outlineDashMode = PrismDashMode.LinearScreen; dashCount = 60f; dashFill = 0.5f; dashSpeed = 0.6f; dashAngle = 90f;
                    glow = 0.35f; glowColor = holo; glowWidth = 6f; glowPasses = 3; glowPulseSpeed = 0.5f; glowPulseAmount = 0.3f;
                    innerGlow = 0f;
                    hologram = true; hologramMode = PrismHologramMode.Replace; hologramActivation = PrismHologramActivation.Always;
                    hologramColor = holo; hologramAlpha = 0.3f; scanlineDensity = 40f; bandIntensity = 2f; glitchAmount = 0.015f;
                    break;

                case PrismPreset.SubtleHover:
                    outline = 0.8f; outlineColor = new Color(1f, 1f, 1f, 0.85f); outlineWidth = 1.5f;
                    glow = 0.25f; glowColor = Color.white; glowWidth = 6f; glowPasses = 2; glowPulseSpeed = 0f;
                    innerGlow = 0.3f; innerGlowColor = Color.white; innerGlowPower = 3f;
                    break;

                case PrismPreset.Objective:
                    var gold = new Color(1f, 0.82f, 0.15f, 1f);
                    outline = 1f; outlineColor = gold; outlineWidth = 2.5f;
                    outlineDashMode = PrismDashMode.LinearScreen; dashCount = 40f; dashFill = 0.55f; dashSpeed = 0.8f; dashAngle = 45f;
                    glow = 0.9f; glowColor = gold * 1.5f; glowWidth = 10f; glowPasses = 4; glowPulseSpeed = 1.2f; glowPulseAmount = 0.5f;
                    innerGlow = 0.4f; innerGlowColor = gold; innerGlowPower = 2.5f;
                    target = true; targetColor = gold; targetAlignToGround = true; targetInitialScale = 3f; targetEndScale = 1.2f;
                    seeThroughMode = PrismSeeThroughMode.AlwaysWhenOccluded; seeThroughColor = gold; seeThroughIntensity = 0.5f;
                    hitColor = gold;
                    break;
            }
        }
    }
}
