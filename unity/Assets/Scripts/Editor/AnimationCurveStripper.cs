using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// Blacklist-based animation curve stripper for floating-head avatars.
    /// Only removes curves that match the blacklist. Everything else is kept.
    /// Blacklist is editable in the UI and persisted via EditorPrefs.
    /// </summary>
    public class AnimationCurveStripper : EditorWindow
    {
        private const string PrefKeyMuscle = "CurveStripper_MuscleBlacklist";
        private const string PrefKeyBlendShape = "CurveStripper_BlendShapeBlacklist";

        // Default blacklists (used on first launch or reset)
        private static readonly string[] DefaultMuscleBlacklist =
        {
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
            "Jaw ",
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

        private static readonly string[] DefaultBlendShapeBlacklist =
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

        // Editable blacklists
        private List<string> muscleBlacklist = new();
        private List<string> blendShapeBlacklist = new();

        private Object[] selectedClips = new Object[0];
        private Vector2 scrollPos;
        private List<string> lastLog = new();
        private bool showMuscleBlacklist = false;
        private bool showBlendShapeBlacklist = false;
        private string newMuscleEntry = "";
        private string newBlendShapeEntry = "";

        // Analysis result for curve info
        private struct CurveInfo
        {
            public EditorCurveBinding binding;
            public int keyCount;
        }

        [MenuItem("Tools/Firsthabit/Animation Curve Stripper")]
        public static void ShowWindow()
        {
            var window = GetWindow<AnimationCurveStripper>("Curve Stripper");
            window.minSize = new Vector2(450, 400);
        }

        private void OnEnable()
        {
            muscleBlacklist = LoadList(PrefKeyMuscle, DefaultMuscleBlacklist);
            blendShapeBlacklist = LoadList(PrefKeyBlendShape, DefaultBlendShapeBlacklist);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animation Curve Stripper", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Blacklist 방식 커브 정리 도구\n" +
                "Muscle Blacklist: prefix 매칭 (시작 문자열 일치 시 제거)\n" +
                "BlendShape Blacklist: exact 매칭 (정확히 일치 시 제거)\n" +
                "그 외 모든 커브는 유지됩니다.\n\n" +
                "⚠ 주의: Unity Editor의 Animation 창에서는 CamelCase에 자동으로\n" +
                "공백이 삽입되어 표시됩니다. (예: NeckHide → \"Neck Hide\")\n" +
                "여기에는 실제 property 이름을 입력해야 합니다. (공백 없이)\n" +
                "BlendShape도 마찬가지로 실제 이름은 \"blendShape.이름\" 형식입니다.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Muscle Blacklist
            DrawEditableBlacklist(
                "Muscle Blacklist (prefix match)",
                ref showMuscleBlacklist,
                muscleBlacklist,
                ref newMuscleEntry,
                PrefKeyMuscle,
                DefaultMuscleBlacklist);

            // BlendShape Blacklist
            DrawEditableBlacklist(
                "BlendShape Blacklist (exact match)",
                ref showBlendShapeBlacklist,
                blendShapeBlacklist,
                ref newBlendShapeEntry,
                PrefKeyBlendShape,
                DefaultBlendShapeBlacklist);

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

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Strip Selected Clips", GUILayout.Height(30)))
            {
                AnalyzeAndConfirm(selectedClips);
            }

            if (GUILayout.Button("Strip ALL in Animation folder", GUILayout.Height(30)))
            {
                var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Animation" });
                var allClips = guids
                    .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(c => c != null && !c.name.StartsWith("__preview__"))
                    .Cast<Object>()
                    .ToArray();
                AnalyzeAndConfirm(allClips);
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

        private void DrawEditableBlacklist(
            string title,
            ref bool foldout,
            List<string> list,
            ref string newEntry,
            string prefKey,
            string[] defaults)
        {
            foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, $"{title} ({list.Count})");
            if (foldout)
            {
                EditorGUI.indentLevel++;

                for (int i = 0; i < list.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    string edited = EditorGUILayout.TextField(list[i]);
                    if (edited != list[i])
                    {
                        list[i] = edited;
                        SaveList(prefKey, list);
                    }
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    {
                        list.RemoveAt(i);
                        SaveList(prefKey, list);
                        i--;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                newEntry = EditorGUILayout.TextField(newEntry);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newEntry)))
                {
                    if (GUILayout.Button("+", GUILayout.Width(22)))
                    {
                        list.Add(newEntry.Trim());
                        SaveList(prefKey, list);
                        newEntry = "";
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
                {
                    if (EditorUtility.DisplayDialog("Reset Blacklist",
                        "현재 목록을 기본값으로 초기화합니다.\n직접 추가한 항목이 모두 사라집니다.\n\n계속하시겠습니까?",
                        "초기화", "취소"))
                    {
                        list.Clear();
                        list.AddRange(defaults);
                        SaveList(prefKey, list);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>
        /// Step 1: Analyze clips and show confirmation dialog with keyframe warnings.
        /// Step 2: User confirms → apply.
        /// </summary>
        private void AnalyzeAndConfirm(Object[] clips)
        {
            lastLog.Clear();

            // Analyze all clips
            var analysisPerClip = new List<(AnimationClip clip, List<CurveInfo> toRemove, int kept)>();
            int totalRemove = 0;
            int totalKept = 0;
            int curvesWithKeys = 0;    // curves with 2 keyframes (some data)
            int curvesWithAnim = 0;    // curves with 3+ keyframes (likely animated)
            var warningLines = new List<string>();
            var infoLines = new List<string>();

            foreach (var obj in clips)
            {
                var clip = obj as AnimationClip;
                if (clip == null) continue;

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var toRemove = new List<CurveInfo>();
                int kept = 0;

                foreach (var binding in bindings)
                {
                    if (IsBlacklisted(binding.propertyName))
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        int keyCount = curve?.length ?? 0;
                        toRemove.Add(new CurveInfo { binding = binding, keyCount = keyCount });

                        if (keyCount >= 3)
                        {
                            curvesWithAnim++;
                            warningLines.Add($"  ⚠ {clip.name} / {binding.propertyName} ({keyCount} keys)");
                        }
                        else if (keyCount > 1)
                        {
                            curvesWithKeys++;
                            infoLines.Add($"  ℹ {clip.name} / {binding.propertyName} ({keyCount} keys)");
                        }
                    }
                    else
                    {
                        kept++;
                    }
                }

                if (toRemove.Count > 0)
                    analysisPerClip.Add((clip, toRemove, kept));

                totalRemove += toRemove.Count;
                totalKept += kept;
            }

            // Nothing to remove
            if (totalRemove == 0)
            {
                lastLog.Add($"=== {clips.Length} clip(s) 분석 완료: 제거할 커브 없음 ===");
                Repaint();
                return;
            }

            // Build log preview
            foreach (var (clip, toRemove, kept) in analysisPerClip)
            {
                lastLog.Add($"--- {clip.name} ---");
                lastLog.Add($"  Remove: {toRemove.Count}, Keep: {kept}");
                foreach (var info in toRemove)
                {
                    string keyTag = info.keyCount >= 3 ? $" ⚠ {info.keyCount} keys"
                                  : info.keyCount > 1  ? $" ℹ {info.keyCount} keys"
                                  : "";
                    lastLog.Add($"  [-] {info.binding.propertyName}{keyTag}");
                }
            }
            lastLog.Insert(0, $"=== 분석: {totalRemove} remove, {totalKept} keep across {clips.Length} clip(s) ===");
            Repaint();

            // Build confirmation dialog message
            var msg = new StringBuilder();
            msg.AppendLine($"총 {analysisPerClip.Count}개 클립에서 {totalRemove}개 커브를 제거합니다.");
            msg.AppendLine($"유지: {totalKept}개 커브");

            if (curvesWithAnim > 0)
            {
                msg.AppendLine();
                msg.AppendLine($"⚠ 경고: {curvesWithAnim}개 커브에 키프레임이 3개 이상 있습니다.");
                msg.AppendLine("   실제 애니메이션 데이터가 포함되어 있을 수 있습니다!");
                foreach (var line in warningLines)
                    msg.AppendLine(line);
            }

            if (curvesWithKeys > 0)
            {
                msg.AppendLine();
                msg.AppendLine($"ℹ 알림: {curvesWithKeys}개 커브에 키프레임이 2개 있습니다.");
                foreach (var line in infoLines)
                    msg.AppendLine(line);
            }

            msg.AppendLine();
            msg.AppendLine("계속하시겠습니까?");

            // Show confirmation dialog
            MessageType dialogType = curvesWithAnim > 0 ? MessageType.Warning : MessageType.Info;
            string dialogTitle = curvesWithAnim > 0
                ? "⚠ 경고: 애니메이션 데이터가 있는 커브 포함"
                : "커브 제거 확인";

            if (!EditorUtility.DisplayDialog(dialogTitle, msg.ToString(), "제거 실행", "취소"))
            {
                lastLog.Add("--- 사용자 취소 ---");
                Repaint();
                return;
            }

            // Step 2: Apply
            int actualRemoved = 0;
            foreach (var (clip, toRemove, _) in analysisPerClip)
            {
                foreach (var info in toRemove)
                {
                    AnimationUtility.SetEditorCurve(clip, info.binding, null);
                    actualRemoved++;
                }
                EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
            lastLog.Add($"=== 완료: {actualRemoved}개 커브 제거됨 ===");
            Repaint();
        }

        private bool IsBlacklisted(string propertyName)
        {
            foreach (var name in blendShapeBlacklist)
            {
                if (propertyName == name)
                    return true;
            }
            foreach (var prefix in muscleBlacklist)
            {
                if (propertyName.StartsWith(prefix))
                    return true;
            }
            return false;
        }

        #region EditorPrefs Persistence

        private static void SaveList(string key, List<string> list)
        {
            EditorPrefs.SetString(key, string.Join("|", list));
        }

        private static List<string> LoadList(string key, string[] defaults)
        {
            if (!EditorPrefs.HasKey(key))
                return new List<string>(defaults);

            string raw = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(raw))
                return new List<string>();

            return raw.Split('|').ToList();
        }

        #endregion
    }
}
