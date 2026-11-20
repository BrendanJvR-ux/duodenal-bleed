using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>The one object in the scene. Builds the world and the game at start.</summary>
public class Bootstrap : MonoBehaviour
{
    public static Tract tract;
    void Start()
    {
        Application.targetFrameRate = 60;
        var cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.fieldOfView = 100; cam.nearClipPlane = 0.02f; cam.farClipPlane = 40; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.005f, 0.005f);
        var ucd = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        ucd.renderPostProcessing = true; ucd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.11f; RenderSettings.fogColor = new Color(0.10f, 0.02f, 0.02f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.10f, 0.055f, 0.05f);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) l.gameObject.SetActive(false);
        if (GameAudio.I == null) new GameObject("Audio").AddComponent<GameAudio>();
        tract = Tract.Create();
        GameFlow.Create(tract, cam);
    }
}
