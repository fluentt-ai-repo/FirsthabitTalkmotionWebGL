using UnityEditor;
using UnityEngine;
using System;
using System.IO;

public class AutoBuildTest
{
    private static readonly string buildBasePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build");
    
    [MenuItem("Build/Test Windows Build")]
    public static void BuildWindows()
    {
        Debug.Log("Starting Windows build...");
        
        string buildPath = Path.Combine(buildBasePath, "Windows", "FluenttModuleLibrary.exe");
        string buildFolder = Path.GetDirectoryName(buildPath);
        
        if (!Directory.Exists(buildFolder))
        {
            Directory.CreateDirectory(buildFolder);
        }
        
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new[] { "Assets/TestScene/AvatarCompatibilityTest/AvatarCompatibilityTest.unity" };
        buildPlayerOptions.locationPathName = buildPath;
        buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
        buildPlayerOptions.options = BuildOptions.None;
        
        Debug.Log($"Building to: {buildPath}");
        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"Windows build succeeded! Size: {report.summary.totalSize} bytes, Time: {report.summary.totalTime}");
        }
        else
        {
            Debug.LogError($"Windows build failed! Result: {report.summary.result}");
        }
    }
    
    [MenuItem("Build/Test WebGL Build")]
    public static void BuildWebGL()
    {
        Debug.Log("Starting WebGL build...");
        
        string buildPath = Path.Combine(buildBasePath, "WebGL");
        
        if (!Directory.Exists(buildPath))
        {
            Directory.CreateDirectory(buildPath);
        }
        
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new[] { "Assets/TestScene/AvatarCompatibilityTest/AvatarCompatibilityTest.unity" };
        buildPlayerOptions.locationPathName = buildPath;
        buildPlayerOptions.target = BuildTarget.WebGL;
        buildPlayerOptions.options = BuildOptions.None;
        
        Debug.Log($"Building to: {buildPath}");
        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"WebGL build succeeded! Size: {report.summary.totalSize} bytes, Time: {report.summary.totalTime}");
        }
        else
        {
            Debug.LogError($"WebGL build failed! Result: {report.summary.result}");
        }
    }
    
    [MenuItem("Build/Test Both Builds")]
    public static void BuildBoth()
    {
        Debug.Log("=== Starting build tests ===");
        BuildWindows();
        BuildWebGL();
        Debug.Log("=== Build tests completed ===");
    }
}