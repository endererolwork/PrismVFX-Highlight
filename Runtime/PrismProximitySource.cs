using System.Collections.Generic;
using UnityEngine;

namespace PrismFX
{
    /// <summary>
    /// Marks something that PrismHighlight proximity reacts to: a fingertip bone, a controller tip,
    /// a tool. Put one on each point you care about (index tips are usually enough).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PrismFX/Prism Proximity Source")]
    public class PrismProximitySource : MonoBehaviour
    {
        internal static readonly List<PrismProximitySource> Active = new List<PrismProximitySource>(8);
        public static IReadOnlyList<PrismProximitySource> ActiveSources => Active;

        [Tooltip("Size of the tip in meters. It's subtracted from the distance, so 'touching' happens at the edge of this sphere.")]
        [Min(0f)] public float radius = 0.01f;

        [Tooltip("Leave a touch glow spot on surfaces. Turn this off for sources like a palm or the head.")]
        public bool touchGlow = true;

        [Tooltip("Local offset from this transform, e.g. to move the point to the very tip of a controller.")]
        public Vector3 localOffset;

        public Vector3 Position => localOffset == Vector3.zero ? transform.position : transform.TransformPoint(localOffset);

        void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        void OnDisable() => Active.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(Position, Mathf.Max(radius, 0.002f));
        }
    }
}
