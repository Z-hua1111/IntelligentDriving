#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

public class BuildAndroid
{
    public static void BuildApk()
    {
        string buildPath = Path.Combine(Application.dataPath, "../build_android");

        if (!Directory.Exists(buildPath))
            Directory.CreateDirectory(buildPath);

        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = new string[] { "Assets/Scenes/SampleScene.unity" };
        options.locationPathName = buildPath + "/unity.apk";
        options.target = BuildTarget.Android;
        options.options = BuildOptions.Development;

        Debug.Log("[BuildAndroid] Starting Android build...");
        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[BuildAndroid] Build result: " + report.summary.result);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("[BuildAndroid] APK built at: " + options.locationPathName);
        }
        else
        {
            Debug.LogError("[BuildAndroid] Build FAILED! Check errors above.");
        }
    }
}
#endif
