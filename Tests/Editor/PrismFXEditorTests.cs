using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using PrismFX.EditorTools;

namespace PrismFX.Tests
{
    public class PrismFXEditorTests
    {
        static readonly string[] HighlightPasses =
        {
            "PrismMask", "PrismClear", "PrismGlow0", "PrismGlow1", "PrismGlow2", "PrismGlow3",
            "PrismOutline", "PrismSurface", "PrismSeeThrough", "PrismTarget"
        };

        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        GameObject Cube(string name, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            else _root = go;
            return go;
        }

        // ------------------------------------------------------------------ shaders
        [TestCase("Hidden/PrismFX/Highlight")]
        [TestCase("PrismFX/Hologram")]
        [TestCase("PrismFX/DashedLine")]
        public void Shader_IsFoundAndSupported(string name)
        {
            var sh = Shader.Find(name);
            Assert.IsNotNull(sh, $"shader not found: {name}");
            Assert.IsTrue(sh.isSupported, $"{name} is not supported, check the Console for shader errors");
        }

        [Test]
        public void HighlightShader_HasAllPasses()
        {
            var mat = new Material(Shader.Find("Hidden/PrismFX/Highlight"));
            try
            {
                foreach (var p in HighlightPasses)
                    Assert.GreaterOrEqual(mat.FindPass(p), 0, $"missing pass {p}");
            }
            finally { Object.DestroyImmediate(mat); }
        }

        [Test]
        public void HologramShader_HasPasses()
        {
            var mat = new Material(Shader.Find("PrismFX/Hologram"));
            try
            {
                Assert.GreaterOrEqual(mat.FindPass("HoloForward"), 0);
                Assert.GreaterOrEqual(mat.FindPass("HoloDepth"), 0);
            }
            finally { Object.DestroyImmediate(mat); }
        }

        // ------------------------------------------------------------------ project setup
        [Test]
        public void Project_UsesURP()
        {
            Assert.IsNotEmpty(PrismFXSetup.AllUrpAssets(), "no URP asset assigned");
        }

        [Test]
        public void Project_AllRenderersHaveFeature()
        {
            var missing = PrismFXSetup.RenderersMissingFeature();
            Assert.IsEmpty(missing, "feature missing, run Tools > PrismFX > Setup Project");
        }

        // ------------------------------------------------------------------ settings
        [Test]
        public void Settings_CopyFrom_RoundTrip()
        {
            var a = new PrismHighlightSettings { outlineWidth = 7.5f, glowPasses = 2, outlineDashMode = PrismDashMode.Radial, hologram = true };
            var b = new PrismHighlightSettings();
            b.CopyFrom(a);
            Assert.AreEqual(7.5f, b.outlineWidth);
            Assert.AreEqual(2, b.glowPasses);
            Assert.AreEqual(PrismDashMode.Radial, b.outlineDashMode);
            Assert.IsTrue(b.hologram);
        }

        [Test]
        public void Presets_AllApplyWithoutError()
        {
            foreach (PrismPreset p in System.Enum.GetValues(typeof(PrismPreset)))
            {
                var s = new PrismHighlightSettings();
                Assert.DoesNotThrow(() => s.ApplyPreset(p), p.ToString());
                Assert.That(s.outline + s.glow + s.innerGlow, Is.GreaterThan(0f), $"{p} doesn't enable anything");
            }
        }

        // ------------------------------------------------------------------ component
        [Test]
        public void Component_FindsChildRenderers_AndRespectsNestedHighlights()
        {
            var root = Cube("Root");
            Cube("Child", root.transform);
            var nested = Cube("NestedOwner", root.transform);
            Cube("NestedChild", nested.transform);
            nested.AddComponent<PrismHighlight>();

            var h = root.AddComponent<PrismHighlight>();
            h.RefreshRenderers();
            Assert.AreEqual(2, h.RendererCount, "nested highlight subtree should be skipped");
        }

        [Test]
        public void Component_RegistersOnlyWhileHighlighted()
        {
            var go = Cube("Reg");
            var h = go.AddComponent<PrismHighlight>();
            h.previewInEditor = true;

            h.highlighted = false;
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, h);

            h.highlighted = true;
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, h);
            Assert.AreEqual(1f, h.CurrentFade, 1e-4f);

            h.highlighted = false;
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, h);
        }

        [Test]
        public void Component_DisableUnregisters()
        {
            var go = Cube("Dis");
            var h = go.AddComponent<PrismHighlight>();
            h.highlighted = true;
            h.enabled = false;
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, h);
            h.enabled = true;
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, h);
        }

        [Test]
        public void ProximityPreview_ShowsEffectInEditMode()
        {
            var go = Cube("ProxPreview");
            var h = go.AddComponent<PrismHighlight>();
            h.previewInEditor = true;
            h.settings.proximity = true;
            h.settings.proximityPreview = 0f;
            h.Refresh();
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, h);

            h.settings.proximityPreview = 0.7f;
            h.Refresh();
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, h);
            Assert.AreEqual(0.7f, h.ProximityAmount, 1e-4f);
        }

        [Test]
        public void SeeThroughAlways_RegistersEvenWhenNotHighlighted()
        {
            var go = Cube("See");
            var h = go.AddComponent<PrismHighlight>();
            h.settings.seeThroughMode = PrismSeeThroughMode.AlwaysWhenOccluded;
            h.Refresh();
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, h);
        }

        // ------------------------------------------------------------------ end-to-end pixel test
        // Renders through URP for real and counts outline-colored pixels.
        [UnityTest]
        public IEnumerator Render_OutlineAppearsInPixels()
        {
            if (PrismFXSetup.RenderersMissingFeature().Count > 0)
                Assert.Ignore("feature missing, run Tools > PrismFX > Setup Project first");

            var go = Cube("PixelTestCube");
            go.transform.position = new Vector3(0f, 0f, 1000f); // away from whatever else is in the scene
            go.layer = 0;

            var camGo = new GameObject("PrismTestCamera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -4f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 40f;
            cam.cullingMask = 1;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;

            var h = go.AddComponent<PrismHighlight>();
            var s = h.settings;
            s.outline = 1f; s.outlineColor = Color.red; s.outlineWidth = 6f; s.outlineVisibility = PrismVisibility.AlwaysOnTop;
            s.glow = 0f; s.innerGlow = 0f; s.overlay = 0f; s.target = false; s.seeThroughMode = PrismSeeThroughMode.Never;
            h.previewInEditor = true;
            h.Refresh();

            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            try
            {
                h.highlighted = false;
                RenderTo(cam, rt);
                int before = CountRed(rt);

                h.highlighted = true;
                RenderTo(cam, rt);       // first render updates isVisible
                yield return null;
                RenderTo(cam, rt);
                int after = CountRed(rt);

                Assert.Less(before, 10, "red pixels with highlight off");
                Assert.Greater(after, 200, $"outline not found in the render (red px: {after})");
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void RenderTo(Camera cam, RenderTexture rt)
        {
#if UNITY_6000_0_OR_NEWER
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
            {
                RenderPipeline.SubmitRenderRequest(cam, req);
                return;
            }
#endif
            cam.targetTexture = rt;
            cam.Render();
        }

        static int CountRed(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            int n = 0;
            foreach (var c in tex.GetPixels32())
                if (c.r > 150 && c.g < 60 && c.b < 60) n++;
            Object.DestroyImmediate(tex);
            return n;
        }
    }
}
