using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PrismFX.EditorTools
{
    public static class PrismFXDemoScene
    {
        const string ScenePath = "Assets/PrismFX/Demo/PrismFX_Demo.unity";

        [MenuItem("Tools/PrismFX/Create Demo Scene", priority = 20)]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (PrismFXSetup.RenderersMissingFeature().Count > 0) PrismFXSetup.SetupProject();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 2.6f, -7.5f);
                cam.transform.rotation = Quaternion.Euler(16f, 0f, 0f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
                cam.allowHDR = true;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            var shapes = new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Capsule, PrimitiveType.Cylinder, PrimitiveType.Cube };
            var presets = new[] { PrismPreset.SciFiSelect, PrismPreset.Danger, PrismPreset.GhostHologram, PrismPreset.SubtleHover, PrismPreset.Objective };
            var targets = new PrismHighlight[shapes.Length];

            for (int i = 0; i < shapes.Length; i++)
            {
                var go = GameObject.CreatePrimitive(shapes[i]);
                go.name = $"Demo_{presets[i]}";
                go.transform.position = new Vector3((i - 2) * 2.2f, 0.75f, 0f);
                go.transform.localScale = Vector3.one * 1.1f;

                var h = go.AddComponent<PrismHighlight>();
                h.settings.ApplyPreset(presets[i]);
                h.highlighted = true;
                targets[i] = h;
            }

            // wall in front of the last object so see-through has something to do
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Occluder_Wall";
            wall.transform.position = new Vector3(4.4f, 1f, -1.3f);
            wall.transform.localScale = new Vector3(1.6f, 2f, 0.15f);

            // connector line between the first two
            var lineGo = new GameObject("DashedLine_SciFi_to_Danger");
            var line = lineGo.AddComponent<PrismDashedLine>();
            line.from = targets[0].transform;
            line.to = targets[1].transform;
            line.linkedHighlight = targets[0];
            line.width = 0.025f;
            line.dashLength = 0.15f;
            line.gapLength = 0.1f;

            var demo = new GameObject("PrismFX_Demo").AddComponent<PrismDemoCycler>();
            demo.targets = targets;

            string path = ScenePath;
            if (AssetDatabase.IsValidFolder("Assets/PrismFX"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/PrismFX/Demo")) AssetDatabase.CreateFolder("Assets/PrismFX", "Demo");
            }
            else path = "Assets/PrismFX_Demo.unity";
            EditorSceneManager.SaveScene(scene, path);
            Selection.activeGameObject = targets[0].gameObject;
            Debug.Log("[PrismFX] Demo scene saved to " + path + ". Hit Play.");
        }
    }
}
