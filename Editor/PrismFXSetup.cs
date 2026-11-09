using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PrismFX.EditorTools
{
    // Tools > PrismFX > Setup Project adds the renderer feature to every URP renderer it can find
    // and puts the shaders in Always Included so builds don't strip them.
    [InitializeOnLoad]
    public static class PrismFXSetup
    {
        const string HighlightShader = "Hidden/PrismFX/Highlight";
        const string HologramShader = "PrismFX/Hologram";
        const string DashedShader = "PrismFX/DashedLine";
        static string AskedKey => "PrismFX.SetupAsked." + Application.dataPath.GetHashCode();

        static PrismFXSetup()
        {
            // ask once after the first import
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(AskedKey, false) || Application.isBatchMode) return;
                EditorPrefs.SetBool(AskedKey, true);
                var missing = RenderersMissingFeature();
                if (missing.Count == 0) return;
                if (EditorUtility.DisplayDialog("PrismFX Setup",
                        $"{missing.Count} URP renderer(s) don't have the Prism Highlight Feature yet.\nAdd it now?",
                        "Add it", "Later"))
                    SetupProject();
            };
        }

        [MenuItem("Tools/PrismFX/Setup Project", priority = 0)]
        public static void SetupProject()
        {
            int added = 0;
            foreach (var data in RenderersMissingFeature())
                if (AddFeature(data)) added++;

            int shaders = AddAlwaysIncludedShaders();
            AssetDatabase.SaveAssets();

            string report = Validate(out _);
            EditorUtility.DisplayDialog("PrismFX Setup",
                $"Renderer features added: {added}\nShaders added to Always Included: {shaders}\n\n{report}", "OK");
        }

        [MenuItem("Tools/PrismFX/Validate Project", priority = 1)]
        public static void ValidateMenu()
        {
            string report = Validate(out bool ok);
            EditorUtility.DisplayDialog(ok ? "PrismFX: all good" : "PrismFX: something's off", report, "OK");
        }

        // validation
        public static string Validate(out bool ok)
        {
            ok = true;
            var sb = new StringBuilder();

            var assets = AllUrpAssets();
            if (assets.Count == 0)
            {
                ok = false;
                sb.AppendLine("✗ No URP pipeline asset found (Graphics / Quality settings).");
            }
            else sb.AppendLine($"✓ {assets.Count} URP pipeline asset(s)");

            var all = AllRendererData();
            var missing = RenderersMissingFeature();
            if (missing.Count > 0)
            {
                ok = false;
                foreach (var m in missing) sb.AppendLine($"✗ Feature missing on: {m.name}");
            }
            else if (all.Count > 0) sb.AppendLine($"✓ Feature present on {all.Count} renderer(s)");

            bool shadersOk = true;
            foreach (var name in new[] { HighlightShader, HologramShader, DashedShader })
            {
                var sh = Shader.Find(name);
                if (sh == null) { shadersOk = false; sb.AppendLine($"✗ Shader not found: {name}"); }
                else if (!sh.isSupported) { shadersOk = false; sb.AppendLine($"✗ Shader failed to compile / unsupported: {name} (check the Console)"); }
            }
            if (shadersOk) sb.AppendLine("✓ Shaders compiled");
            ok &= shadersOk;

#if UNITY_6000_0_OR_NEWER
            if (IsCompatibilityModeOn())
                sb.AppendLine("• Compatibility Mode is on. Works, but Render Graph is the way to go on Unity 6.");
            else
                sb.AppendLine("✓ Render Graph enabled");
#endif
            return sb.ToString();
        }

#if UNITY_6000_0_OR_NEWER
        static bool IsCompatibilityModeOn()
        {
            // reflection because this API moved around between 6.0 and 6.3
            var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.RenderGraphSettings");
            if (type == null) return false;
            var getter = typeof(GraphicsSettings).GetMethod("GetRenderPipelineSettings", System.Type.EmptyTypes);
            if (getter == null) return false;
            object settings;
            try { settings = getter.MakeGenericMethod(type).Invoke(null, null); }
            catch { return false; }
            var prop = type.GetProperty("enableRenderCompatibilityMode");
            return settings != null && prop != null && (bool)prop.GetValue(settings);
        }
#endif

        // renderer feature
        public static List<UniversalRenderPipelineAsset> AllUrpAssets()
        {
            var list = new List<UniversalRenderPipelineAsset>();
            AddUnique(list, GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++) AddUnique(list, QualitySettings.GetRenderPipelineAssetAt(i));
            return list;
        }

        static void AddUnique(List<UniversalRenderPipelineAsset> list, RenderPipelineAsset a)
        {
            var u = a as UniversalRenderPipelineAsset;
            if (u != null && !list.Contains(u)) list.Add(u);
        }

        public static List<ScriptableRendererData> AllRendererData()
        {
            var result = new List<ScriptableRendererData>();
            foreach (var asset in AllUrpAssets())
            {
                var so = new SerializedObject(asset);
                var arr = so.FindProperty("m_RendererDataList");
                if (arr == null) continue;
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var d = arr.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                    if (d != null && !result.Contains(d)) result.Add(d);
                }
            }
            return result;
        }

        public static List<ScriptableRendererData> RenderersMissingFeature()
        {
            var missing = new List<ScriptableRendererData>();
            foreach (var d in AllRendererData())
            {
                bool has = false;
                foreach (var f in d.rendererFeatures)
                    if (f is PrismHighlightFeature) { has = true; break; }
                if (!has) missing.Add(d);
            }
            return missing;
        }

        // Same steps as URP's own "Add Renderer Feature" button: sub-asset + feature map entry.
        static bool AddFeature(ScriptableRendererData data)
        {
            if (data == null) return false;
            var feature = ScriptableObject.CreateInstance<PrismHighlightFeature>();
            feature.name = "PrismHighlightFeature";
            feature.hideFlags |= HideFlags.HideInHierarchy;
            Undo.RegisterCreatedObjectUndo(feature, "Add Prism Highlight Feature");

            if (EditorUtility.IsPersistent(data)) AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            if (features == null || map == null) return false;

            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            if (EditorUtility.IsPersistent(data)) AssetDatabase.SaveAssetIfDirty(data);
            data.SetDirty();
            Debug.Log($"[PrismFX] Added Prism Highlight Feature to {data.name}", data);
            return true;
        }

        // always included shaders
        static int AddAlwaysIncludedShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) return 0;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return 0;

            int added = 0;
            foreach (var name in new[] { HighlightShader, HologramShader, DashedShader })
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                bool exists = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { exists = true; break; }
                if (exists) continue;
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                added++;
            }
            if (added > 0) so.ApplyModifiedProperties();
            return added;
        }
    }
}
