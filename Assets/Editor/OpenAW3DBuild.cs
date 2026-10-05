using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class OpenAW3DBuild
{
    [MenuItem("Tools/OpenAW3D/Build Windows")]
    public static void Run()
    {
        OpenAW3DProjectSetup.Run();
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
            .Select(scene => scene.path).ToArray();
        var output = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Windows", "OpenAW3D.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("OpenAW3D build failed: " + report.summary.result);
    }
}
