using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace PrismFX.Tests
{
    public class PrismFXPlayModeTests
    {
        GameObject _go;
        PrismHighlight _h;

        [SetUp]
        public void SetUp()
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _h = _go.AddComponent<PrismHighlight>();
            _h.settings.fadeInDuration = 0.1f;
            _h.settings.fadeOutDuration = 0.1f;
            _h.settings.audioEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.Destroy(_go);
        }

        [UnityTest]
        public IEnumerator Fade_InAndOut()
        {
            _h.highlighted = true;
            Assert.Less(_h.CurrentFade, 1f, "should still be fading in");
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(1f, _h.CurrentFade, 1e-4f);
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, _h);

            _h.highlighted = false;
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(0f, _h.CurrentFade, 1e-4f);
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, _h, "should drop out of the render list once faded out");
        }

        [UnityTest]
        public IEnumerator HitFX_RegistersTemporarily()
        {
            _h.settings.hitDuration = 0.1f;
            _h.HitFX();
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, _h);
            yield return new WaitForSeconds(0.25f);
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, _h);
        }

        [UnityTest]
        public IEnumerator HologramReplace_SwapsAndRestoresMaterials()
        {
            var r = _go.GetComponent<Renderer>();
            var original = r.sharedMaterial;

            _h.settings.hologram = true;
            _h.settings.hologramMode = PrismHologramMode.Replace;
            _h.settings.hologramActivation = PrismHologramActivation.WithHighlight;
            _h.Refresh();
            _h.highlighted = true;
            yield return new WaitForSeconds(0.05f);
            Assert.AreNotEqual(original, r.sharedMaterial, "Replace mode should swap in the hologram material");
            Assert.AreEqual("PrismFX/Hologram", r.sharedMaterial.shader.name);

            _h.highlighted = false;
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(original, r.sharedMaterial, "Fade bitince orijinal materyal geri gelmeli.");
        }

        [UnityTest]
        public IEnumerator HologramReplace_RestoresOnDisable()
        {
            var r = _go.GetComponent<Renderer>();
            var original = r.sharedMaterial;
            _h.settings.hologram = true;
            _h.settings.hologramMode = PrismHologramMode.Replace;
            _h.settings.hologramActivation = PrismHologramActivation.Always;
            _h.Refresh();
            yield return null;
            Assert.AreNotEqual(original, r.sharedMaterial);
            _h.enabled = false;
            Assert.AreEqual(original, r.sharedMaterial);
        }

        [UnityTest]
        public IEnumerator Events_Fire()
        {
            int start = 0, end = 0, hit = 0;
            _h.onHighlightStart.AddListener(() => start++);
            _h.onHighlightEnd.AddListener(() => end++);
            _h.onHit.AddListener(() => hit++);

            _h.highlighted = true;
            _h.highlighted = true; // same value again, no extra event
            _h.HitFX();
            _h.highlighted = false;
            yield return null;

            Assert.AreEqual(1, start);
            Assert.AreEqual(1, end);
            Assert.AreEqual(1, hit);
        }

        [UnityTest]
        public IEnumerator Audio_CreatesSourceAndPlays()
        {
            var clip = AudioClip.Create("test", 4410, 1, 44100, false);
            _h.settings.audioEnabled = true;
            _h.settings.highlightOnClip = clip;
            _h.settings.minSoundInterval = 0f;
            _h.highlighted = true;
            yield return null;
            Assert.IsNotNull(_go.GetComponent<AudioSource>(), "expected an AudioSource to be created");
            Object.Destroy(clip);
        }

        [UnityTest]
        public IEnumerator Target_FadesInAndStaysWhileHighlighted()
        {
            _h.settings.target = true;
            _h.settings.targetTransitionDuration = 0.1f;
            _h.settings.targetStayDuration = 0f;
            _h.Refresh();
            _h.highlighted = true;
            yield return new WaitForSeconds(0.3f);
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, _h);
            Assert.Greater(_h.WorldBounds.size.magnitude, 0.5f, "bounds should be computed while the target is visible");
        }

        PrismProximitySource MakeTip(Vector3 pos)
        {
            var tip = new GameObject("Tip").AddComponent<PrismProximitySource>();
            tip.radius = 0f;
            tip.transform.position = pos;
            return tip;
        }

        void EnableProximity()
        {
            var s = _h.settings;
            s.proximity = true;
            s.proximityStartDistance = 0.5f;
            s.proximityFullDistance = 0.05f;
            s.proximitySmoothing = 0f;
            s.proximityResponse = 1f;
            _h.Refresh();
        }

        [UnityTest]
        public IEnumerator Proximity_FadesInNearHand_AndOutWhenFar()
        {
            EnableProximity();
            var tip = MakeTip(new Vector3(0f, 0f, -3f)); // cube surface is at z = -0.5
            yield return null; yield return null;
            Assert.AreEqual(0f, _h.ProximityAmount, 1e-4f, "far away, should be 0");
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, _h, "far away objects shouldn't render");

            tip.transform.position = new Vector3(0f, 0f, -0.52f); // 2 cm from the surface
            yield return null; yield return null;
            Assert.Greater(_h.ProximityAmount, 0.95f);
            CollectionAssert.Contains(PrismHighlight.ActiveHighlights, _h);
            Assert.IsTrue(_h.SpotActive, "touch glow should be on this close");

            tip.transform.position = new Vector3(0f, 0f, -0.75f); // 25 cm, halfway-ish
            yield return null; yield return null;
            Assert.That(_h.ProximityAmount, NUnit.Framework.Is.InRange(0.4f, 0.7f));

            tip.transform.position = new Vector3(0f, 0f, -3f);
            yield return null; yield return null;
            Assert.AreEqual(0f, _h.ProximityAmount, 1e-4f);
            CollectionAssert.DoesNotContain(PrismHighlight.ActiveHighlights, _h);
            Object.Destroy(tip.gameObject);
        }

        [UnityTest]
        public IEnumerator Proximity_EventsFireOncePerApproach()
        {
            EnableProximity();
            int enter = 0, exit = 0;
            _h.onProximityEnter.AddListener(() => enter++);
            _h.onProximityExit.AddListener(() => exit++);

            var tip = MakeTip(new Vector3(0f, 0f, -3f));
            yield return null;
            tip.transform.position = new Vector3(0f, 0f, -0.52f);
            for (int i = 0; i < 5; i++) yield return null;   // staying near: still one event
            tip.transform.position = new Vector3(0f, 0f, -3f);
            yield return null; yield return null;

            Assert.AreEqual(1, enter);
            Assert.AreEqual(1, exit);
            Object.Destroy(tip.gameObject);
        }

        [UnityTest]
        public IEnumerator Proximity_OnlyAffectsSelectedEffects()
        {
            var cam = MakeCamera();
            _h.settings.ApplyPreset(PrismPreset.SubtleHover);  // outline + glow + inner glow
            _h.settings.proximityInnerGlow = false;
            _h.settings.touchGlow = false;
            EnableProximity();
            var tip = MakeTip(new Vector3(0f, 0f, -0.52f));
            yield return new WaitForSeconds(0.1f);

            var sink = new CountingSink();
            PrismHighlightFeature.DrawAll(ref sink, cam);
            // mask + 2 glow + outline + clear, no surface pass (inner glow and touch glow are off)
            Assert.AreEqual(5, sink.renderers);

            Object.Destroy(tip.gameObject);
            Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator Proximity_DisabledSourceIsIgnored()
        {
            EnableProximity();
            var tip = MakeTip(new Vector3(0f, 0f, -0.52f));
            tip.enabled = false;
            yield return null; yield return null;
            Assert.AreEqual(0f, _h.ProximityAmount, 1e-4f);
            Object.Destroy(tip.gameObject);
        }

        struct CountingSink : IPrismDrawSink
        {
            public int renderers, meshes;
            public void DrawRenderer(Renderer r, Material m, int sub, int pass) => renderers++;
            public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material m, int pass) => meshes++;
        }

        static readonly System.Reflection.MethodInfo s_Update = typeof(PrismHighlight).GetMethod("Update",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Camera MakeCamera()
        {
            var cam = new GameObject("PrismTestCam").AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -5f);
            cam.transform.LookAt(Vector3.zero);
            return cam;
        }

        [UnityTest]
        public IEnumerator Render_IssuesExpectedDrawCalls()
        {
            var cam = MakeCamera();
            _h.settings.ApplyPreset(PrismPreset.SubtleHover); // outline + glow(2) + inner glow
            _h.settings.glowPasses = 2;
            _h.Refresh();
            _h.highlighted = true;
            yield return new WaitForSeconds(0.3f); // let the fade finish and isVisible update

            var sink = new CountingSink();
            PrismHighlightFeature.DrawAll(ref sink, cam);
            // surface + mask + 2 glow + outline + clear
            Assert.AreEqual(6, sink.renderers, "unexpected draw count");
            Assert.AreEqual(0, sink.meshes);

            _h.settings.target = true;
            _h.PlayTarget();
            yield return new WaitForSeconds(0.1f);
            sink = new CountingSink();
            PrismHighlightFeature.DrawAll(ref sink, cam);
            Assert.AreEqual(1, sink.meshes, "target quad not drawn");

            Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator SteadyState_UpdateAndRender_DoNotAllocate()
        {
            var cam = MakeCamera();
            var list = new PrismHighlight[50];
            var updates = new System.Action[50];
            for (int i = 0; i < list.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = new Vector3((i % 10) - 5f, (i / 10) - 2f, 3f);
                list[i] = go.AddComponent<PrismHighlight>();
                list[i].settings.audioEnabled = false;
                list[i].settings.outlineDashMode = PrismDashMode.Radial; // exercise the bounds path too
                list[i].settings.proximity = true;
                list[i].highlighted = true;
                updates[i] = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), list[i], s_Update);
            }
            var tip = new GameObject("Tip").AddComponent<PrismProximitySource>();
            tip.transform.position = list[0].transform.position + new Vector3(0f, 0f, -0.55f);
            yield return new WaitForSeconds(0.4f);

            TestDelegate frame = () =>
            {
                for (int i = 0; i < updates.Length; i++) updates[i]();
                var sink = new CountingSink();
                PrismHighlightFeature.DrawAll(ref sink, cam);
            };
            frame(); // warm up first, the first call may JIT/lazy-init

            Assert.That(frame, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "update/render path allocated");

            foreach (var h in list) Object.Destroy(h.gameObject);
            Object.Destroy(tip.gameObject);
            Object.Destroy(cam.gameObject);
        }
    }
}
