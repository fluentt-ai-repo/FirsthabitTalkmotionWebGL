using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// Adds missing blendShape curves (value 0) to animation clips
    /// by comparing against a reference clip.
    /// Prevents blendShape "ghosting" when switching between animations
    /// that have different sets of blendShape curves.
    /// </summary>
    public class BlendShapeCurveFiller : EditorWindow
    {
        private AnimationClip referenceClip;
        private Object[] targetClips = new Object[0];
        private Vector2 scrollPos;
        private List<string> log = new();
        private bool showPreview;

        // Preview data
        private List<string> referenceShapes = new();
        private Dictionary<string, List<string>> missingPerClip = new();

        [MenuItem("Tools/Firsthabit/BlendShape Curve Filler")]
        public static void ShowWindow()
        {
            var window = GetWindow<BlendShapeCurveFiller>("BlendShape Filler");
            window.minSize = new Vector2(500, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("BlendShape Curve Filler", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Reference 클립에 있는 blendShape 커브 중\n" +
                "Target 클립에 없는 커브를 값 0으로 추가합니다.\n\n" +
                "용도: 서로 다른 blendShape 세트를 가진 애니메이션 간\n" +
                "전환 시 이전 값이 리셋되지 않는 문제를 방지합니다.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Reference clip
            EditorGUILayout.LabelField("Reference Clip (기준)", EditorStyles.boldLabel);
            referenceClip = (AnimationClip)EditorGUILayout.ObjectField(
                "Reference", referenceClip, typeof(AnimationClip), false);

            EditorGUILayout.Space(4);

            // Target clips
            EditorGUILayout.LabelField("Target Clips (대상)", EditorStyles.boldLabel);
            if (GUILayout.Button("Use Selected Assets in Project Window"))
            {
                targetClips = Selection.objects
                    .Where(o => o is AnimationClip)
                    .ToArray();

                if (targetClips.Length == 0)
                {
                    var clips = new List<Object>();
                    foreach (var obj in Selection.objects)
                    {
                        var path = AssetDatabase.GetAssetPath(obj);
                        if (string.IsNullOrEmpty(path)) continue;
                        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                        clips.AddRange(assets.Where(a =>
                            a is AnimationClip && !a.name.StartsWith("__preview__")));
                    }
                    targetClips = clips.ToArray();
                }
            }

            EditorGUILayout.LabelField($"Selected: {targetClips.Length} clip(s)");

            EditorGUILayout.Space(8);

            // Buttons
            using (new EditorGUI.DisabledScope(referenceClip == null || targetClips.Length == 0))
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Analyze (미리보기)", GUILayout.Height(30)))
                {
                    Analyze();
                }

                if (GUILayout.Button("Fill Missing Curves", GUILayout.Height(30)))
                {
                    FillMissingCurves();
                }

                EditorGUILayout.EndHorizontal();
            }

            // Log
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            foreach (var line in log)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Collect all blendShape bindings from the reference clip,
        /// grouped by (path, classID) so we handle multiple SkinnedMeshRenderers.
        /// </summary>
        private List<EditorCurveBinding> GetReferenceBlendShapeBindings()
        {
            var bindings = AnimationUtility.GetCurveBindings(referenceClip);
            return bindings
                .Where(b => b.propertyName.StartsWith("blendShape."))
                .ToList();
        }

        /// <summary>
        /// Get existing blendShape property names for a clip, keyed by (path, propertyName).
        /// </summary>
        private HashSet<string> GetBlendShapeKeys(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var keys = new HashSet<string>();
            foreach (var b in bindings)
            {
                if (b.propertyName.StartsWith("blendShape."))
                    keys.Add($"{b.path}|{b.propertyName}");
            }
            return keys;
        }

        private void Analyze()
        {
            log.Clear();

            if (referenceClip == null)
            {
                log.Add("ERROR: Reference clip is not set.");
                Repaint();
                return;
            }

            var refBindings = GetReferenceBlendShapeBindings();
            log.Add($"=== Reference: {referenceClip.name} — {refBindings.Count} blendShape curves ===");

            // Deduplicate by (path, propertyName) — reference may have duplicate paths
            var uniqueRef = new Dictionary<string, EditorCurveBinding>();
            foreach (var b in refBindings)
            {
                string key = $"{b.path}|{b.propertyName}";
                if (!uniqueRef.ContainsKey(key))
                    uniqueRef[key] = b;
            }

            log.Add($"Unique (path + property) pairs: {uniqueRef.Count}");
            log.Add("");

            int totalMissing = 0;
            foreach (var obj in targetClips)
            {
                var clip = obj as AnimationClip;
                if (clip == null) continue;

                var existing = GetBlendShapeKeys(clip);
                var missing = uniqueRef.Keys.Where(k => !existing.Contains(k)).ToList();

                log.Add($"--- {clip.name} ---");
                log.Add($"  Existing: {existing.Count}, Missing: {missing.Count}");

                foreach (var key in missing)
                {
                    var parts = key.Split('|');
                    log.Add($"  [+] {parts[1]}  (path: {parts[0]})");
                }

                totalMissing += missing.Count;
            }

            log.Insert(log.Count, "");
            log.Add($"=== Total missing curves to add: {totalMissing} ===");
            Repaint();
        }

        private void FillMissingCurves()
        {
            log.Clear();

            if (referenceClip == null)
            {
                log.Add("ERROR: Reference clip is not set.");
                Repaint();
                return;
            }

            var refBindings = GetReferenceBlendShapeBindings();

            // Deduplicate
            var uniqueRef = new Dictionary<string, EditorCurveBinding>();
            foreach (var b in refBindings)
            {
                string key = $"{b.path}|{b.propertyName}";
                if (!uniqueRef.ContainsKey(key))
                    uniqueRef[key] = b;
            }

            log.Add($"=== Reference: {referenceClip.name} — {uniqueRef.Count} unique blendShape bindings ===");

            // Create a constant zero curve (two keyframes: start and end at 0)
            // Using two keyframes ensures the curve covers the clip's full duration.
            int totalAdded = 0;
            int clipCount = 0;

            foreach (var obj in targetClips)
            {
                var clip = obj as AnimationClip;
                if (clip == null) continue;

                var existing = GetBlendShapeKeys(clip);
                int addedForClip = 0;

                foreach (var kvp in uniqueRef)
                {
                    if (existing.Contains(kvp.Key))
                        continue;

                    var refBinding = kvp.Value;

                    // Create a constant zero curve spanning the clip duration
                    var zeroCurve = new AnimationCurve(
                        new Keyframe(0f, 0f),
                        new Keyframe(clip.length, 0f)
                    );

                    // Match the binding (path, type, propertyName)
                    var newBinding = new EditorCurveBinding
                    {
                        path = refBinding.path,
                        type = refBinding.type,
                        propertyName = refBinding.propertyName
                    };

                    AnimationUtility.SetEditorCurve(clip, newBinding, zeroCurve);
                    addedForClip++;
                }

                if (addedForClip > 0)
                {
                    EditorUtility.SetDirty(clip);
                    clipCount++;
                }

                log.Add($"  {clip.name}: +{addedForClip} curves added");
                totalAdded += addedForClip;
            }

            if (totalAdded > 0)
                AssetDatabase.SaveAssets();

            log.Add("");
            log.Add($"=== Done: {totalAdded} curves added to {clipCount} clip(s) ===");
            Repaint();
        }
    }
}
