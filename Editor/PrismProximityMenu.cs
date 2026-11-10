using UnityEditor;
using UnityEngine;

namespace PrismFX.EditorTools
{
    static class PrismProximityMenu
    {
        const string MenuPath = "GameObject/PrismFX/Add Proximity Source";

        // Select the fingertip bones (e.g. the index tips of your hand rig) and run this.
        [MenuItem(MenuPath, false, 20)]
        static void AddSource()
        {
            int added = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<PrismProximitySource>() != null) continue;
                Undo.AddComponent<PrismProximitySource>(go);
                added++;
            }
            Debug.Log($"[PrismFX] Added Prism Proximity Source to {added} object(s).");
        }

        [MenuItem(MenuPath, true)]
        static bool AddSourceValidate() => Selection.gameObjects.Length > 0;
    }
}
