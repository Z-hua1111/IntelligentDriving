#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// 无头（batchmode）导出 Android Gradle 工程。
/// 目的是把最新的 C# 逻辑真正编进 libil2cpp.so（只重建 data.unity3d 是做不到的）。
///
/// 用法：
///   Unity.exe -batchmode -quit -projectPath <工程> ^
///             -executeMethod BuildAndroidExport.Run -logFile <日志> ^
///             --exportPath <导出目录>
/// </summary>
public static class BuildAndroidExport
{
    // 模拟器访问宿主机的固定地址（与已发布的 APK 保持一致）
    private const string TargetUrl = "ws://10.0.2.2:8765";
    private const string PackageName = "com.example.smartdrivingvisualization";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    private static string exportPath = "d:/intelligent driving/unity_android_export";

    [MenuItem("Tools/ChangeIPAndBuild/4. 导出 Android 工程")]
    public static void Run()
    {
        string[] cmdArgs = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < cmdArgs.Length; i++)
        {
            if (cmdArgs[i] == "--exportPath" && i + 1 < cmdArgs.Length)
                exportPath = cmdArgs[i + 1];
        }

        Debug.Log("[ExportAndroid] 开始，导出目录: " + exportPath);

        // ---------- 1. 切平台 ----------
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[ExportAndroid] 切换构建平台到 Android ...");
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);
            if (!ok)
            {
                Debug.LogError("[ExportAndroid] 切换平台失败：可能没装 Android Build Support");
                EditorApplication.Exit(2);
                return;
            }
        }

        // ---------- 2. 包名 / 后端 / 架构 ----------
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageName);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        Debug.Log("[ExportAndroid] 包名=" + PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)
                  + " 架构=" + PlayerSettings.Android.targetArchitectures
                  + " 后端=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android));

        // ---------- 3. 把场景里的 serverUrl 改对（等价于 Tools/1. 修改 IP） ----------
        FixSceneServerUrl();

        // ---------- 4. 导出 Gradle 工程 ----------
        if (Directory.Exists(exportPath))
            Directory.Delete(exportPath, true);
        Directory.CreateDirectory(exportPath);

        EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
        EditorUserBuildSettings.buildAppBundle = false;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = exportPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        Debug.Log("[ExportAndroid] BuildPlayer 开始（IL2CPP，可能十几分钟）...");
        var report = BuildPipeline.BuildPlayer(options);
        var s = report.summary;
        Debug.Log("[ExportAndroid] result=" + s.result + " time=" + s.totalTime
                  + " errors=" + s.totalErrors + " size=" + (s.totalSize / (1024 * 1024)) + "MB");

        if (s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("[ExportAndroid] 构建失败");
            EditorApplication.Exit(1);
            return;
        }

        // ---------- 5. 检查产物 ----------
        string so = Path.Combine(exportPath, "unityLibrary/src/main/jniLibs/arm64-v8a/libil2cpp.so");
        string data = Path.Combine(exportPath, "unityLibrary/src/main/assets/bin/Data/data.unity3d");
        Debug.Log("[ExportAndroid] libil2cpp.so: " + File.Exists(so)
                  + (File.Exists(so) ? " " + new FileInfo(so).Length + " 字节" : ""));
        Debug.Log("[ExportAndroid] data.unity3d: " + File.Exists(data)
                  + (File.Exists(data) ? " " + new FileInfo(data).Length + " 字节" : ""));

        // 留个标记，方便外部判断是不是这次真的跑完了
        File.WriteAllText(Path.Combine(exportPath, "EXPORT_OK.txt"),
            "ok\n" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + TargetUrl + "\n");

        Debug.Log("[ExportAndroid] 全部完成");
        EditorApplication.Exit(0);
    }

    private static void FixSceneServerUrl()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        int changed = 0;

        foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null) continue;
            var field = mb.GetType().GetField("serverUrl");
            if (field == null || field.FieldType != typeof(string)) continue;

            string old = (string)field.GetValue(mb);
            if (old == TargetUrl) continue;

            Debug.Log("[ExportAndroid] " + mb.gameObject.name + ": " + old + " -> " + TargetUrl);
            field.SetValue(mb, TargetUrl);
            changed++;
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[ExportAndroid] 场景已保存，共修改 " + changed + " 处 serverUrl");
        }
        else
        {
            Debug.Log("[ExportAndroid] 场景内 serverUrl 已是目标值，无需修改");
        }
    }
}
#endif
