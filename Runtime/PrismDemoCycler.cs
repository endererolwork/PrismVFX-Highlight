using UnityEngine;

namespace PrismFX
{
    // Drives the demo scene. Keys (legacy input only): 1-5 toggle, H hit, S select, E error.
    [AddComponentMenu("PrismFX/Demo Cycler")]
    public class PrismDemoCycler : MonoBehaviour
    {
        public PrismHighlight[] targets;
        [Min(0.2f)] public float interval = 2.5f;
        public bool autoCycle = true;
        public bool rotateObjects = true;

        float _next;
        int _step;

        void Update()
        {
            if (targets == null || targets.Length == 0) return;

            if (rotateObjects)
            {
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] != null) targets[i].transform.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < targets.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && targets[i] != null) targets[i].ToggleHighlight();
            if (Input.GetKeyDown(KeyCode.H)) ForEach(t => t.HitFX());
            if (Input.GetKeyDown(KeyCode.S)) ForEach(t => t.Select());
            if (Input.GetKeyDown(KeyCode.E)) ForEach(t => t.Error());
#endif

            if (!autoCycle || Time.time < _next) return;
            _next = Time.time + interval;

            var t0 = targets[_step % targets.Length];
            if (t0 != null)
            {
                switch ((_step / targets.Length) % 3)
                {
                    case 0: t0.ToggleHighlight(); break;
                    case 1: if (t0.highlighted) t0.HitFX(); else t0.ToggleHighlight(); break;
                    case 2: if (t0.highlighted) t0.Select(); else t0.ToggleHighlight(); break;
                }
            }
            _step++;
        }

        void ForEach(System.Action<PrismHighlight> a)
        {
            foreach (var t in targets) if (t != null && t.highlighted) a(t);
        }
    }
}
