using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Repeatable browser integration build, without a dependency on editor automation packages.</summary>
public static class WebGLSmokeBuild
{
    public static void Build()
    {
        string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string output = Path.Combine(repository, ".tmp/webgl-review/player");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/WebGLSmoke/Smoke.unity" },
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.Development,
        });
        var summary = report.summary;
        File.WriteAllText(Path.Combine(repository, ".tmp/webgl-review/build-result.txt"),
            $"Result: {summary.result}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\nBytes: {summary.totalSize}\n");
        if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0)
            throw new InvalidOperationException("The Web smoke build failed. Read the Editor log for diagnostics.");
    }
}
