using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// Reduces head/neck muscle curve values in an animation clip to create
    /// a subtler idle-style animation. Outputs a new .anim file.
    /// </summary>
    public class AnimationMotionDamper : EditorWindow
    {
        // Target muscle names for damping
        private static readonly string[] HeadNeckMuscles =
        {
            "Head Nod Down-Up",
            "Head Tilt Left-Right",
            "Head Turn Left-Right",
            "Neck Nod Down-Up",
            "Neck Tilt Left-Right",
            "Neck Turn Left-Right",
        };

        private AnimationClip sourceClip;
        private float headDamping = 0.3f;  // 30% of original
        private float neckDamping = 0.3f;
        private string outputSuffix = "_idle";
        private bool previewFoldout = true;

        // Preview data
        private List<CurvePreview> curvePreviewList = new();

        private struct CurvePreview
        {
            public string name;
            public float originalRange;
            public float dampedRange;
            public float damping;
        }

        [MenuItem("Tools/Firsthabit/Animation Motion Damper")]
        private static void ShowWindow()
        {
            var window = GetWindow<AnimationMotionDamper>("Motion Damper");
            window.minSize = new Vector2(400, 300);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animation Motion Damper", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Reduce head/neck rotation values to convert a motion clip into a subtle idle animation.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            // Source clip
            var newClip = (AnimationClip)EditorGUILayout.ObjectField(
                "Source Clip", sourceClip, typeof(AnimationClip), false);
            if (newClip != sourceClip)
            {
                sourceClip = newClip;
                RefreshPreview();
            }

            EditorGUILayout.Space(4);

            // Damping sliders
            EditorGUILayout.LabelField("Damping (0 = remove, 1 = keep original)", EditorStyles.miniLabel);

            float newHead = EditorGUILayout.Slider("Head", headDamping, 0f, 1f);
            float newNeck = EditorGUILayout.Slider("Neck", neckDamping, 0f, 1f);
            if (!Mathf.Approximately(newHead, headDamping) || !Mathf.Approximately(newNeck, neckDamping))
            {
                headDamping = newHead;
                neckDamping = newNeck;
                RefreshPreview();
            }

            EditorGUILayout.Space(4);
            outputSuffix = EditorGUILayout.TextField("Output Suffix", outputSuffix);

            // Preview
            if (sourceClip != null && curvePreviewList.Count > 0)
            {
                EditorGUILayout.Space(8);
                previewFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(previewFoldout, "Preview");
                if (previewFoldout)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Muscle", EditorStyles.miniLabel, GUILayout.Width(160));
                    EditorGUILayout.LabelField("Original Range", EditorStyles.miniLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Damped Range", EditorStyles.miniLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Factor", EditorStyles.miniLabel, GUILayout.Width(50));
                    EditorGUILayout.EndHorizontal();

                    foreach (var p in curvePreviewList)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(p.name, GUILayout.Width(160));
                        EditorGUILayout.LabelField($"{p.originalRange:F3}", GUILayout.Width(100));
                        EditorGUILayout.LabelField($"{p.dampedRange:F3}", GUILayout.Width(100));
                        EditorGUILayout.LabelField($"x{p.damping:F2}", GUILayout.Width(50));
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            // Generate button
            EditorGUILayout.Space(12);
            EditorGUI.BeginDisabledGroup(sourceClip == null);
            if (GUILayout.Button("Generate Damped Animation", GUILayout.Height(32)))
            {
                Generate();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void RefreshPreview()
        {
            curvePreviewList.Clear();
            if (sourceClip == null) return;

            var bindings = AnimationUtility.GetCurveBindings(sourceClip);
            foreach (var binding in bindings)
            {
                float damping = GetDampingForAttribute(binding.propertyName);
                if (damping >= 1f) continue; // Not a target curve

                var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                if (curve == null || curve.keys.Length == 0) continue;

                float min = float.MaxValue, max = float.MinValue;
                foreach (var key in curve.keys)
                {
                    min = Mathf.Min(min, key.value);
                    max = Mathf.Max(max, key.value);
                }

                float range = max - min;
                curvePreviewList.Add(new CurvePreview
                {
                    name = binding.propertyName,
                    originalRange = range,
                    dampedRange = range * damping,
                    damping = damping,
                });
            }
        }

        private float GetDampingForAttribute(string attribute)
        {
            foreach (var muscle in HeadNeckMuscles)
            {
                if (attribute != muscle) continue;
                return attribute.StartsWith("Head") ? headDamping : neckDamping;
            }
            return 1f; // Not a target
        }

        private void Generate()
        {
            // Determine output path
            string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
            string dir = Path.GetDirectoryName(sourcePath);
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(dir, baseName + outputSuffix + ".anim");
            outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);

            // Duplicate clip
            var newClip = new AnimationClip();
            EditorUtility.CopySerialized(sourceClip, newClip);
            newClip.name = Path.GetFileNameWithoutExtension(outputPath);

            // Damp target curves
            int dampedCount = 0;
            var bindings = AnimationUtility.GetCurveBindings(newClip);
            foreach (var binding in bindings)
            {
                float damping = GetDampingForAttribute(binding.propertyName);
                if (damping >= 1f) continue;

                var curve = AnimationUtility.GetEditorCurve(newClip, binding);
                if (curve == null) continue;

                // Calculate center value (average of min and max)
                float min = float.MaxValue, max = float.MinValue;
                foreach (var key in curve.keys)
                {
                    min = Mathf.Min(min, key.value);
                    max = Mathf.Max(max, key.value);
                }
                float center = (min + max) * 0.5f;

                // Damp: pull each keyframe value toward center
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    float offset = keys[i].value - center;
                    keys[i].value = center + offset * damping;
                    keys[i].inTangent *= damping;
                    keys[i].outTangent *= damping;
                }
                curve.keys = keys;

                AnimationUtility.SetEditorCurve(newClip, binding, curve);
                dampedCount++;
            }

            // Save
            AssetDatabase.CreateAsset(newClip, outputPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MotionDamper] Created '{outputPath}' — {dampedCount} curves damped (Head x{headDamping}, Neck x{neckDamping})");
            EditorGUIUtility.PingObject(newClip);
            Selection.activeObject = newClip;
        }
    }
}
