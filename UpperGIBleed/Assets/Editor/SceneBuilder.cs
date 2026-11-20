using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the Main scene and fixes import settings. Run headless:
///   Unity -batchmode -quit -projectPath UpperGIBleed -executeMethod SceneBuilder.Build -logFile -
/// </summary>
public static class SceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Upper GI Bleed/Build scene")]
    public static void Build()
    {
        FixImports();
        ImportTmpEssentials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>(); cam.fieldOfView = 100; cam.nearClipPlane = 0.02f;
        var ucd = camGo.AddComponent<UniversalAdditionalCameraData>(); ucd.renderPostProcessing = true;
        // global volume: tonemapping, a little bloom for the wet highlights, vignette
        var volGo = new GameObject("Global Volume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
        var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.55f); bloom.threshold.Override(0.95f);
        var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.45f);
        var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.15f); ca.saturation.Override(6f);
        Directory.CreateDirectory("Assets/Settings");
        AssetDatabase.CreateAsset(profile, "Assets/Settings/MainVolumeProfile.asset"); vol.sharedProfile = profile;
        new GameObject("Bootstrap").AddComponent<Bootstrap>();
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        // reserve the Wall layer
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers"); layers.GetArrayElementAtIndex(Tract.Layer).stringValue = "Wall"; tagManager.ApplyModifiedProperties();
        PlayerSettings.productName = "Upper GI Bleed"; PlayerSettings.companyName = "Brendan JvR";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultWebScreenWidth = 1280; PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        Debug.Log("SceneBuilder: scene built at " + ScenePath);
    }

    static void FixImports()
    {
        foreach (var path in new[] { "Assets/Resources/Art/tract.fbx", "Assets/Resources/Art/props.fbx" })
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter; if (mi == null) { Debug.LogWarning("missing " + path); continue; }
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.isReadable = true; mi.importAnimation = false; mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import; mi.useFileScale = true; mi.globalScale = 1f;
            mi.SaveAndReimport();
        }
        var ti = AssetImporter.GetAtPath("Assets/Resources/Art/tract_albedo.png") as TextureImporter;
        if (ti != null) { ti.maxTextureSize = 4096; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.SaveAndReimport(); }
        for (int i = 1; i <= 6; i++)
        {
            var ai = AssetImporter.GetAtPath($"Assets/Resources/Art/avatars/avatar_{i}.png") as TextureImporter;
            if (ai != null) { ai.alphaIsTransparency = true; ai.mipmapEnabled = false; ai.maxTextureSize = 256; ai.SaveAndReimport(); }
        }
    }

    static void ImportTmpEssentials()
    {
        if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return;
        try
        {
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
            Debug.Log("SceneBuilder: TMP essentials imported");
        }
        catch (System.Exception e) { Debug.LogWarning("TMP essentials import failed: " + e.Message + " (legacy Text will be used)"); }
    }
}
