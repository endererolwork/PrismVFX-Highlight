using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace PrismFX
{
    /// <summary>
    /// Highlight / selection effects for one object (and its children).
    /// Drawing happens in PrismHighlightFeature; the object's own materials are left alone
    /// except in hologram Replace mode.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("PrismFX/Prism Highlight")]
    public partial class PrismHighlight : MonoBehaviour
    {
        internal static readonly List<PrismHighlight> Active = new List<PrismHighlight>(32);

        // Set by the renderer feature. Must be one of URP's user bits (1, 2, 4, 8).
        public static int StencilBit { get; internal set; } = 8;
        public static IReadOnlyList<PrismHighlight> ActiveHighlights => Active;

        [Tooltip("Optional shared settings asset.")]
        public PrismHighlightProfile profile;
        [Tooltip("Copy settings from the profile in OnEnable.")]
        public bool syncWithProfile = false;
        public PrismHighlightSettings settings = new PrismHighlightSettings();

        [SerializeField] bool _highlighted;
        public PrismTrigger trigger = PrismTrigger.Manual;
        [Tooltip("Calls Select() on click (MouseHover only).")]
        public bool selectOnClick = true;
        [Tooltip("Show the effect in the Scene view while editing.")]
        public bool previewInEditor = true;

        public UnityEvent onHighlightStart = new UnityEvent();
        public UnityEvent onHighlightEnd = new UnityEvent();
        public UnityEvent onHit = new UnityEvent();

        [SerializeField, HideInInspector] Shader _highlightShader;
        [SerializeField, HideInInspector] Shader _hologramShader;

        const string HighlightShaderName = "Hidden/PrismFX/Highlight";
        const string HologramShaderName = "PrismFX/Hologram";

        // runtime state
        Material _mat, _holoMat;
        readonly List<Renderer> _renderers = new List<Renderer>(8);
        readonly List<int> _subMeshCounts = new List<int>(8);
        bool[] _drawable = new bool[8];
        Material[][] _originalMaterials;
        bool _replaced, _registered, _dirty = true, _lastHighlighted;
        float _fade, _sentSeeThrough = -1f, _sentHoloFade = -1f;
        Vector4 _fadeVec, _sentFadeVec = new Vector4(-1f, -1f, -1f, -1f);
        Vector4 _sentSpot0 = new Vector4(0f, 0f, 0f, -1f), _sentSpot1 = new Vector4(0f, 0f, 0f, -1f);
        float _hitStart = -1000f, _hitDuration = 0.25f, _hitIntensity = 1f;
        Color _hitColor = Color.white;
        PrismHitMode _hitMode = PrismHitMode.FlashAndRim;
        float _targetStart = -1000f, _targetAlpha, _targetGrow;
        Bounds _bounds;
        Vector3 _groundPoint, _groundNormal = Vector3.up;
        AudioSource _sfx, _loop;
        float _lastSfxTime = -10f;

        static readonly RaycastHit[] s_Hits = new RaycastHit[8];
        static readonly List<Renderer> s_TmpRenderers = new List<Renderer>(16);

        // looked up by pass name so reordering the shader doesn't break anything
        static int P_Mask = -1, P_Clear = -1, P_Outline = -1, P_Surface = -1, P_SeeThrough = -1, P_Target = -1, P_HoloForward = -1;
        static readonly int[] P_Glow = { -1, -1, -1, -1 };

        // public API
        public bool highlighted
        {
            get => _highlighted;
            set => SetHighlighted(value);
        }

        /// <summary>Current 0..1 highlight amount, including the fade.</summary>
        public float CurrentFade => _fade;
        public Bounds WorldBounds => _bounds;
        public IReadOnlyList<Renderer> Renderers => _renderers;
        public int RendererCount => _renderers.Count;

        public void SetHighlighted(bool value)
        {
            _highlighted = value;
            Tick(0f);
        }

        public static void RefreshAllMaterials()
        {
            for (int i = 0; i < Active.Count; i++) if (Active[i] != null) Active[i]._dirty = true;
        }

        public void ToggleHighlight() => SetHighlighted(!_highlighted);

        public void HitFX() => TriggerHit(settings.hitColor, settings.hitDuration, settings.hitIntensity, settings.hitMode, settings.hitClip);

        public void HitFX(Color color, float duration = 0.25f, float intensity = 1f)
            => TriggerHit(color, duration, intensity, settings.hitMode, settings.hitClip);

        public void Select() => TriggerHit(settings.hitColor, settings.hitDuration, settings.hitIntensity * 0.6f, PrismHitMode.Rim, settings.selectClip);

        /// <summary>Red flash + error sound. Handy for "wrong part" feedback.</summary>
        public void Error() => TriggerHit(settings.errorColor, Mathf.Max(settings.hitDuration, 0.4f), 1f, PrismHitMode.FlashAndRim, settings.errorClip);

        public void PlayTarget()
        {
            _targetStart = Now;
            Tick(0f);
        }

        /// <summary>Call this after changing settings from code.</summary>
        public void Refresh()
        {
            _dirty = true;
            Tick(0f);
        }

        public void ApplyPreset(PrismPreset preset)
        {
            settings.ApplyPreset(preset);
            Refresh();
        }

        public void ApplyProfile()
        {
            if (profile == null) return;
            settings.CopyFrom(profile.settings);
            RefreshRenderers();
            Refresh();
        }

        public void SaveToProfile()
        {
            if (profile == null) return;
            profile.settings.CopyFrom(settings);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(profile);
#endif
        }

        /// <summary>Call this if renderers were added or removed under the object.</summary>
        public void RefreshRenderers()
        {
            bool wasReplaced = _replaced;
            RestoreMaterials();

            _renderers.Clear();
            _subMeshCounts.Clear();
            s_TmpRenderers.Clear();

            if (settings.includeChildren) GetComponentsInChildren(true, s_TmpRenderers);
            else
            {
                var own = GetComponent<Renderer>();
                if (own != null) s_TmpRenderers.Add(own);
            }

            for (int i = 0; i < s_TmpRenderers.Count; i++)
            {
                var r = s_TmpRenderers[i];
                int subs = 0;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) subs = mf.sharedMesh.subMeshCount;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh != null) subs = smr.sharedMesh.subMeshCount;
                }
                if (subs == 0) continue;
                // a child with its own PrismHighlight owns that subtree
                if (r.GetComponentInParent<PrismHighlight>(true) != this) continue;
                _renderers.Add(r);
                _subMeshCounts.Add(subs);
            }
            s_TmpRenderers.Clear();

            if (_drawable.Length < _renderers.Count) _drawable = new bool[Mathf.NextPowerOfTwo(_renderers.Count)];
            RefreshColliders();
            _dirty = true;
            if (wasReplaced) UpdateHologramReplace();
        }

        // lifecycle
        void Reset()
        {
            FindShaders();
            settings = new PrismHighlightSettings();
#if UNITY_EDITOR
            settings.highlightOnClip = FindClip("PrismSFX_HighlightOn");
            settings.highlightOffClip = FindClip("PrismSFX_HighlightOff");
            settings.hitClip = FindClip("PrismSFX_Hit");
            settings.selectClip = FindClip("PrismSFX_Select");
            settings.errorClip = FindClip("PrismSFX_Error");
            settings.hologramLoopClip = FindClip("PrismSFX_HologramHum");
#endif
        }

        void OnValidate()
        {
            FindShaders();
            _dirty = true;
            // in play mode Update picks this up (can't AddComponent from OnValidate)
            if (!Application.isPlaying && isActiveAndEnabled && _mat != null) Tick(0f);
        }

        void OnEnable()
        {
            FindShaders();
            if (profile != null && syncWithProfile) settings.CopyFrom(profile.settings);
            CreateMaterials();
            RefreshRenderers();
            _hitColor = settings.hitColor;
            _lastHighlighted = _highlighted;
            if (_highlighted) _targetStart = Now;
            _fade = (_highlighted && !Application.isPlaying && previewInEditor) ? 1f : 0f;
            ResetSentValues();
            ResetProximity();
            _dirty = true;
            Tick(0f);
        }

        void OnDisable()
        {
            RestoreMaterials();
            Unregister();
            if (_loop != null && _loop.isPlaying) _loop.Stop();
            // destroy here rather than OnDestroy, otherwise domain reloads leak materials
            DestroyImmediateSafe(_mat);
            DestroyImmediateSafe(_holoMat);
            _mat = _holoMat = null;
        }

        void Update()
        {
            // idle objects bail out right away
            if (!_registered && !_replaced && !_highlighted && !_lastHighlighted && _fade <= 0f
                && settings.seeThroughMode != PrismSeeThroughMode.AlwaysWhenOccluded
                && !(settings.hologram && settings.hologramActivation == PrismHologramActivation.Always)
                && !HitActive && !settings.proximity && (_loop == null || !_loop.isPlaying))
                return;

            Tick(Application.isPlaying ? Time.deltaTime : 0f);
            if (Application.isPlaying) UpdateLoopAudio();
        }

        void OnMouseEnter() { if (trigger == PrismTrigger.MouseHover) SetHighlighted(true); }
        void OnMouseExit() { if (trigger == PrismTrigger.MouseHover) SetHighlighted(false); }
        void OnMouseDown() { if (trigger == PrismTrigger.MouseHover && selectOnClick) Select(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        // core update
        static float Now => Application.isPlaying ? Time.timeSinceLevelLoad : Time.realtimeSinceStartup;

        bool HitActive => Now - _hitStart < _hitDuration;

        float HoloFade
        {
            get
            {
                if (!settings.hologram) return 0f;
                return settings.hologramActivation == PrismHologramActivation.Always ? 1f : _fade;
            }
        }

        void Tick(float dt)
        {
            if (!isActiveAndEnabled) return;
            var s = settings;

            if (_highlighted != _lastHighlighted)
            {
                _lastHighlighted = _highlighted;
                OnHighlightChanged(_highlighted);
            }

            float target = _highlighted && (Application.isPlaying || previewInEditor) ? 1f : 0f;
            if (!Application.isPlaying) _fade = target;
            else if (_fade != target)
            {
                float d = target > _fade ? s.fadeInDuration : s.fadeOutDuration;
                _fade = d <= 0f ? target : Mathf.MoveTowards(_fade, target, dt / d);
            }

            UpdateTargetAnim();
            UpdateProximity(dt);
            _fadeVec = ProximityFade();

            bool holoOverlay = HoloFade > 0.001f && (s.hologramMode == PrismHologramMode.Overlay || !Application.isPlaying);
            bool needs = _fade > 0.001f || _prox > 0.001f || SpotActive || HitActive || _targetAlpha > 0.001f || holoOverlay
                         || s.seeThroughMode == PrismSeeThroughMode.AlwaysWhenOccluded;

            if (needs) Register(); else Unregister();
            if (Application.isPlaying) UpdateHologramReplace();
            if (!_registered && !_replaced) return;

            if (_mat == null) CreateMaterials();
            if (_mat == null) return;
            if (_dirty) ApplyStaticUniforms();

            if (NeedsBounds) UpdateBounds();
            UpdateGround();
            PushDynamicUniforms();
        }

        void ResetSentValues()
        {
            _sentSeeThrough = _sentHoloFade = -1f;
            _sentFadeVec = new Vector4(-1f, -1f, -1f, -1f);
            _sentSpot0 = _sentSpot1 = new Vector4(0f, 0f, 0f, -1f);
        }

        void OnHighlightChanged(bool on)
        {
            if (on)
            {
                _targetStart = Now;
                PlaySfx(settings.highlightOnClip);
                if (Application.isPlaying) onHighlightStart?.Invoke();
            }
            else
            {
                PlaySfx(settings.highlightOffClip);
                if (Application.isPlaying) onHighlightEnd?.Invoke();
            }
        }

        void TriggerHit(Color color, float duration, float intensity, PrismHitMode mode, AudioClip clip)
        {
            _hitStart = Now;
            _hitDuration = Mathf.Max(duration, 0.01f);
            _hitIntensity = intensity;
            _hitColor = color;
            _hitMode = mode;
            PlaySfx(clip);
            if (Application.isPlaying) onHit?.Invoke();
            Tick(0f);
            if (_mat != null)
            {
                _mat.SetColor(ID.HitColor, _hitColor);
                _mat.SetVector(ID.HitParams, new Vector4(_hitStart, _hitDuration, (float)_hitMode, _hitIntensity));
            }
        }

        void UpdateTargetAnim()
        {
            var s = settings;
            if (!s.target) { _targetAlpha = 0f; return; }

            if (!Application.isPlaying)
            {
                _targetGrow = 1f;
                _targetAlpha = _fade;
                return;
            }

            float t = Now - _targetStart;
            if (t < 0f || _targetStart < -999f) { _targetAlpha = 0f; return; }

            float trans = Mathf.Max(s.targetTransitionDuration, 0.0001f);
            float k = Mathf.Clamp01(t / trans);
            float inv = 1f - k;
            _targetGrow = 1f - inv * inv * inv; // ease-out cubic

            float a = k;
            if (s.targetStayDuration > 0f)
            {
                float outT = t - trans - s.targetStayDuration;
                if (outT > 0f) a = 1f - Mathf.Clamp01(outT / trans);
            }
            else a *= _fade;

            _targetAlpha = a;
        }

        void UpdateBounds() => _bounds = ComputeBounds();

        Bounds ComputeBounds()
        {
            bool has = false;
            Bounds b = default(Bounds);
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return has ? b : new Bounds(transform.position, Vector3.zero);
        }

        // Renderer.bounds isn't free on skinned meshes, so only compute it when something uses it.
        bool NeedsBounds => _targetAlpha > 0.001f || boundsRequested
                            || ((settings.outline > 0f || settings.glowDashed) && settings.outlineDashMode == PrismDashMode.Radial);

        // Set by things like PrismDashedLine that read WorldBounds every frame.
        [System.NonSerialized] public bool boundsRequested;

        Vector3 TargetCenter => settings.targetCenter != null ? settings.targetCenter.position : _bounds.center;

        void UpdateGround()
        {
            var s = settings;
            if (!s.target || !s.targetAlignToGround || _targetAlpha <= 0.001f) return;

            Vector3 c = TargetCenter;
            Vector3 origin = new Vector3(c.x, _bounds.max.y + 0.01f, c.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_Hits, _bounds.size.y + 50f, s.groundMask, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_Hits[i];
                if (h.transform == null || h.transform.IsChildOf(transform)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    _groundPoint = h.point;
                    _groundNormal = h.normal;
                    found = true;
                }
            }
            if (!found)
            {
                _groundPoint = new Vector3(c.x, _bounds.min.y, c.z);
                _groundNormal = Vector3.up;
            }
        }

        internal Matrix4x4 GetTargetMatrix(Camera cam)
        {
            var s = settings;
            float size = 1f;
            if (s.targetScaleToBounds)
            {
                var e = _bounds.extents;
                size = s.targetAlignToGround ? Mathf.Max(e.x, e.z) * 2f : Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * 2f;
                if (size < 1e-4f) size = 1f;
            }
            float scale = Mathf.LerpUnclamped(s.targetInitialScale, s.targetEndScale, _targetGrow) * size;
            float angle = Now * s.targetRotationSpeed;

            Vector3 pos;
            Quaternion rot;
            if (s.targetAlignToGround)
            {
                pos = _groundPoint + _groundNormal * s.targetGroundOffset;
                rot = Quaternion.FromToRotation(Vector3.forward, _groundNormal) * Quaternion.Euler(0f, 0f, angle);
            }
            else
            {
                var ct = cam.transform;
                pos = TargetCenter;
                rot = Quaternion.LookRotation(ct.forward, ct.up) * Quaternion.Euler(0f, 0f, angle);
            }
            return Matrix4x4.TRS(pos, rot, new Vector3(scale, scale, scale));
        }

        // materials
        void FindShaders()
        {
            if (_highlightShader == null) _highlightShader = Shader.Find(HighlightShaderName);
            if (_hologramShader == null) _hologramShader = Shader.Find(HologramShaderName);
        }

        void CreateMaterials()
        {
            if (_mat == null && _highlightShader != null)
            {
                _mat = new Material(_highlightShader) { name = "PrismHighlight (Instance)", hideFlags = HideFlags.HideAndDontSave };
                _dirty = true;
            }
            if (_holoMat == null && _hologramShader != null)
            {
                _holoMat = new Material(_hologramShader) { name = "PrismHologram (Instance)", hideFlags = HideFlags.HideAndDontSave };
                _dirty = true;
            }
            if (_mat != null) ResolvePasses(_mat, _holoMat);
        }

        static void ResolvePasses(Material m, Material holo)
        {
            P_Mask = m.FindPass("PrismMask");
            P_Clear = m.FindPass("PrismClear");
            for (int i = 0; i < 4; i++) P_Glow[i] = m.FindPass("PrismGlow" + i);
            P_Outline = m.FindPass("PrismOutline");
            P_Surface = m.FindPass("PrismSurface");
            P_SeeThrough = m.FindPass("PrismSeeThrough");
            P_Target = m.FindPass("PrismTarget");
            if (holo != null) P_HoloForward = holo.FindPass("HoloForward");
        }

        static float ZTest(PrismVisibility v)
        {
            switch (v)
            {
                case PrismVisibility.AlwaysOnTop: return (float)CompareFunction.Always;
                case PrismVisibility.OnlyWhenOccluded: return (float)CompareFunction.Greater;
                default: return (float)CompareFunction.LessEqual;
            }
        }

        void ApplyStaticUniforms()
        {
            _dirty = false;
            var s = settings;
            var m = _mat;

            m.SetFloat(ID.StencilBit, StencilBit);
            m.SetVector(ID.Intensities, new Vector4(s.outline, s.glow, s.overlay, s.innerGlow));
            m.SetFloat(ID.HullCull, s.hullCull == PrismHullCull.Front ? (float)CullMode.Front : (float)CullMode.Off);

            m.SetColor(ID.OutlineColor, s.outlineColor);
            m.SetVector(ID.OutlineParams, new Vector4(s.outlineWidth, (float)s.widthMode, (float)s.normalMode, s.glowWidth));
            m.SetFloat(ID.OutlineZTest, ZTest(s.outlineVisibility));
            float count = s.outlineDashMode == PrismDashMode.Radial ? Mathf.Max(1f, Mathf.Round(s.dashCount)) : s.dashCount;
            m.SetVector(ID.DashParams, new Vector4((float)s.outlineDashMode, count, s.dashFill, s.dashSpeed));
            m.SetVector(ID.DashParams2, new Vector4(s.dashAngle, s.dashSoftness, s.glowDashed ? 1f : 0f, 0f));

            m.SetColor(ID.GlowColor, s.glowColor);
            m.SetFloat(ID.GlowZTest, ZTest(s.glowVisibility));
            m.SetVector(ID.GlowParams, new Vector4(Mathf.Clamp(s.glowPasses, 1, 4), s.glowDithering ? s.glowDitherStrength : 0f, s.glowPulseSpeed, s.glowPulseAmount));

            m.SetColor(ID.InnerGlowColor, s.innerGlowColor);
            m.SetFloat(ID.InnerGlowPower, s.innerGlowPower);

            m.SetColor(ID.OverlayColor, s.overlayColor);
            m.SetTexture(ID.OverlayTex, s.overlayTexture != null ? s.overlayTexture : Texture2D.whiteTexture);
            m.SetVector(ID.OverlayParams, new Vector4(s.overlayBlending, s.overlayMinIntensity, s.overlayAnimationSpeed, (float)s.overlayMapping));
            m.SetVector(ID.OverlayTexParams, new Vector4(s.overlayTextureScale, s.overlayScroll.x, s.overlayScroll.y, 0f));
            m.SetFloat(ID.SurfaceZTest, ZTest(s.surfaceVisibility));

            m.SetColor(ID.SeeThroughColor, s.seeThroughColor);

            m.SetColor(ID.HitColor, _hitColor);
            m.SetVector(ID.HitParams, new Vector4(_hitStart, _hitDuration, (float)_hitMode, _hitIntensity));

            m.SetTexture(ID.TargetTex, s.targetTexture != null ? s.targetTexture : PrismFXResources.DefaultTarget);
            m.SetColor(ID.TargetColor, s.targetColor);
            m.SetFloat(ID.TargetZTest, ZTest(s.targetVisibility));

            m.SetColor(ID.ProxColor, s.touchGlowColor);
            m.SetVector(ID.ProxParams, new Vector4(s.touchGlowRadius, s.touchGlowFalloff, s.touchGlowRing, s.touchGlowDistance));

            ResetSentValues();

            if (_holoMat != null) ApplyHologramUniforms();
        }

        void ApplyHologramUniforms()
        {
            var s = settings;
            var h = _holoMat;
            h.SetColor(ID.HoloColor, s.hologramColor);
            h.SetColor(ID.HoloRimColor, s.hologramRimColor);
            h.SetTexture(ID.MainTex, s.hologramTexture != null ? s.hologramTexture : Texture2D.whiteTexture);
            h.SetFloat(ID.TexInfluence, s.hologramTexture != null ? s.hologramTextureInfluence : 0f);
            h.SetFloat(ID.HoloAlpha, s.hologramAlpha);
            h.SetFloat(ID.FresnelPower, s.hologramFresnelPower);
            h.SetFloat(ID.RimIntensity, s.hologramRimIntensity);
            h.SetFloat(ID.ScanSpace, (float)s.hologramScanSpace);
            h.SetFloat(ID.ScanDensity, s.scanlineDensity);
            h.SetFloat(ID.ScanSpeed, s.scanlineSpeed);
            h.SetFloat(ID.ScanThickness, s.scanlineThickness);
            h.SetFloat(ID.ScanIntensity, s.scanlineIntensity);
            h.SetFloat(ID.FineDensity, s.fineLineDensity);
            h.SetFloat(ID.FineIntensity, s.fineLineIntensity);
            h.SetFloat(ID.BandFrequency, s.bandFrequency);
            h.SetFloat(ID.BandSpeed, s.bandSpeed);
            h.SetFloat(ID.BandWidth, s.bandWidth);
            h.SetFloat(ID.BandIntensity, s.bandIntensity);
            h.SetFloat(ID.FlickerSpeed, s.flickerSpeed);
            h.SetFloat(ID.FlickerAmount, s.flickerAmount);
            h.SetFloat(ID.GlitchAmount, s.glitchAmount);
            h.SetFloat(ID.GlitchFrequency, s.glitchFrequency);
            h.SetFloat(ID.GlitchSpeed, s.glitchSpeed);
            h.SetFloat(ID.GlitchProbability, s.glitchProbability);
            h.SetFloat(ID.Cull, s.hologramDoubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            h.SetFloat(ID.SrcBlend, (float)BlendMode.SrcAlpha);
            h.SetFloat(ID.DstBlend, s.hologramAdditive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            h.SetShaderPassEnabled("SRPDefaultUnlit", s.hologramDepthPrepass);
            h.renderQueue = (int)RenderQueue.Transparent;
        }

        void PushDynamicUniforms()
        {
            var s = settings;
            if (_fadeVec != _sentFadeVec)
            {
                _mat.SetVector(ID.Fade, _fadeVec);
                _sentFadeVec = _fadeVec;
            }
            if (_spot0 != _sentSpot0 || _spot0.w != _sentSpot0.w)
            {
                _mat.SetVector(ID.Spot0, _spot0);
                _sentSpot0 = _spot0;
            }
            if (_spot1 != _sentSpot1 || _spot1.w != _sentSpot1.w)
            {
                _mat.SetVector(ID.Spot1, _spot1);
                _sentSpot1 = _spot1;
            }

            float st = s.seeThroughMode == PrismSeeThroughMode.AlwaysWhenOccluded ? s.seeThroughIntensity
                     : s.seeThroughMode == PrismSeeThroughMode.WhenHighlighted ? s.seeThroughIntensity * _fade
                     : 0f;
            if (st != _sentSeeThrough)
            {
                _mat.SetVector(ID.SeeThroughParams, new Vector4(st, s.seeThroughColorBlend, s.seeThroughNoise, s.seeThroughBorder));
                _sentSeeThrough = st;
            }

            if (NeedsBounds) _mat.SetVector(ID.CenterWS, _bounds.center);

            if (_targetAlpha > 0.001f)
                _mat.SetVector(ID.TargetParams, new Vector4(_targetAlpha, s.targetPulse, 0f, 0f));

            if (_holoMat != null)
            {
                float hf = HoloFade;
                if (hf != _sentHoloFade)
                {
                    _holoMat.SetFloat(ID.HoloFade, hf);
                    _sentHoloFade = hf;
                }
            }
        }

        // hologram replace
        void UpdateHologramReplace()
        {
            var s = settings;
            bool want = Application.isPlaying && _holoMat != null && s.hologram
                        && s.hologramMode == PrismHologramMode.Replace && HoloFade > 0.001f;
            if (want == _replaced) return;

            if (want)
            {
                if (_dirty && _mat != null) ApplyStaticUniforms();
                _originalMaterials = new Material[_renderers.Count][];
                for (int i = 0; i < _renderers.Count; i++)
                {
                    var r = _renderers[i];
                    if (r == null) continue;
                    var orig = r.sharedMaterials;
                    _originalMaterials[i] = orig;
                    var arr = new Material[orig.Length];
                    for (int j = 0; j < arr.Length; j++) arr[j] = _holoMat;
                    r.sharedMaterials = arr;
                }
                _replaced = true;
            }
            else RestoreMaterials();
        }

        void RestoreMaterials()
        {
            if (!_replaced) return;
            if (_originalMaterials != null)
            {
                for (int i = 0; i < _renderers.Count && i < _originalMaterials.Length; i++)
                {
                    var r = _renderers[i];
                    if (r != null && _originalMaterials[i] != null) r.sharedMaterials = _originalMaterials[i];
                }
            }
            _originalMaterials = null;
            _replaced = false;
        }

        // audio
        void PlaySfx(AudioClip clip)
        {
            var s = settings;
            if (!Application.isPlaying || !s.audioEnabled || clip == null) return;
            float t = Time.unscaledTime;
            if (t - _lastSfxTime < s.minSoundInterval) return;
            _lastSfxTime = t;

            if (_sfx == null) _sfx = CreateSource(false);
            else ConfigureSource(_sfx);
            _sfx.pitch = 1f + Random.Range(-s.pitchVariation, s.pitchVariation);
            _sfx.PlayOneShot(clip, s.volume);
        }

        void UpdateLoopAudio()
        {
            var s = settings;
            var clip = s.hologramLoopClip;
            float vol = s.audioEnabled && clip != null ? s.loopVolume * s.volume * HoloFade : 0f;
            if (vol > 0.001f)
            {
                if (_loop == null) _loop = CreateSource(true);
                if (_loop.clip != clip) _loop.clip = clip;
                _loop.volume = vol;
                if (!_loop.isPlaying) _loop.Play();
            }
            else if (_loop != null && _loop.isPlaying) _loop.Stop();
        }

        AudioSource CreateSource(bool loop)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor;
            a.playOnAwake = false;
            a.loop = loop;
            ConfigureSource(a);
            return a;
        }

        void ConfigureSource(AudioSource a)
        {
            var s = settings;
            a.spatialBlend = s.spatialBlend;
            a.outputAudioMixerGroup = s.outputGroup;
            a.dopplerLevel = 0f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = 0.5f;
            a.maxDistance = 25f;
        }

        // rendering (called by the renderer feature)
        void Register()
        {
            if (_registered) return;
            if (!Active.Contains(this)) Active.Add(this);
            _registered = true;
        }

        void Unregister()
        {
            if (!_registered) return;
            Active.Remove(this);
            _registered = false;
        }

        internal void Render<T>(ref T sink, Camera cam) where T : struct, IPrismDrawSink
        {
            if (_mat == null || !isActiveAndEnabled) return;
            var s = settings;

            int camMask = cam.cullingMask;
            int count = _renderers.Count;
            if (_drawable.Length < count) _drawable = new bool[Mathf.NextPowerOfTwo(count)];
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                var r = _renderers[i];
                bool ok = r != null && r.enabled && r.isVisible && r.gameObject.activeInHierarchy
                          && (camMask & (1 << r.gameObject.layer)) != 0;
                _drawable[i] = ok;
                any |= ok;
            }

            bool outline = _fadeVec.x > 0.001f && s.outline > 0f && s.outlineWidth > 0f;
            bool glow = _fadeVec.y > 0.001f && s.glow > 0f && s.glowWidth > 0f;
            bool surface = (_fadeVec.z > 0.001f && s.overlay > 0f) || (_fadeVec.w > 0.001f && s.innerGlow > 0f)
                           || HitActive || SpotActive;
            bool seeThrough = _sentSeeThrough > 0.001f;
            bool holoOverlay = _holoMat != null && !_replaced && HoloFade > 0.001f;
            bool stencil = outline || glow;

            if (any)
            {
                if (seeThrough) DrawPass(ref sink, _mat, P_SeeThrough);
                if (surface) DrawPass(ref sink, _mat, P_Surface);
                if (holoOverlay) DrawPass(ref sink, _holoMat, P_HoloForward);

                if (stencil)
                {
                    DrawPass(ref sink, _mat, P_Mask);
                    if (glow)
                    {
                        int passes = Mathf.Clamp(s.glowPasses, 1, 4);
                        for (int g = 0; g < passes; g++) DrawPass(ref sink, _mat, P_Glow[g]);
                    }
                    if (outline) DrawPass(ref sink, _mat, P_Outline);
                }
                if (stencil || seeThrough) DrawPass(ref sink, _mat, P_Clear);
            }

            if (_targetAlpha > 0.001f && P_Target >= 0)
                sink.DrawMesh(PrismFXResources.Quad, GetTargetMatrix(cam), _mat, P_Target);
        }

        void DrawPass<T>(ref T sink, Material m, int pass) where T : struct, IPrismDrawSink
        {
            if (pass < 0) return;
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (!_drawable[i]) continue;
                var r = _renderers[i];
                int subs = _subMeshCounts[i];
                for (int sub = 0; sub < subs; sub++) sink.DrawRenderer(r, m, sub, pass);
            }
        }

        // helpers
        static void DestroyImmediateSafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

#if UNITY_EDITOR
        static AudioClip FindClip(string name)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets(name + " t:AudioClip");
            if (guids.Length == 0) return null;
            return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif

        static class ID
        {
            public static readonly int Fade = Shader.PropertyToID("_HL_FadeVec");
            public static readonly int Spot0 = Shader.PropertyToID("_HL_Spot0");
            public static readonly int Spot1 = Shader.PropertyToID("_HL_Spot1");
            public static readonly int ProxColor = Shader.PropertyToID("_ProxColor");
            public static readonly int ProxParams = Shader.PropertyToID("_ProxParams");
            public static readonly int CenterWS = Shader.PropertyToID("_HL_CenterWS");
            public static readonly int Intensities = Shader.PropertyToID("_Intensities");
            public static readonly int HullCull = Shader.PropertyToID("_HullCull");
            public static readonly int StencilBit = Shader.PropertyToID("_PrismStencilBit");
            public static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
            public static readonly int OutlineParams = Shader.PropertyToID("_OutlineParams");
            public static readonly int OutlineZTest = Shader.PropertyToID("_OutlineZTest");
            public static readonly int DashParams = Shader.PropertyToID("_DashParams");
            public static readonly int DashParams2 = Shader.PropertyToID("_DashParams2");
            public static readonly int GlowColor = Shader.PropertyToID("_GlowColor");
            public static readonly int GlowZTest = Shader.PropertyToID("_GlowZTest");
            public static readonly int GlowParams = Shader.PropertyToID("_GlowParams");
            public static readonly int InnerGlowColor = Shader.PropertyToID("_InnerGlowColor");
            public static readonly int InnerGlowPower = Shader.PropertyToID("_InnerGlowPower");
            public static readonly int OverlayColor = Shader.PropertyToID("_OverlayColor");
            public static readonly int OverlayTex = Shader.PropertyToID("_OverlayTex");
            public static readonly int OverlayParams = Shader.PropertyToID("_OverlayParams");
            public static readonly int OverlayTexParams = Shader.PropertyToID("_OverlayTexParams");
            public static readonly int SurfaceZTest = Shader.PropertyToID("_SurfaceZTest");
            public static readonly int SeeThroughColor = Shader.PropertyToID("_SeeThroughColor");
            public static readonly int SeeThroughParams = Shader.PropertyToID("_SeeThroughParams");
            public static readonly int HitColor = Shader.PropertyToID("_HitColor");
            public static readonly int HitParams = Shader.PropertyToID("_HitParams");
            public static readonly int TargetTex = Shader.PropertyToID("_TargetTex");
            public static readonly int TargetColor = Shader.PropertyToID("_TargetColor");
            public static readonly int TargetZTest = Shader.PropertyToID("_TargetZTest");
            public static readonly int TargetParams = Shader.PropertyToID("_TargetParams");

            public static readonly int HoloColor = Shader.PropertyToID("_HoloColor");
            public static readonly int HoloRimColor = Shader.PropertyToID("_RimColor");
            public static readonly int MainTex = Shader.PropertyToID("_MainTex");
            public static readonly int TexInfluence = Shader.PropertyToID("_TexInfluence");
            public static readonly int HoloAlpha = Shader.PropertyToID("_Alpha");
            public static readonly int FresnelPower = Shader.PropertyToID("_FresnelPower");
            public static readonly int RimIntensity = Shader.PropertyToID("_RimIntensity");
            public static readonly int ScanSpace = Shader.PropertyToID("_ScanSpace");
            public static readonly int ScanDensity = Shader.PropertyToID("_ScanDensity");
            public static readonly int ScanSpeed = Shader.PropertyToID("_ScanSpeed");
            public static readonly int ScanThickness = Shader.PropertyToID("_ScanThickness");
            public static readonly int ScanIntensity = Shader.PropertyToID("_ScanIntensity");
            public static readonly int FineDensity = Shader.PropertyToID("_FineDensity");
            public static readonly int FineIntensity = Shader.PropertyToID("_FineIntensity");
            public static readonly int BandFrequency = Shader.PropertyToID("_BandFrequency");
            public static readonly int BandSpeed = Shader.PropertyToID("_BandSpeed");
            public static readonly int BandWidth = Shader.PropertyToID("_BandWidth");
            public static readonly int BandIntensity = Shader.PropertyToID("_BandIntensity");
            public static readonly int FlickerSpeed = Shader.PropertyToID("_FlickerSpeed");
            public static readonly int FlickerAmount = Shader.PropertyToID("_FlickerAmount");
            public static readonly int GlitchAmount = Shader.PropertyToID("_GlitchAmount");
            public static readonly int GlitchFrequency = Shader.PropertyToID("_GlitchFrequency");
            public static readonly int GlitchSpeed = Shader.PropertyToID("_GlitchSpeed");
            public static readonly int GlitchProbability = Shader.PropertyToID("_GlitchProbability");
            public static readonly int HoloFade = Shader.PropertyToID("_Fade");
            public static readonly int Cull = Shader.PropertyToID("_Cull");
            public static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
            public static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        }
    }
}
