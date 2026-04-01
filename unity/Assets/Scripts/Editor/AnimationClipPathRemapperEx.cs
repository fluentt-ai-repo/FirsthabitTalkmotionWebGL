using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// Extended Animation Clip Path Remapper.
    /// Supports multiple path remapping pairs and "Apply to Original" option.
    /// </summary>
    public class AnimationClipPathRemapperEx : EditorWindow
    {
        private const string PrefKeyPairs = "PathRemapperEx_Pairs";

        private enum ApplyMode
        {
            CopyToFolder,
            ApplyToOriginal
        }

        [System.Serializable]
        private class PathPair
        {
            public string oldPath = "";
            public string newPath = "";
        }

        private List<PathPair> pathPairs = new() { new PathPair { oldPath = "Face", newPath = "head_grp/face" } };
        private List<Object> selectedAssets = new();
        private string outputFolder = "Assets/ProcessedAnimations";
        private ApplyMode applyMode = ApplyMode.CopyToFolder;
        private Vector2 scrollPosition;
        private Vector2 pairScrollPosition;
        private Vector2 logScrollPosition;
        private List<string> log = new();
        private bool showPathPairs = true;
        private string newOldPath = "";
        private string newNewPath = "";

        [MenuItem("Tools/Firsthabit/Animation Path Remapper Ex")]
        public static void ShowWindow()
        {
            var window = GetWindow<AnimationClipPathRemapperEx>("Path Remapper Ex");
            window.minSize = new Vector2(520, 550);
        }

        private void OnEnable()
        {
            LoadPairs();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Animation Clip Path Remapper", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "AnimationClip의 커브 경로(path)를 일괄 재매핑합니다.\n" +
                "여러 경로 쌍을 등록하여 한 번에 변환할 수 있습니다.\n\n" +
                "• Copy to Folder: 원본 유지, 새 .anim 생성 (FBX/Clip 모두 가능)\n" +
                "• Apply to Original: 원본 .anim 파일에 직접 적용 (독립 .anim만 가능)\n\n" +
                "⚠ 주의: Unity Animation 창에서 보이는 경로와 실제 데이터가 다를 수 있습니다.\n" +
                "  • CamelCase 자동 공백: 실제 \"headGrp\"이 \"head Grp\"으로 표시됨\n" +
                "  • 여기에는 실제 경로를 입력하세요 (공백 없이)\n" +
                "  • 경로 구분자는 / 사용 (예: taerin_t_head_grp/taerin_t_face1)\n" +
                "  • 확인 방법: .anim 파일을 텍스트 에디터로 열어 path: 값 확인",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Path pairs
            DrawPathPairs();

            EditorGUILayout.Space(8);

            // Apply mode
            EditorGUILayout.LabelField("Apply Mode", EditorStyles.boldLabel);
            applyMode = (ApplyMode)EditorGUILayout.EnumPopup("Mode", applyMode);

            if (applyMode == ApplyMode.ApplyToOriginal)
            {
                EditorGUILayout.HelpBox(
                    "원본 .anim 파일을 직접 수정합니다.\n" +
                    "FBX 내장 클립은 수정 불가하므로 자동 건너뜁니다.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        string dataPath = Application.dataPath.Replace("\\", "/");
                        path = path.Replace("\\", "/");
                        if (path.StartsWith(dataPath))
                            outputFolder = "Assets" + path.Substring(dataPath.Length);
                        else
                            EditorUtility.DisplayDialog("Error", "Assets 폴더 내부를 선택해 주세요.", "OK");
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);

            // Assets list
            EditorGUILayout.LabelField("Assets to Process", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Selected Assets"))
                AddSelectedAssets();
            if (GUILayout.Button("Clear List"))
                selectedAssets.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(120));
            for (int i = 0; i < selectedAssets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                selectedAssets[i] = EditorGUILayout.ObjectField(selectedAssets[i], typeof(Object), false);

                string label = GetAssetTypeLabel(selectedAssets[i]);
                EditorGUILayout.LabelField(label, GUILayout.Width(60));

                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    selectedAssets.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);

            // Action buttons
            var validPairs = pathPairs.Where(p =>
                !string.IsNullOrEmpty(p.oldPath) &&
                !string.IsNullOrEmpty(p.newPath) &&
                p.oldPath != p.newPath).ToList();

            bool canExecute = selectedAssets.Count > 0 && validPairs.Count > 0;

            using (new EditorGUI.DisabledScope(!canExecute))
            {
                if (GUILayout.Button("Preview Changes", GUILayout.Height(28)))
                    PreviewChanges(validPairs);

                EditorGUILayout.Space(4);

                string buttonLabel = applyMode == ApplyMode.ApplyToOriginal
                    ? "Apply to Original Files"
                    : "Extract & Remap to Output Folder";

                GUI.backgroundColor = applyMode == ApplyMode.ApplyToOriginal
                    ? new Color(1f, 0.7f, 0.3f)
                    : Color.green;

                if (GUILayout.Button(buttonLabel, GUILayout.Height(36)))
                    ExecuteRemap(validPairs);

                GUI.backgroundColor = Color.white;
            }

            // Log
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.ExpandHeight(true));
            foreach (var line in log)
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPathPairs()
        {
            showPathPairs = EditorGUILayout.BeginFoldoutHeaderGroup(showPathPairs,
                $"Path Remapping Pairs ({pathPairs.Count})");

            if (showPathPairs)
            {
                // Header
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Old Path", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("", GUILayout.Width(20)); // arrow space
                EditorGUILayout.LabelField("New Path", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("", GUILayout.Width(25)); // X button space
                EditorGUILayout.EndHorizontal();

                pairScrollPosition = EditorGUILayout.BeginScrollView(pairScrollPosition, GUILayout.MaxHeight(150));
                bool changed = false;

                for (int i = 0; i < pathPairs.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();

                    string editedOld = EditorGUILayout.TextField(pathPairs[i].oldPath);
                    EditorGUILayout.LabelField("→", GUILayout.Width(20));
                    string editedNew = EditorGUILayout.TextField(pathPairs[i].newPath);

                    if (editedOld != pathPairs[i].oldPath || editedNew != pathPairs[i].newPath)
                    {
                        pathPairs[i].oldPath = editedOld;
                        pathPairs[i].newPath = editedNew;
                        changed = true;
                    }

                    if (GUILayout.Button("X", GUILayout.Width(25)))
                    {
                        pathPairs.RemoveAt(i);
                        changed = true;
                        i--;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                // Add new pair
                EditorGUILayout.BeginHorizontal();
                newOldPath = EditorGUILayout.TextField(newOldPath);
                EditorGUILayout.LabelField("→", GUILayout.Width(20));
                newNewPath = EditorGUILayout.TextField(newNewPath);

                bool canAdd = !string.IsNullOrWhiteSpace(newOldPath) && !string.IsNullOrWhiteSpace(newNewPath);
                using (new EditorGUI.DisabledScope(!canAdd))
                {
                    if (GUILayout.Button("+", GUILayout.Width(25)))
                    {
                        pathPairs.Add(new PathPair { oldPath = newOldPath.Trim(), newPath = newNewPath.Trim() });
                        newOldPath = "";
                        newNewPath = "";
                        changed = true;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (changed)
                    SavePairs();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private string GetAssetTypeLabel(Object asset)
        {
            if (asset == null) return "";
            if (asset is AnimationClip)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                bool isStandalone = path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase);
                return isStandalone ? "[Clip]" : "[FBX Clip]";
            }
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                return "[FBX]";
            return "[?]";
        }

        private void AddSelectedAssets()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj == null || selectedAssets.Contains(obj)) continue;
                string assetPath = AssetDatabase.GetAssetPath(obj);

                if (obj is AnimationClip || assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    selectedAssets.Add(obj);
            }
        }

        private List<(AnimationClip clip, string sourceName, bool isStandalone)> CollectClips()
        {
            var result = new List<(AnimationClip, string, bool)>();

            foreach (var asset in selectedAssets)
            {
                if (asset == null) continue;

                if (asset is AnimationClip clip)
                {
                    string path = AssetDatabase.GetAssetPath(clip);
                    bool standalone = path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase);
                    string sourceName = Path.GetFileNameWithoutExtension(path);
                    result.Add((clip, sourceName, standalone));
                }
                else
                {
                    string assetPath = AssetDatabase.GetAssetPath(asset);
                    if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                    string fbxName = Path.GetFileNameWithoutExtension(assetPath);
                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (var sub in subAssets)
                    {
                        if (sub is AnimationClip c && !c.name.Contains("__preview__"))
                            result.Add((c, fbxName, false));
                    }
                }
            }
            return result;
        }

        private void PreviewChanges(List<PathPair> pairs)
        {
            log.Clear();
            foreach (var pair in pairs)
                log.Add($"=== '{pair.oldPath}' → '{pair.newPath}' ===");

            var clips = CollectClips();
            int totalMatch = 0;

            foreach (var (clip, sourceName, isStandalone) in clips)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                int matchCount = 0;
                foreach (var b in bindings)
                {
                    if (pairs.Any(p => p.oldPath == b.path))
                        matchCount++;
                }

                string typeTag = isStandalone ? "Clip" : "FBX";
                if (matchCount > 0)
                {
                    log.Add($"  [{typeTag}] {clip.name}: {matchCount} curves match");
                    totalMatch += matchCount;
                }
                else
                {
                    log.Add($"  [{typeTag}] {clip.name}: no match");
                }

                if (applyMode == ApplyMode.ApplyToOriginal && !isStandalone)
                    log.Add($"    ⚠ FBX 내장 클립 → Apply to Original 시 건너뜀");
            }

            log.Add($"--- Total: {totalMatch} curves in {clips.Count} clip(s) ---");
            Repaint();
        }

        private void ExecuteRemap(List<PathPair> pairs)
        {
            var clips = CollectClips();

            int eligibleCount = 0;
            foreach (var (_, _, isStandalone) in clips)
            {
                if (applyMode == ApplyMode.ApplyToOriginal && !isStandalone) continue;
                eligibleCount++;
            }

            // Build confirmation message
            var msg = new StringBuilder();
            msg.AppendLine("Path Remapping:");
            foreach (var pair in pairs)
                msg.AppendLine($"  '{pair.oldPath}' → '{pair.newPath}'");
            msg.AppendLine();

            string modeLabel = applyMode == ApplyMode.ApplyToOriginal
                ? "원본 파일을 직접 수정합니다."
                : $"Output Folder: {outputFolder}";
            msg.AppendLine(modeLabel);
            msg.AppendLine($"대상: {eligibleCount} clip(s)");
            msg.AppendLine("\n계속하시겠습니까?");

            if (!EditorUtility.DisplayDialog("Confirm Path Remapping",
                msg.ToString(), "Apply", "Cancel"))
                return;

            log.Clear();

            if (applyMode == ApplyMode.ApplyToOriginal)
                ExecuteApplyToOriginal(clips, pairs);
            else
                ExecuteCopyToFolder(clips, pairs);

            Repaint();
        }

        private void ExecuteApplyToOriginal(
            List<(AnimationClip clip, string sourceName, bool isStandalone)> clips,
            List<PathPair> pairs)
        {
            int modified = 0;
            int skipped = 0;
            int totalCurves = 0;

            foreach (var (clip, sourceName, isStandalone) in clips)
            {
                if (!isStandalone)
                {
                    log.Add($"  [SKIP] {clip.name} (FBX 내장 클립은 직접 수정 불가)");
                    skipped++;
                    continue;
                }

                int remapped = RemapClipPaths(clip, pairs);
                if (remapped > 0)
                {
                    EditorUtility.SetDirty(clip);
                    log.Add($"  [OK] {clip.name}: {remapped} curves remapped (in-place)");
                    modified++;
                    totalCurves += remapped;
                }
                else
                {
                    log.Add($"  [--] {clip.name}: no matching curves");
                }
            }

            AssetDatabase.SaveAssets();
            log.Insert(0, $"=== Apply to Original: {modified} modified, {skipped} skipped, {totalCurves} curves ===");
        }

        private void ExecuteCopyToFolder(
            List<(AnimationClip clip, string sourceName, bool isStandalone)> clips,
            List<PathPair> pairs)
        {
            CreateFolderRecursive(outputFolder);

            int created = 0;
            int totalCurves = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var (clip, sourceName, isStandalone) in clips)
                {
                    var newClip = Object.Instantiate(clip);
                    newClip.name = clip.name;

                    int remapped = RemapClipPaths(newClip, pairs);
                    totalCurves += remapped;

                    string fileName = isStandalone
                        ? $"{clip.name}.anim"
                        : $"{sourceName}_{clip.name}.anim";
                    string outputPath = $"{outputFolder}/{SanitizeFileName(fileName)}";

                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null)
                    {
                        string baseName = Path.GetFileNameWithoutExtension(outputPath);
                        int counter = 1;
                        do
                        {
                            outputPath = $"{outputFolder}/{baseName}_{counter}.anim";
                            counter++;
                        } while (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null);
                    }

                    AssetDatabase.CreateAsset(newClip, outputPath);
                    log.Add($"  [OK] {outputPath} ({remapped} remapped)");
                    created++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            log.Insert(0, $"=== Copy to Folder: {created} created, {totalCurves} curves remapped ===");
        }

        private static int RemapClipPaths(AnimationClip clip, List<PathPair> pairs)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            int count = 0;

            foreach (var binding in bindings)
            {
                var match = pairs.FirstOrDefault(p => p.oldPath == binding.path);
                if (match == null) continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                var newBinding = new EditorCurveBinding
                {
                    path = match.newPath,
                    type = binding.type,
                    propertyName = binding.propertyName
                };

                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                count++;
            }

            return count;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            folderPath = folderPath.Replace("\\", "/");
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        #region EditorPrefs Persistence

        private void SavePairs()
        {
            var entries = pathPairs.Select(p => $"{p.oldPath}|{p.newPath}");
            EditorPrefs.SetString(PrefKeyPairs, string.Join("||", entries));
        }

        private void LoadPairs()
        {
            if (!EditorPrefs.HasKey(PrefKeyPairs)) return;

            string raw = EditorPrefs.GetString(PrefKeyPairs, "");
            if (string.IsNullOrEmpty(raw)) return;

            pathPairs.Clear();
            foreach (string entry in raw.Split(new[] { "||" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split('|');
                if (parts.Length == 2)
                    pathPairs.Add(new PathPair { oldPath = parts[0], newPath = parts[1] });
            }

            if (pathPairs.Count == 0)
                pathPairs.Add(new PathPair { oldPath = "Face", newPath = "head_grp/face" });
        }

        #endregion
    }
}
