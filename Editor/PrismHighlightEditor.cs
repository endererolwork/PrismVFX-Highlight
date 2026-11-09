using UnityEditor;
using UnityEngine;

namespace PrismFX.EditorTools
{
    [CustomEditor(typeof(PrismHighlight)), CanEditMultipleObjects]
    public class PrismHighlightEditor : Editor
    {
        SerializedProperty _settings, _profile, _syncWithProfile, _highlighted, _trigger, _selectOnClick, _preview;
        SerializedProperty _onStart, _onEnd, _onHit;
        PrismPreset _preset = PrismPreset.SciFiSelect;

        static GUIStyle s_Title, s_Sub, s_Fold;

        static readonly Color C_General = new Color(0.55f, 0.55f, 0.6f);
        static readonly Color C_Outline = new Color(0.25f, 0.85f, 1f);
        static readonly Color C_Glow = new Color(0.35f, 0.6f, 1f);
        static readonly Color C_Inner = new Color(0.6f, 0.95f, 1f);
        static readonly Color C_Overlay = new Color(1f, 0.85f, 0.2f);
        static readonly Color C_Holo = new Color(0.3f, 1f, 0.85f);
        static readonly Color C_Target = new Color(0.4f, 1f, 0.5f);
        static readonly Color C_See = new Color(1f, 0.35f, 0.3f);
        static readonly Color C_Hit = new Color(1f, 1f, 1f);
        static readonly Color C_Audio = new Color(0.85f, 0.5f, 1f);
        static readonly Color C_Events = new Color(0.6f, 0.6f, 0.6f);

        void OnEnable()
        {
            _settings = serializedObject.FindProperty("settings");
            _profile = serializedObject.FindProperty("profile");
            _syncWithProfile = serializedObject.FindProperty("syncWithProfile");
            _highlighted = serializedObject.FindProperty("_highlighted");
            _trigger = serializedObject.FindProperty("trigger");
            _selectOnClick = serializedObject.FindProperty("selectOnClick");
            _preview = serializedObject.FindProperty("previewInEditor");
            _onStart = serializedObject.FindProperty("onHighlightStart");
            _onEnd = serializedObject.FindProperty("onHighlightEnd");
            _onHit = serializedObject.FindProperty("onHit");
        }

        SerializedProperty P(string name) => _settings.FindPropertyRelative(name);

        void Fields(params string[] names)
        {
            foreach (var n in names)
            {
                var p = P(n);
                if (p != null) EditorGUILayout.PropertyField(p, true);
            }
        }

        static void InitStyles()
        {
            if (s_Title != null) return;
            s_Title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            s_Sub = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            s_Fold = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
        }

        // header row: color strip, foldout, and the effect's main toggle/slider on the right
        bool Section(string key, string title, SerializedProperty enabler, Color accent)
        {
            string prefKey = "PrismFX.Section." + key;
            bool open = SessionState.GetBool(prefKey, key == "outline" || key == "glow");

            EditorGUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.07f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);

            var foldRect = new Rect(rect.x + 16f, rect.y + 3f, 150f, 16f);
            bool newOpen = EditorGUI.Foldout(foldRect, open, title, true, s_Fold);

            if (enabler != null)
            {
                var r = new Rect(rect.x + 170f, rect.y + 3f, rect.width - 176f, 16f);
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUI.PropertyField(r, enabler, GUIContent.none);
                EditorGUI.indentLevel = indent;
            }

            if (newOpen != open) SessionState.SetBool(prefKey, newOpen);
            return newOpen;
        }

        public override void OnInspectorGUI()
        {
            InitStyles();
            serializedObject.Update();
            var main = (PrismHighlight)target;

            // --- banner
            var banner = GUILayoutUtility.GetRect(1f, 34f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(banner, new Color(0.08f, 0.12f, 0.16f));
            EditorGUI.DrawRect(new Rect(banner.x, banner.yMax - 2f, banner.width, 2f), C_Outline);
            var oldColor = GUI.color;
            GUI.color = new Color(0.75f, 0.95f, 1f);
            GUI.Label(new Rect(banner.x + 10f, banner.y + 2f, banner.width, 20f), "PRISM HIGHLIGHT", s_Title);
            GUI.color = new Color(0.6f, 0.75f, 0.85f);
            GUI.Label(new Rect(banner.x + 10f, banner.y + 18f, banner.width, 14f),
                $"Outline · Glow · Hologram · Target · See-Through · Hit · SFX     |  {main.RendererCount} renderer", EditorStyles.miniLabel);
            GUI.color = oldColor;
            EditorGUILayout.Space(4);

            if (!PrismHighlightFeature.IsPresent && PrismFXSetup.RenderersMissingFeature().Count > 0)
            {
                EditorGUILayout.HelpBox("Prism Highlight Feature is missing from your URP renderer, nothing will be drawn.", MessageType.Warning);
                if (GUILayout.Button("Fix it (Tools > PrismFX > Setup Project)")) PrismFXSetup.SetupProject();
            }

            // --- state + test
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_highlighted, new GUIContent("Highlighted"));
                if (!Application.isPlaying) EditorGUILayout.PropertyField(_preview, new GUIContent("Scene Preview"));
            }
            EditorGUILayout.PropertyField(_trigger);
            if (_trigger.enumValueIndex == (int)PrismTrigger.MouseHover)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_selectOnClick);
                EditorGUILayout.HelpBox("Needs a collider and the legacy Input Manager. For XR, hook XRI hover events to SetHighlighted instead.", MessageType.None);
                EditorGUI.indentLevel--;
            }

            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Toggle")) foreach (var t in targets) ((PrismHighlight)t).ToggleHighlight();
                    if (GUILayout.Button("Hit FX")) foreach (var t in targets) ((PrismHighlight)t).HitFX();
                    if (GUILayout.Button("Select")) foreach (var t in targets) ((PrismHighlight)t).Select();
                    if (GUILayout.Button("Error")) foreach (var t in targets) ((PrismHighlight)t).Error();
                    if (GUILayout.Button("Target")) foreach (var t in targets) ((PrismHighlight)t).PlayTarget();
                }
            }

            // --- profile + preset
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_profile);
                if (GUILayout.Button("Create", GUILayout.Width(56))) CreateProfile(main);
            }
            if (_profile.objectReferenceValue != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(_syncWithProfile, new GUIContent("Sync On Enable"));
                    if (GUILayout.Button("Load", GUILayout.Width(56)))
                    {
                        foreach (var t in targets) { Undo.RecordObject(t, "Load Prism Profile"); ((PrismHighlight)t).ApplyProfile(); }
                        serializedObject.Update();
                    }
                    if (GUILayout.Button("Save", GUILayout.Width(56)))
                    {
                        Undo.RecordObject(_profile.objectReferenceValue, "Save Prism Profile");
                        main.SaveToProfile();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _preset = (PrismPreset)EditorGUILayout.EnumPopup("Preset", _preset);
                if (GUILayout.Button("Apply", GUILayout.Width(56)))
                {
                    foreach (var t in targets) { Undo.RecordObject(t, "Apply Prism Preset"); ((PrismHighlight)t).ApplyPreset(_preset); EditorUtility.SetDirty(t); }
                    serializedObject.Update();
                }
            }

            // --- sections
            if (Section("general", "General", null, C_General))
            {
                EditorGUI.indentLevel++;
                Fields("includeChildren", "fadeInDuration", "fadeOutDuration", "widthMode", "normalMode", "hullCull", "surfaceVisibility");
                if (GUILayout.Button("Refresh Renderers")) foreach (var t in targets) ((PrismHighlight)t).RefreshRenderers();
                EditorGUI.indentLevel--;
            }

            if (Section("outline", "Outline", P("outline"), C_Outline))
            {
                EditorGUI.indentLevel++;
                Fields("outlineColor", "outlineWidth", "outlineVisibility", "outlineDashMode");
                var mode = (PrismDashMode)P("outlineDashMode").enumValueIndex;
                if (mode != PrismDashMode.Solid)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(P("dashCount"), new GUIContent(mode == PrismDashMode.Radial ? "Dashes Per Loop" : mode == PrismDashMode.LinearWorld ? "Dashes Per Meter" : "Dash Density"));
                    Fields("dashFill", "dashSpeed");
                    if (mode != PrismDashMode.Radial) Fields("dashAngle");
                    Fields("dashSoftness");
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            if (Section("glow", "Outer Glow", P("glow"), C_Glow))
            {
                EditorGUI.indentLevel++;
                Fields("glowColor", "glowWidth", "glowPasses", "glowVisibility", "glowDashed", "glowDithering");
                if (P("glowDithering").boolValue) { EditorGUI.indentLevel++; Fields("glowDitherStrength"); EditorGUI.indentLevel--; }
                Fields("glowPulseSpeed", "glowPulseAmount");
                EditorGUI.indentLevel--;
            }

            if (Section("inner", "Inner Glow", P("innerGlow"), C_Inner))
            {
                EditorGUI.indentLevel++;
                Fields("innerGlowColor", "innerGlowPower");
                EditorGUI.indentLevel--;
            }

            if (Section("overlay", "Overlay", P("overlay"), C_Overlay))
            {
                EditorGUI.indentLevel++;
                Fields("overlayColor", "overlayTexture");
                if (P("overlayTexture").objectReferenceValue != null) Fields("overlayMapping", "overlayTextureScale", "overlayScroll");
                Fields("overlayBlending", "overlayMinIntensity", "overlayAnimationSpeed");
                EditorGUI.indentLevel--;
            }

            if (Section("holo", "Hologram", P("hologram"), C_Holo))
            {
                EditorGUI.indentLevel++;
                Fields("hologramMode", "hologramActivation");
                if (P("hologramMode").enumValueIndex == (int)PrismHologramMode.Replace)
                    EditorGUILayout.HelpBox("Replace swaps materials in play mode only. In edit mode it previews as Overlay.", MessageType.None);
                SubHeader("Color");
                Fields("hologramColor", "hologramRimColor", "hologramAlpha", "hologramFresnelPower", "hologramRimIntensity", "hologramTexture");
                if (P("hologramTexture").objectReferenceValue != null) Fields("hologramTextureInfluence");
                SubHeader("Scanlines");
                Fields("hologramScanSpace", "scanlineDensity", "scanlineSpeed", "scanlineThickness", "scanlineIntensity", "fineLineDensity", "fineLineIntensity");
                SubHeader("Scan Band");
                Fields("bandFrequency", "bandSpeed", "bandWidth", "bandIntensity");
                SubHeader("Flicker & Glitch");
                Fields("flickerSpeed", "flickerAmount", "glitchAmount", "glitchFrequency", "glitchSpeed", "glitchProbability");
                SubHeader("Render");
                Fields("hologramAdditive", "hologramDoubleSided", "hologramDepthPrepass");
                EditorGUI.indentLevel--;
            }

            if (Section("target", "Target", P("target"), C_Target))
            {
                EditorGUI.indentLevel++;
                Fields("targetTexture", "targetColor", "targetCenter", "targetRotationSpeed", "targetInitialScale", "targetEndScale",
                       "targetScaleToBounds", "targetAlignToGround");
                if (P("targetAlignToGround").boolValue) { EditorGUI.indentLevel++; Fields("targetGroundOffset", "groundMask"); EditorGUI.indentLevel--; }
                Fields("targetTransitionDuration", "targetStayDuration", "targetVisibility", "targetPulse");
                EditorGUI.indentLevel--;
            }

            if (Section("see", "See Through", P("seeThroughMode"), C_See))
            {
                EditorGUI.indentLevel++;
                Fields("seeThroughIntensity", "seeThroughColor", "seeThroughColorBlend", "seeThroughNoise", "seeThroughBorder");
                EditorGUI.indentLevel--;
            }

            if (Section("hit", "Hit FX", P("hitIntensity"), C_Hit))
            {
                EditorGUI.indentLevel++;
                Fields("hitMode", "hitDuration", "hitColor", "errorColor");
                EditorGUILayout.LabelField("From code: HitFX(), Select(), Error()", s_Sub);
                EditorGUI.indentLevel--;
            }

            if (Section("audio", "Audio", P("audioEnabled"), C_Audio))
            {
                EditorGUI.indentLevel++;
                Fields("highlightOnClip", "highlightOffClip", "hitClip", "selectClip", "errorClip", "hologramLoopClip",
                       "volume", "loopVolume", "pitchVariation", "spatialBlend", "outputGroup", "minSoundInterval");
                EditorGUI.indentLevel--;
            }

            if (Section("events", "Events", null, C_Events))
            {
                EditorGUILayout.PropertyField(_onStart);
                EditorGUILayout.PropertyField(_onEnd);
                EditorGUILayout.PropertyField(_onHit);
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (var t in targets) ((PrismHighlight)t).Refresh();
                SceneView.RepaintAll();
            }
        }

        static void SubHeader(string text)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(text, EditorStyles.miniBoldLabel);
        }

        void CreateProfile(PrismHighlight main)
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Prism Highlight Profile", main.name + "_PrismProfile", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var profile = CreateInstance<PrismHighlightProfile>();
            profile.settings.CopyFrom(main.settings);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            _profile.objectReferenceValue = profile;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
