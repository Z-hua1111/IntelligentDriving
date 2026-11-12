#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

public class ChangeIPAndBuild
{
    // 修改这里的 IP 为你当前电脑的 IP
    private static string targetIP = "ws://10.33.237.159:8765";

    // Android 工程中 data.unity3d 的目标路径
    private static string androidDataPath = "d:/intelligent driving/SmartDrivingVisualization/SmartDrivingVisualization/unityLibrary/src/main/assets/bin/Data/data.unity3d";

    [MenuItem("Tools/ChangeIPAndBuild/1. 修改 IP")]
    public static void ChangeIP()
    {
        // Open the scene
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        // Find all WebSocketClient components in scene
        var clients = GameObject.FindObjectsOfType<MonoBehaviour>();
        bool found = false;
        foreach (var client in clients)
        {
            var field = client.GetType().GetField("serverUrl");
            if (field != null)
            {
                string oldUrl = (string)field.GetValue(client);
                Debug.Log($"[ChangeIP] Found WebSocketClient on {client.gameObject.name}, old URL: {oldUrl}");
                field.SetValue(client, targetIP);
                found = true;
                Debug.Log($"[ChangeIP] Changed to: {targetIP}");
            }
        }

        // Also check prefabs
        string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in prefabPaths)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                var components = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var field = comp.GetType().GetField("serverUrl");
                    if (field != null)
                    {
                        string oldUrl = (string)field.GetValue(comp);
                        Debug.Log($"[ChangeIP] Found in prefab {path}, old URL: {oldUrl}");
                        field.SetValue(comp, targetIP);
                        EditorUtility.SetDirty(prefab);
                        found = true;
                    }
                }
            }
        }

        if (found)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[ChangeIP] All changes saved!");
        }
        else
        {
            Debug.LogWarning("[ChangeIP] No WebSocketClient found.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[ChangeIP] Done.");
    }

    [MenuItem("Tools/ChangeIPAndBuild/2. 构建并替换 data.unity3d (Windows版)")]
    public static void BuildData()
    {
        string buildPath = "d:/intelligent driving/unity_build_temp";

        if (!Directory.Exists(buildPath))
            Directory.CreateDirectory(buildPath);

        // Build for Windows Standalone to generate data files
        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = new string[] { "Assets/Scenes/SampleScene.unity" };
        options.locationPathName = Path.Combine(buildPath, "build.exe");
        options.target = BuildTarget.StandaloneWindows64;
        options.options = BuildOptions.Development;

        Debug.Log("[BuildData] Starting Windows build...");
        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[BuildData] Build result: " + report.summary.result);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("[BuildData] Build FAILED!");
            return;
        }

        // Find data.unity3d
        string dataPath = Path.Combine(buildPath, "build_Data", "data.unity3d");
        string foundPath = null;

        if (File.Exists(dataPath))
        {
            foundPath = dataPath;
        }
        else
        {
            string[] files = Directory.GetFiles(buildPath, "data.unity3d", SearchOption.AllDirectories);
            if (files.Length > 0)
                foundPath = files[0];
        }

        if (foundPath == null)
        {
            Debug.LogError("[BuildData] data.unity3d not found in build output!");
            return;
        }

        // Copy to Android project
        string destPath = androidDataPath;

        if (!File.Exists(destPath))
        {
            Debug.LogError($"[BuildData] 目标路径不存在: {destPath}");
            Debug.LogError("[BuildData] 请检查 Android 工程路径是否正确");
            return;
        }

        // Backup original
        string backupPath = destPath + ".bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(destPath, backupPath);
            Debug.Log("[BuildData] 已备份原 data.unity3d");
        }

        // Copy new file
        File.Copy(foundPath, destPath, true);
        Debug.Log($"[BuildData] data.unity3d 替换成功! 来源: {foundPath}");
        Debug.Log($"[BuildData] 目标: {destPath}");
    }

    [MenuItem("Tools/ChangeIPAndBuild/3. 一键：修改IP + 构建 + 替换")]
    public static void ChangeIPAndBuildAll()
    {
        ChangeIP();
        BuildData();
        Debug.Log("[ChangeIPAndBuild] 全部完成！现在可以用 Android Studio 重新编译 App 了。");
    }
}
#endif
