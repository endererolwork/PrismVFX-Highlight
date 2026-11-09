using UnityEngine;

namespace PrismFX
{
    // Small shared resources generated at runtime so the package ships without extra assets.
    internal static class PrismFXResources
    {
        static Mesh s_Quad;
        static Texture2D s_Target;

        public static Mesh Quad
        {
            get
            {
                if (s_Quad == null) s_Quad = CreateQuad();
                return s_Quad;
            }
        }

        public static Texture2D DefaultTarget
        {
            get
            {
                if (s_Target == null) s_Target = CreateTargetTexture(256);
                return s_Target;
            }
        }

        static Mesh CreateQuad()
        {
            var m = new Mesh { name = "PrismFX Quad", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),   new Vector3(-0.5f, 0.5f, 0f)
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        // Default reticle: 4 thick arcs, a dotted inner ring and 4 ticks
        static Texture2D CreateTargetTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "PrismFX Default Target",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var px = new Color32[size * size];
            float aa = 2.5f / size;
            const float tau = Mathf.PI * 2f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u) / tau + 0.5f; // 0..1

                float outerSeg = Frac(ang * 4f + 0.125f);
                float outer = Band(r, 0.90f, 0.035f, aa) * Segment(outerSeg, 0.72f, 0.02f);

                float innerSeg = Frac(ang * 32f);
                float inner = Band(r, 0.74f, 0.012f, aa) * Segment(innerSeg, 0.55f, 0.06f);

                float tickAng = Frac(ang * 4f);
                float tickDist = Mathf.Min(tickAng, 1f - tickAng) * tau / 4f * r; // distance along the arc
                float tick = Mathf.Clamp01((0.012f - tickDist) / aa + 0.5f) * Band(r, 0.585f, 0.06f, aa);

                float a = Mathf.Max(outer, Mathf.Max(inner * 0.8f, tick));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        static float Band(float r, float center, float halfWidth, float aa)
            => Mathf.Clamp01((halfWidth - Mathf.Abs(r - center)) / aa + 0.5f);

        static float Segment(float t, float fill, float soft)
            => Mathf.Clamp01((fill * 0.5f - Mathf.Abs(t - fill * 0.5f)) / soft + 0.5f);
    }
}
