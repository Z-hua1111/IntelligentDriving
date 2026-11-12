using UnityEditor;
using UnityEngine;

public static class BuildAPK
{
    public static void Build()
    {
        string[] scenes = new string[] { "Assets/Scenes/SampleScene.unity" };
        
        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = scenes;
        options.locationPathName = "D:/IntelligentDriving/build.apk";
        options.target = BuildTarget.Android;
        options.options = (BuildOptions)1;

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[BuildAPK] Result: " + report.summary.result);
        
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("[BuildAPK] APK 生成成功: D:/IntelligentDriving/build.apk");
        }
        else
        {
            Debug.Log("[BuildAPK] 打包失败");
        }
    }
}
