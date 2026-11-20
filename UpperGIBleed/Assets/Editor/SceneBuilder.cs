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
        MakeShaderReferences();
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
        if (ti != null)
        {
            ti.maxTextureSize = 4096; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.anisoLevel = 8;
            foreach (var plat in new[] { "WebGL", "Standalone" })
            { var ps = ti.GetPlatformTextureSettings(plat); ps.overridden = true; ps.maxTextureSize = 4096; ps.format = TextureImporterFormat.Automatic; ps.textureCompression = TextureImporterCompression.CompressedHQ; ti.SetPlatformTextureSettings(ps); }
            ti.SaveAndReimport();
        }
        for (int i = 1; i <= 6; i++)
        {
            var ai = AssetImporter.GetAtPath($"Assets/Resources/Art/avatars/avatar_{i}.png") as TextureImporter;
            if (ai != null) { ai.alphaIsTransparency = true; ai.mipmapEnabled = false; ai.maxTextureSize = 256; ai.SaveAndReimport(); }
        }
    }

    /// <summary>
    /// The game builds all its materials at runtime with Shader.Find, so nothing in the project would otherwise reference the URP
    /// shaders and a player build could strip them (and their transparent / emission variants). These material assets keep them in.
    /// </summary>
    static void MakeShaderReferences()
    {
        Directory.CreateDirectory("Assets/Resources/ShaderRefs");
        void Ref(string name, string shader, System.Action<Material> setup = null)
        {
            string path = $"Assets/Resources/ShaderRefs/{name}.mat";
            var sh = Shader.Find(shader); if (sh == null) { Debug.LogError("shader not found: " + shader); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
            setup?.Invoke(m); EditorUtility.SetDirty(m);
        }
        Ref("LitOpaque", "Universal Render Pipeline/Lit");
        Ref("LitTransparent", "Universal Render Pipeline/Lit", m => { Tract.MakeTransparent(m); });
        Ref("LitEmissive", "Universal Render Pipeline/Lit", m => { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.red); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; });
        Ref("ParticlesUnlit", "Universal Render Pipeline/Particles/Unlit", m => { Tract.MakeTransparent(m); });
        AssetDatabase.SaveAssets();
        var always = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]).FindProperty("m_AlwaysIncludedShaders");
        foreach (var shName in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "UI/Default" })
        {
            var sh = Shader.Find(shName); if (sh == null) continue; bool present = false;
            for (int i = 0; i < always.arraySize; i++) if (always.GetArrayElementAtIndex(i).objectReferenceValue == sh) present = true;
            if (!present) { always.InsertArrayElementAtIndex(always.arraySize); always.GetArrayElementAtIndex(always.arraySize - 1).objectReferenceValue = sh; }
        }
        always.serializedObject.ApplyModifiedProperties();
        // keep every URP variant: the materials are made at runtime, so the build cannot know which ones it needs
        try
        {
            var strip = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            if (strip != null) { strip.stripUnusedVariants = false; strip.stripUnusedPostProcessingVariants = false; strip.stripScreenCoordOverrideVariants = false; Debug.Log("SceneBuilder: URP variant stripping off"); }
            else Debug.LogWarning("SceneBuilder: URPShaderStrippingSetting not found");
        }
        catch (System.Exception e) { Debug.LogWarning("SceneBuilder: could not change URP stripping: " + e.Message); }
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
