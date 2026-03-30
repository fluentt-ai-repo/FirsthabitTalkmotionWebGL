using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// Blacklist-based animation curve stripper for floating-head avatars.
    /// Only removes curves that match the blacklist. Everything else is kept.
    /// </summary>
    public class AnimationCurveStripper : EditorWindow
    {
        // Blacklist for blend shapes (checked against full propertyName)
        private static readonly string[] BlendShapeBlacklist = new[]
        {
            "blendShape.Eye_BL_A_L",
            "blendShape.Eye_BL_B_L",
            "blendShape.Eye_BL_A_R",
            "blendShape.Eye_BL_B_R",
            "blendShape.Eye_Big_L",
            "blendShape.Eye_Big_R",
            "blendShape.Mouth_Grim",
            "blendShape.Mouth_IN_L",
            "blendShape.Mouth_IN_R",
            "blendShape.Mouth_UP_L",
            "blendShape.Mouth_UP_R",
            "blendShape.Mouth_UN_L",
            "blendShape.Mouth_UN_R",
            "blendShape.Mouth_A",
            "blendShape.Mouth_O",
            "blendShape.Mouth_E",
            "blendShape.Mouth_Sm",
            "blendShape.Mouth_SRT_L",
            "blendShape.Mouth_SRT_R",
        };

        // Blacklist: curves starting with these prefixes will be REMOVED
        private static readonly string[] Blacklist = new[]
        {
            // === Arms: Shoulder → Fingers ===
            "Left Shoulder",
            "Right Shoulder",
            "Left Arm",
            "Right Arm",
            "Left Forearm",
            "Right Forearm",
            "Left Hand",
            "Right Hand",
            "LeftHand",
            "RightHand",

            // === Jaw ===
            "Jaw ",

            // === Legs: Upper Leg → Toes ===
            "Left Upper Leg",
            "Right Upper Leg",
            "Left Lower Leg",
            "Right Lower Leg",
            "Left Foot",
            "Right Foot",
            "Left Toes",
            "Right Toes",
            "LeftFoot",
            "RightFoot",
        };

        private Object[] selectedClips = new Object[0];
        private Vector2 scrollPos;
        private List<string> lastLog = new();
        private bool dryRun = true;

        [MenuItem("Tools/Firsthabit/Animation Curve Stripper")]
        public static void ShowWindow()
        {
            var window = GetWindow<AnimationCurveStripper>("Curve Stripper");
            window.minSize = new Vector2(450, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animation Curve Stripper", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Blacklist 방식 커브 정리 도구\n" +
                "제거: 양쪽 어깨~손가락, 양쪽 허벅지~발끝, Jaw,\n" +
                "      커스텀 BlendShape (Eye_BL/Big/Line, Mouth_*)\n" +
                "그 외 모든 커브는 유지됩니다.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Blacklist display
            if (EditorGUILayout.BeginFoldoutHeaderGroup(false, $"Blacklist ({Blacklist.Length} entries)"))
            {
                EditorGUI.indentLevel++;
                foreach (var entry in Blacklist)
                {
                    EditorGUILayout.LabelField(entry, EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(8);

            // Clip selection
            EditorGUILayout.LabelField("Animation Clips", EditorStyles.boldLabel);

            if (GUILayout.Button("Use Selected Assets in Project Window"))
            {
                selectedClips = Selection.objects
                    .Where(o => o is AnimationClip)
                    .ToArray();

                if (selectedClips.Length == 0)
                {
                    var clips = new List<Object>();
                    foreach (var obj in Selection.objects)
                    {
                        var path = AssetDatabase.GetAssetPath(obj);
                        if (string.IsNullOrEmpty(path)) continue;
                        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                        clips.AddRange(assets.Where(a => a is AnimationClip && !a.name.StartsWith("__preview__")));
                    }
                    selectedClips = clips.ToArray();
                }
            }

            EditorGUILayout.LabelField($"Selected: {selectedClips.Length} clip(s)");

            EditorGUILayout.Space(4);
            dryRun = EditorGUILayout.Toggle("Dry Run (preview only)", dryRun);

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Strip Selected Clips", GUILayout.Height(30)))
            {
                StripCurves(selectedClips, dryRun);
            }

            if (GUILayout.Button("Strip ALL in Animation folder", GUILayout.Height(30)))
            {
                if (dryRun || EditorUtility.DisplayDialog("Confirm",
                    "Assets/Animation 폴더 내 모든 AnimationClip을 처리합니다.\n계속하시겠습니까?", "Yes", "Cancel"))
                {
                    var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Animation" });
                    var allClips = guids
                        .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(c => c != null && !c.name.StartsWith("__preview__"))
                        .Cast<Object>()
                        .ToArray();
                    StripCurves(allClips, dryRun);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Log
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            foreach (var line in lastLog)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void StripCurves(Object[] clips, bool preview)
        {
            lastLog.Clear();
            int totalRemoved = 0;
            int totalKept = 0;

            foreach (var obj in clips)
            {
                var clip = obj as AnimationClip;
                if (clip == null) continue;

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var toRemove = new List<EditorCurveBinding>();
                int kept = 0;

                foreach (var binding in bindings)
                {
                    if (IsBlacklisted(binding.propertyName))
                        toRemove.Add(binding);
                    else
                        kept++;
                }

                if (toRemove.Count > 0)
                {
                    lastLog.Add($"--- {clip.name} ---");
                    lastLog.Add($"  Remove: {toRemove.Count}, Keep: {kept}");

                    foreach (var b in toRemove)
                        lastLog.Add($"  [-] {b.propertyName}");

                    if (!preview)
                    {
                        foreach (var b in toRemove)
                            AnimationUtility.SetEditorCurve(clip, b, null);
                        EditorUtility.SetDirty(clip);
                        lastLog.Add($"  => {toRemove.Count} curves removed!");
                    }
                    else
                    {
                        lastLog.Add($"  (dry run)");
                    }

                    totalRemoved += toRemove.Count;
                }
                else
                {
                    lastLog.Add($"--- {clip.name}: clean ---");
                }

                totalKept += kept;
            }

            lastLog.Insert(0, $"=== Total: {totalRemoved} removed, {totalKept} kept across {clips.Length} clip(s) ===");

            if (!preview && totalRemoved > 0)
            {
                AssetDatabase.SaveAssets();
                lastLog.Add("Assets saved.");
            }

            Repaint();
        }

        private static bool IsBlacklisted(string propertyName)
        {
            // Check blend shape blacklist (exact match)
            foreach (var name in BlendShapeBlacklist)
            {
                if (propertyName == name)
                    return true;
            }

            // Check prefix blacklist
            foreach (var prefix in Blacklist)
            {
                if (propertyName.StartsWith(prefix))
                    return true;
            }
            return false;
        }
    }
}
