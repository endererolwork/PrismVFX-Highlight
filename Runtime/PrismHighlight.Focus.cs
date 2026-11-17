using UnityEngine;
using UnityEngine.Rendering;

namespace PrismFX
{
    // Focus: everything except the focused objects gets dimmed / desaturated / blurred.
    //
    // The renderer feature copies the camera color once, masks the focused objects into the
    // stencil, draws one fullscreen triangle that reads the copy, then clears the stencil.
    // Only one focus look is applied per frame (the strongest), but every highlight with
    // Focus on is kept out of it.
    public partial class PrismHighlight
    {
        float _distFactor = 1f;

        /// <summary>0..1 focus strength this object currently asks for.</summary>
        public float FocusAmount => isActiveAndEnabled && settings.focus > 0f ? settings.focus * _fade * _distFactor : 0f;

        /// <summary>The highlight with the strongest focus right now, or null.</summary>
        public static PrismHighlight FindFocus(out float amount)
        {
            PrismHighlight best = null;
            amount = 0f;
            for (int i = 0; i < Active.Count; i++)
            {
                var h = Active[i];
                if (h == null) continue;
                float a = h.FocusAmount;
                if (a > amount)
                {
                    amount = a;
                    best = h;
                }
            }
            return amount > 0.001f ? best : null;
        }

        internal void DrawFocusMask<T>(ref T sink, Camera cam) where T : struct, IPrismDrawSink
        {
            if (_mat == null || FocusAmount <= 0.001f || !PrepareDrawable(cam)) return;
            DrawPass(ref sink, _mat, P_FocusMask);
        }

        internal void DrawFocusClear<T>(ref T sink, Camera cam) where T : struct, IPrismDrawSink
        {
            if (_mat == null || FocusAmount <= 0.001f || !PrepareDrawable(cam)) return;
            DrawPass(ref sink, _mat, P_Clear);
        }

        void UpdateDistanceFactor()
        {
            _distFactor = 1f;
            var s = settings;
            if (!s.distanceCulling || !Application.isPlaying) return;

            var cam = Camera.main;
            if (cam == null) return;

            float d = Vector3.Distance(cam.transform.position, transform.position);
            float fadeStart = s.maxDistance - s.distanceFadeRange;
            if (d > fadeStart)
                _distFactor = Mathf.Clamp01((s.maxDistance - d) / Mathf.Max(s.distanceFadeRange, 1e-4f));
        }

        bool CulledByDistance(Camera cam)
        {
            var s = settings;
            if (!s.distanceCulling || !Application.isPlaying) return false;
            float maxSqr = s.maxDistance * s.maxDistance;
            return (cam.transform.position - transform.position).sqrMagnitude > maxSqr;
        }
    }
}
