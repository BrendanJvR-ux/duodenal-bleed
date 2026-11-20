using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// WebGL build for GitHub Pages (Brotli is not served with the right headers by Pages, so compression is off):
///   Unity -batchmode -quit -projectPath UpperGIBleed -executeMethod Builder.WebGL -logFile -
/// Output: ../play (served at /play/ on the Pages site).
/// </summary>
public static class Builder
{
    [MenuItem("Upper GI Bleed/Build WebGL")]
    public static void WebGL()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.template = "APPLICATION:Minimal";
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "play"));
        var opts = new BuildPlayerOptions { scenes = new[] { SceneBuilder.ScenePath }, locationPathName = outDir, target = BuildTarget.WebGL, options = BuildOptions.None };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"Builder: WebGL {report.summary.result}, {report.summary.totalSize / 1048576f:F1} MB, {report.summary.totalErrors} errors -> {outDir}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
