using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Enters Play mode on the Main scene with the DemoDriver enabled; the driver saves screenshots and exits the Editor.
///   Unity -batchmode -projectPath UpperGIBleed -executeMethod Snapshot.Run -logFile -     (no -quit, no -nographics)
/// </summary>
public static class Snapshot
{
    public static void Run()
    {
        PlayerPrefs.SetInt("demo.snapshots", 1);
        PlayerPrefs.SetString("demo.dir", System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "shots")));
        PlayerPrefs.Save();
        EditorSceneManager.OpenScene(SceneBuilder.ScenePath);
        var go = new GameObject("DemoDriver"); go.AddComponent<DemoDriver>();
        EditorApplication.EnterPlaymode();
    }
}
