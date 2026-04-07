using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// Extract animation clips from selected FBX files and save as independent .anim assets.
    /// Naming: {FBXName}_{AnimationName}.anim
    /// </summary>
    public class FbxAnimationExtractor : EditorWindow
    {
        private string outputFolder = "Assets/Animation/Extracted";
        private Vector2 scrollPos;
        private List<string> log = new();
        private bool overwriteExisting = false;

        [MenuItem("Tools/Firsthabit/FBX Animation Extractor")]
        public static void ShowWindow()
        {
            var window = GetWindow<FbxAnimationExtractor>("FBX Anim Extractor");
            window.minSize = new Vector2(500, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("FBX Animation Extractor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Project Window에서 FBX 파일을 선택한 뒤 Extract 버튼을 누르세요.\n" +
                "FBX 내 모든 AnimationClip을 독립 .anim 파일로 복사합니다.\n" +
                "파일명: {FBX이름}_{애니메이션이름}.anim",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Output folder picker
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string absolute = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                if (!string.IsNullOrEmpty(absolute))
                {
                    // Convert absolute path to Assets-relative path
                    string dataPath = Application.dataPath.Replace("\\", "/");
                    absolute = absolute.Replace("\\", "/");
                    if (absolute.StartsWith(dataPath))
                        outputFolder = "Assets" + absolute.Substring(dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("Error", "Assets 폴더 내부를 선택해 주세요.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", overwriteExisting);

            EditorGUILayout.Space(4);

            // Show selected FBX count
            var fbxPaths = GetSelectedFbxPaths();
            EditorGUILayout.LabelField($"Selected FBX: {fbxPaths.Count} file(s)");

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(fbxPaths.Count == 0))
            {
                if (GUILayout.Button("Extract Animations", GUILayout.Height(30)))
                {
                    ExtractAnimations(fbxPaths);
                }
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

        private List<string> GetSelectedFbxPaths()
        {
            var paths = new List<string>();
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".fbx")
                    paths.Add(path);
            }
            return paths.Distinct().ToList();
        }

        private void ExtractAnimations(List<string> fbxPaths)
        {
            log.Clear();

            // Ensure output folder exists
            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                CreateFolderRecursive(outputFolder);
                log.Add($"Created folder: {outputFolder}");
            }

            int totalExtracted = 0;
            int totalSkipped = 0;

            foreach (string fbxPath in fbxPaths)
            {
                string fbxName = Path.GetFileNameWithoutExtension(fbxPath);
                log.Add($"--- {fbxName} ({fbxPath}) ---");

                // Load all sub-assets and find AnimationClips
                var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                var clips = allAssets
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__"))
                    .ToList();

                if (clips.Count == 0)
                {
                    log.Add("  No animation clips found.");
                    continue;
                }

                foreach (var clip in clips)
                {
                    string safeFbxName = SanitizeFileName(fbxName);
                    string safeClipName = SanitizeFileName(clip.name);
                    string fileName = $"{safeFbxName}_{safeClipName}.anim";
                    string outputPath = $"{outputFolder}/{fileName}";

                    // Check existing
                    if (!overwriteExisting && AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null)
                    {
                        log.Add($"  [SKIP] {fileName} (already exists)");
                        totalSkipped++;
                        continue;
                    }

                    // Deep copy the clip
                    var newClip = new AnimationClip();
                    EditorUtility.CopySerialized(clip, newClip);
                    newClip.name = $"{safeFbxName}_{safeClipName}";

                    AssetDatabase.CreateAsset(newClip, outputPath);
                    log.Add($"  [OK] {fileName}");
                    totalExtracted++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.Insert(0, $"=== Extracted: {totalExtracted}, Skipped: {totalSkipped} from {fbxPaths.Count} FBX(s) ===");
            log.Add("Done.");
            Repaint();
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            return name;
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            // Normalize separators
            folderPath = folderPath.Replace("\\", "/");
            string[] parts = folderPath.Split('/');

            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
