using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PrismFX
{
    // Proximity: the highlight fades in as a PrismProximitySource (fingertip, controller) gets close,
    // and a touch glow spot follows the fingertip on the surface.
    //
    // Cost notes:
    // - per object per frame it's one AABB distance per source (Bounds.SqrDistance), no physics
    //   unless Colliders mode is on, and even then only once the AABB says we're close.
    // - bounds of far-away objects are refreshed every few frames, near ones every frame
    //   (so a part that's being carried still tracks correctly).
    // - the touch spot is computed per pixel in the existing surface pass, no extra draw.
    public partial class PrismHighlight
    {
        [Tooltip("Fired once when a hand gets near (see Event Threshold). A good place for a haptic pulse.")]
        public UnityEvent onProximityEnter = new UnityEvent();
        [Tooltip("Fired once when the hand moves away again.")]
        public UnityEvent onProximityExit = new UnityEvent();

        const int FarBoundsRefreshFrames = 15;

        float _prox;                     // smoothed 0..1
        bool _proxNear;
        float _proxNearestDist = float.MaxValue;
        Bounds _proxBounds;
        int _proxBoundsFrame = -1000;
        Vector4 _spot0, _spot1;          // xyz = world position, w = 0..1 strength
        readonly List<Collider> _colliders = new List<Collider>(4);
        static readonly List<Collider> s_TmpColliders = new List<Collider>(8);

        /// <summary>Current smoothed proximity, 0 = nothing near, 1 = touching.</summary>
        public float ProximityAmount => _prox;

        internal bool SpotActive => _spot0.w > 0.001f || _spot1.w > 0.001f;

        void RefreshColliders()
        {
            _colliders.Clear();
            s_TmpColliders.Clear();
            if (settings.includeChildren) GetComponentsInChildren(true, s_TmpColliders);
            else
            {
                var c = GetComponent<Collider>();
                if (c != null) s_TmpColliders.Add(c);
            }

            for (int i = 0; i < s_TmpColliders.Count; i++)
            {
                var c = s_TmpColliders[i];
                // ClosestPoint isn't supported on concave mesh colliders
                if (c is MeshCollider mc && !mc.convex) continue;
                if (c.GetComponentInParent<PrismHighlight>(true) != this) continue;
                _colliders.Add(c);
            }
            s_TmpColliders.Clear();
        }

        void ResetProximity()
        {
            _prox = 0f;
            _proxNear = false;
            _proxNearestDist = float.MaxValue;
            _spot0.w = _spot1.w = 0f;
        }

        void UpdateProximity(float dt)
        {
            var s = settings;
            if (!s.proximity)
            {
                if (_prox != 0f || SpotActive) ResetProximity();
                return;
            }

            if (!Application.isPlaying)
            {
                _prox = previewInEditor ? s.proximityPreview : 0f;
                _spot0.w = _spot1.w = 0f;
                if (_prox > 0f && s.touchGlow) PreviewSpot();
                return;
            }

            var sources = PrismProximitySource.Active;
            bool anySource = sources.Count > 0 || s.proximityIncludeCamera;

            float nearest = float.MaxValue;
            _spot0.w = _spot1.w = 0f;

            if (anySource)
            {
                bool near = _proxNearestDist < s.proximityStartDistance * 2f + 0.1f;
                int frame = Time.frameCount;
                if (near || frame - _proxBoundsFrame >= FarBoundsRefreshFrames)
                {
                    _proxBounds = ComputeBounds();
                    _proxBoundsFrame = frame;
                }

                float spotD0 = float.MaxValue, spotD1 = float.MaxValue;
                for (int i = 0; i < sources.Count; i++)
                {
                    var src = sources[i];
                    if (src == null) continue;
                    Vector3 p = src.Position;
                    float d = DistanceTo(p) - src.radius;
                    if (d < nearest) nearest = d;

                    if (s.touchGlow && src.touchGlow && d < s.touchGlowDistance)
                    {
                        // keep the two closest fingertips
                        if (d < spotD0)
                        {
                            _spot1 = _spot0; spotD1 = spotD0;
                            _spot0 = new Vector4(p.x, p.y, p.z, SpotStrength(d)); spotD0 = d;
                        }
                        else if (d < spotD1)
                        {
                            _spot1 = new Vector4(p.x, p.y, p.z, SpotStrength(d)); spotD1 = d;
                        }
                    }
                }

                if (s.proximityIncludeCamera)
                {
                    var cam = Camera.main;
                    if (cam != null) nearest = Mathf.Min(nearest, DistanceTo(cam.transform.position));
                }
            }
            _proxNearestDist = nearest;

            float target = 0f;
            float range = Mathf.Max(s.proximityStartDistance - s.proximityFullDistance, 1e-4f);
            float t = Mathf.Clamp01((s.proximityStartDistance - nearest) / range);
            if (t > 0f) target = Mathf.Pow(t, s.proximityResponse);

            if (s.proximitySmoothing <= 0f) _prox = target;
            else _prox = Mathf.Lerp(_prox, target, 1f - Mathf.Exp(-dt / s.proximitySmoothing));
            if (target == 0f && _prox < 0.0005f) _prox = 0f;

            // hysteresis so we don't fire enter/exit every frame at the threshold
            float on = s.proximityEventThreshold;
            if (!_proxNear && _prox >= on)
            {
                _proxNear = true;
                PlaySfx(s.proximityEnterClip);
                onProximityEnter?.Invoke();
            }
            else if (_proxNear && _prox < on * 0.6f)
            {
                _proxNear = false;
                onProximityExit?.Invoke();
            }
        }

        float SpotStrength(float d) => Mathf.Clamp01(1f - d / settings.touchGlowDistance);

        float DistanceTo(Vector3 p)
        {
            float boxDist = Mathf.Sqrt(_proxBounds.SqrDistance(p));
            if (settings.proximityDistanceMode != PrismProximityDistance.Colliders || _colliders.Count == 0)
                return boxDist;

            // the box is a lower bound, so only ask the colliders when it could matter
            if (boxDist > settings.proximityStartDistance) return boxDist;

            float best = float.MaxValue;
            for (int i = 0; i < _colliders.Count; i++)
            {
                var c = _colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                float d = (c.ClosestPoint(p) - p).magnitude;
                if (d < best) best = d;
            }
            return best < float.MaxValue ? best : boxDist;
        }

        // Edit mode: put a fake fingertip on the side of the object facing the scene camera.
        void PreviewSpot()
        {
            _proxBounds = ComputeBounds();
            Vector3 eye = _proxBounds.center + Vector3.up * (_proxBounds.extents.magnitude + 1f);
#if UNITY_EDITOR
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) eye = sv.camera.transform.position;
#endif
            Vector3 p = _proxBounds.ClosestPoint(eye);
            Vector3 dir = (eye - p).normalized;
            float d = (1f - _prox) * settings.touchGlowDistance;
            p += dir * d;
            _spot0 = new Vector4(p.x, p.y, p.z, SpotStrength(d));
        }

        Vector4 ProximityFade()
        {
            var s = settings;
            if (!s.proximity || _prox <= 0f) return new Vector4(_fade, _fade, _fade, _fade);
            float px = _prox * s.proximityStrength;
            return new Vector4(
                s.proximityOutline ? Mathf.Max(_fade, px) : _fade,
                s.proximityGlow ? Mathf.Max(_fade, px) : _fade,
                s.proximityOverlay ? Mathf.Max(_fade, px) : _fade,
                s.proximityInnerGlow ? Mathf.Max(_fade, px) : _fade);
        }

        void OnDrawGizmosSelected()
        {
            if (!settings.proximity) return;
            var b = Application.isPlaying ? _proxBounds : ComputeBounds();
            if (b.size == Vector3.zero) return;
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (settings.proximityStartDistance * 2f));
            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (settings.proximityFullDistance * 2f));
        }
    }
}
