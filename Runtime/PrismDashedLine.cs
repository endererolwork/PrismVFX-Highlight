using UnityEngine;
using UnityEngine.Rendering;

namespace PrismFX
{
    /// <summary>
    /// Animated dashed line between two points, e.g. from a part to its slot.
    /// Link it to a PrismHighlight and it fades in/out with it.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    [AddComponentMenu("PrismFX/Prism Dashed Line")]
    public class PrismDashedLine : MonoBehaviour
    {
        [Header("Endpoints")]
        [Tooltip("Defaults to this transform.")]
        public Transform from;
        public Vector3 fromOffset;
        [Tooltip("If empty, To Offset is used as a world position.")]
        public Transform to;
        public Vector3 toOffset;
        [Tooltip("Use the renderer bounds center instead of the pivot.")]
        public bool useBoundsCenter = true;

        [Header("Look")]
        [ColorUsage(true, true)] public Color color = new Color(0.25f, 0.9f, 1f, 1f);
        [Tooltip("Meters.")]
        [Min(0.0005f)] public float width = 0.008f;
        [Tooltip("Meters.")]
        [Min(0.001f)] public float dashLength = 0.06f;
        [Tooltip("Meters. 0 = solid line.")]
        [Min(0f)] public float gapLength = 0.04f;
        [Tooltip("Meters per second. Negative flows backwards.")]
        public float flowSpeed = 0.25f;
        [Range(0.001f, 1f)] public float edgeSoftness = 0.35f;
        [Range(0f, 4f)] public float glowStrength = 1f;
        [Tooltip("Fade length at both ends (meters).")]
        [Min(0f)] public float endFade = 0.03f;
        public PrismVisibility visibility = PrismVisibility.Normal;

        [Header("Link")]
        public PrismHighlight linkedHighlight;
        [Tooltip("Only visible while the linked highlight is on.")]
        public bool showOnlyWhenHighlighted = true;

        [SerializeField, HideInInspector] Shader _shader;

        LineRenderer _lr;
        Material _mat;
        readonly Vector3[] _points = new Vector3[2];
        Transform _cachedFrom, _cachedTo;
        Renderer _fromRenderer, _toRenderer;

        static readonly int ID_Color = Shader.PropertyToID("_Color");
        static readonly int ID_Dash = Shader.PropertyToID("_DashLength");
        static readonly int ID_Gap = Shader.PropertyToID("_GapLength");
        static readonly int ID_Speed = Shader.PropertyToID("_Speed");
        static readonly int ID_Length = Shader.PropertyToID("_LineLength");
        static readonly int ID_Soft = Shader.PropertyToID("_EdgeSoftness");
        static readonly int ID_Glow = Shader.PropertyToID("_GlowStrength");
        static readonly int ID_EndFade = Shader.PropertyToID("_EndFade");
        static readonly int ID_Fade = Shader.PropertyToID("_Fade");
        static readonly int ID_ZTest = Shader.PropertyToID("_ZTest");

        public void SetEndpoints(Transform fromTransform, Transform toTransform)
        {
            from = fromTransform;
            to = toTransform;
        }

        void Reset()
        {
            _shader = Shader.Find("PrismFX/DashedLine");
            linkedHighlight = GetComponentInParent<PrismHighlight>();
            if (linkedHighlight != null && from == null) from = linkedHighlight.transform;
        }

        void OnValidate()
        {
            if (_shader == null) _shader = Shader.Find("PrismFX/DashedLine");
            if (isActiveAndEnabled) Setup();
        }

        void OnEnable() => Setup();

        void OnDisable()
        {
            if (_lr != null) _lr.enabled = false;
        }

        void OnDestroy()
        {
            if (_mat == null) return;
            if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat);
        }

        void Setup()
        {
            if (_lr == null) _lr = GetComponent<LineRenderer>();
            if (_shader == null) _shader = Shader.Find("PrismFX/DashedLine");
            if (_mat == null && _shader != null)
                _mat = new Material(_shader) { name = "PrismDashedLine (Instance)", hideFlags = HideFlags.HideAndDontSave };

            _lr.useWorldSpace = true;
            _lr.positionCount = 2;
            _lr.alignment = LineAlignment.View;
            _lr.textureMode = LineTextureMode.Stretch;
            _lr.numCapVertices = 0;
            _lr.numCornerVertices = 0;
            _lr.shadowCastingMode = ShadowCastingMode.Off;
            _lr.receiveShadows = false;
            _lr.lightProbeUsage = LightProbeUsage.Off;
            _lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _lr.colorGradient = WhiteGradient();
            if (_mat != null) _lr.sharedMaterial = _mat;
            ApplyStatic();
        }

        static Gradient WhiteGradient()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        void ApplyStatic()
        {
            if (_mat == null) return;
            _mat.SetColor(ID_Color, color);
            _mat.SetFloat(ID_Dash, dashLength);
            _mat.SetFloat(ID_Gap, gapLength);
            _mat.SetFloat(ID_Speed, flowSpeed);
            _mat.SetFloat(ID_Soft, edgeSoftness);
            _mat.SetFloat(ID_Glow, glowStrength);
            _mat.SetFloat(ID_EndFade, endFade);
            _mat.SetFloat(ID_ZTest, visibility == PrismVisibility.AlwaysOnTop ? (float)CompareFunction.Always
                                  : visibility == PrismVisibility.OnlyWhenOccluded ? (float)CompareFunction.Greater
                                  : (float)CompareFunction.LessEqual);
            if (_lr != null) _lr.widthMultiplier = width;
        }

        void LateUpdate()
        {
            if (_lr == null || _mat == null) return;

            float fade = 1f;
            if (linkedHighlight != null)
            {
                linkedHighlight.boundsRequested = true; // we read its bounds for the endpoint
                if (showOnlyWhenHighlighted) fade = linkedHighlight.CurrentFade;
            }
            bool visible = fade > 0.001f;
            if (_lr.enabled != visible) _lr.enabled = visible;
            if (!visible) return;

            _points[0] = Resolve(from != null ? from : transform, fromOffset, ref _cachedFrom, ref _fromRenderer);
            _points[1] = to != null ? Resolve(to, toOffset, ref _cachedTo, ref _toRenderer) : toOffset;
            _lr.SetPositions(_points);

            _mat.SetFloat(ID_Length, Vector3.Distance(_points[0], _points[1]));
            _mat.SetFloat(ID_Fade, fade);
        }

        Vector3 Resolve(Transform t, Vector3 offset, ref Transform cached, ref Renderer cachedRenderer)
        {
            if (!useBoundsCenter) return t.position + offset;
            if (linkedHighlight != null && t == linkedHighlight.transform && linkedHighlight.RendererCount > 0)
                return linkedHighlight.WorldBounds.center + offset;
            if (cached != t)
            {
                cached = t;
                cachedRenderer = null;
                var all = t.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is MeshRenderer || all[i] is SkinnedMeshRenderer) { cachedRenderer = all[i]; break; }
                }
            }
            return (cachedRenderer != null ? cachedRenderer.bounds.center : t.position) + offset;
        }
    }
}
