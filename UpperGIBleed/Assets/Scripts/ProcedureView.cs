using System.Collections.Generic;
using UnityEngine;

/// <summary>Everything you see inside the tract for one case: the scope light, the instrument, the lesion, varices, burns, bands and effects.</summary>
public class ProcedureView : MonoBehaviour
{
    Procedure p; Camera cam; Light scopeLight, cauteryLight;
    GameObject props, toolRoot, shaft, washGo, cauteryGo, capGo, cauteryTipGo;
    GameObject lesionRoot, ulcerGo, escharGo, wellGo, bandedGo; Material wellMat, cauteryTipMat, capMat;
    readonly List<GameObject> cords = new List<GameObject>(), burnGos = new List<GameObject>();
    ParticleSystem washPs, sparkPs;
    Material matCream, matBrown, matDark, matVarix, matNodule, matBand, matBurn, matWash, matProbe, matShaft, matCap;
    int burnsShown; float bandPulse;

    public static ProcedureView Create(Procedure p, Camera cam)
    {
        var go = new GameObject("ProcedureView"); var v = go.AddComponent<ProcedureView>(); v.p = p; v.cam = cam; v.Build(); return v;
    }

    /// <summary>The scope light sits close to the wall in a narrow lumen, so scale it by the local radius (inverse square) to keep the exposure even.</summary>
    public static float ScopeLightIntensity(float z) { float r = Anatomy.Radius(z) / 1.75f; return 3.0f * Mathf.Clamp(r * r, 0.1f, 1f); }

    static Material Lit(Color c, float smooth = 0.5f, float metal = 0, Color? emission = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        if (emission != null) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        return m;
    }
    static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

    GameObject Prop(string name, Material m)
    {
        var src = props.transform.Find(name);
        if (src == null) { Debug.LogWarning("props.fbx has no " + name); return new GameObject(name); }
        var go = Instantiate(src.gameObject); go.name = name; go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        return go;
    }

    void Build()
    {
        props = Resources.Load<GameObject>("Art/props");
        matCream = Lit(C(238, 214, 160), 0.35f); matBrown = Lit(C(78, 52, 40), 0.3f); matDark = Lit(C(38, 26, 22), 0.2f);
        matVarix = Lit(C(74, 80, 158), 0.55f); matNodule = Lit(C(60, 22, 58), 0.45f); matBand = Lit(C(196, 172, 120), 0.4f); matBurn = Lit(C(60, 36, 28), 0.3f);
        matWash = Lit(C(170, 195, 210), 0.6f, 0.4f); matProbe = Lit(C(36, 40, 44), 0.5f); matShaft = Lit(C(120, 125, 135), 0.5f, 0.3f);
        matCap = Lit(new Color(0.8f, 0.76f, 1f, 0.3f), 0.2f); Tract.MakeTransparent(matCap);
        cauteryTipMat = Lit(C(220, 222, 225), 0.3f, 0.8f, Color.black);
        wellMat = Lit(C(225, 22, 28), 0.7f, 0, new Color(1.2f, 0.05f, 0.05f));

        // scope light on the camera
        var lgo = new GameObject("ScopeLight"); lgo.transform.SetParent(cam.transform, false); lgo.transform.localPosition = new Vector3(0, 0.02f, 0.02f);
        scopeLight = lgo.AddComponent<Light>(); scopeLight.type = LightType.Point; scopeLight.range = 7f; scopeLight.intensity = 3.2f; scopeLight.color = new Color(1f, 0.96f, 0.9f); scopeLight.shadows = LightShadows.None;
        var cgo = new GameObject("CauteryLight"); cauteryLight = cgo.AddComponent<Light>(); cauteryLight.type = LightType.Point; cauteryLight.range = 0.9f; cauteryLight.color = new Color(1f, 0.6f, 0.2f); cauteryLight.intensity = 0; cauteryLight.shadows = LightShadows.None;

        // instrument
        toolRoot = new GameObject("Instrument");
        washGo = Prop("WashNozzle", matWash); washGo.transform.SetParent(toolRoot.transform, false);
        cauteryGo = Prop("CauteryProbe", matProbe); cauteryGo.transform.SetParent(toolRoot.transform, false);
        cauteryTipGo = Prop("CauteryTip", cauteryTipMat); cauteryTipGo.transform.SetParent(cauteryGo.transform, false);
        capGo = Prop("BandingCap", matCap); capGo.transform.SetParent(toolRoot.transform, false);
        shaft = Prop("ScopeShaft", matShaft);
        foreach (var g in new[] { washGo, cauteryGo, capGo }) g.transform.localScale = Vector3.one * 0.5f;
        washPs = MakeParticles(toolRoot.transform, new Color(0.85f, 0.95f, 1f, 0.8f), 0.028f, 3.2f, 0.22f, 14f, 220);
        sparkPs = MakeParticles(cauteryGo.transform, new Color(1f, 0.85f, 0.45f, 1f), 0.012f, 0.9f, 0.18f, 60f, 90);

        // lesion
        lesionRoot = new GameObject("Lesion");
        lesionRoot.transform.position = p.lesionPos; lesionRoot.transform.rotation = Quaternion.LookRotation(p.lesionNormal, Mathf.Abs(Vector3.Dot(p.lesionNormal, Vector3.forward)) > 0.9f ? Vector3.up : Vector3.forward);
        if (p.Case.kind == LesionKind.Ulcer)
        {
            ulcerGo = Prop("Ulcer", matCream); ulcerGo.transform.SetParent(lesionRoot.transform, false); ulcerGo.transform.localScale = Vector3.one * p.Case.lesionRadius;
            escharGo = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(escharGo.GetComponent<Collider>()); escharGo.transform.SetParent(lesionRoot.transform, false);
            escharGo.transform.localScale = new Vector3(p.Case.lesionRadius * 0.9f, p.Case.lesionRadius * 0.9f, p.Case.lesionRadius * 0.12f); escharGo.GetComponent<Renderer>().sharedMaterial = matBrown; escharGo.SetActive(false);
            if (p.Case.style == "gastric")                   // folds converging on a gastric ulcer
                for (int i = 0; i < 8; i++)
                {
                    var f = GameObject.CreatePrimitive(PrimitiveType.Capsule); Destroy(f.GetComponent<Collider>()); f.transform.SetParent(lesionRoot.transform, false);
                    float an = i / 8f * Mathf.PI * 2 + 0.3f, r0 = p.Case.lesionRadius * 1.9f, len = p.Case.lesionRadius * 1.4f;
                    f.transform.localPosition = new Vector3(Mathf.Cos(an) * (r0 + len / 2), Mathf.Sin(an) * (r0 + len / 2), 0.02f);
                    f.transform.localRotation = Quaternion.Euler(0, 0, an * Mathf.Rad2Deg - 90); f.transform.localScale = new Vector3(0.05f, len / 2, 0.035f);
                    f.GetComponent<Renderer>().sharedMaterial = Lit(C(236, 150, 135), 0.6f);
                }
        }
        else
        {
            var V = p.Case.varices;
            for (int i = 0; i < V.cordDeg.Length; i++)
            {
                float th = i == V.bleed ? p.lesionTh : Anatomy.ClockToRad(V.cordDeg[i]);
                float wm = (V.w0 + V.w1) / 2, len = V.w1 - V.w0;
                p.tract.WallPoint(th, wm, out var pos, out var n);
                var cord = Prop("Varix", matVarix); cord.transform.position = pos + n * 0.01f;
                cord.transform.rotation = Quaternion.LookRotation(n, Vector3.forward);     // +Z into the lumen, +Y along the tube
                cord.transform.localScale = new Vector3(0.05f, len / 6f, 0.05f);
                cords.Add(cord);
            }
        }
        wellGo = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(wellGo.GetComponent<Collider>()); wellGo.transform.SetParent(lesionRoot.transform, false);
        wellGo.transform.localPosition = new Vector3(0, 0, 0.01f); wellGo.GetComponent<Renderer>().sharedMaterial = wellMat;
        bandedGo = new GameObject("Banded"); bandedGo.transform.SetParent(lesionRoot.transform, false); bandedGo.SetActive(false);
        var nod = Prop("BandNodule", matNodule); nod.transform.SetParent(bandedGo.transform, false); var bnd = Prop("Band", matBand); bnd.transform.SetParent(bandedGo.transform, false);
        bandedGo.transform.localScale = Vector3.one * 0.13f;

        p.OnControlled += OnControlled; p.OnToolChanged += RefreshTool; p.OnBurn += RefreshBurns; p.OnBand += () => bandPulse = 0.5f;
        RefreshTool();
    }

    static ParticleSystem MakeParticles(Transform parent, Color c, float size, float speed, float life, float cone, float rate)
    {
        var go = new GameObject("Particles"); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop();
        var m = ps.main; m.startSize = size; m.startSpeed = speed; m.startLifetime = life; m.startColor = c; m.maxParticles = 600; m.simulationSpace = ParticleSystemSimulationSpace.World; m.playOnAwake = false; m.gravityModifier = 0.3f;
        var e = ps.emission; e.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = cone; sh.radius = 0.01f;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); r.material.SetColor("_BaseColor", c);
        Tract.MakeTransparent(r.material);
        return ps;
    }

    void RefreshTool()
    {
        washGo.SetActive(p.tool == Tool.Wash); cauteryGo.SetActive(p.tool == Tool.Cautery); capGo.SetActive(p.tool == Tool.Banding);
    }

    void OnControlled()
    {
        wellGo.SetActive(false);
        if (p.Case.kind == LesionKind.Ulcer) { ulcerGo.GetComponent<Renderer>().sharedMaterial = Lit(C(214, 190, 150), 0.3f); escharGo.SetActive(true); }
        else bandedGo.SetActive(true);
        GameAudio.I?.Good();
    }

    void RefreshBurns()
    {
        for (int i = burnGos.Count; i < p.burns.Count; i++)
        {
            var b = p.burns[i]; GameObject go;
            if (b.kind == "band") { go = new GameObject("WastedBand"); var n = Prop("BandNodule", matNodule); n.transform.SetParent(go.transform, false); var bd = Prop("Band", matBand); bd.transform.SetParent(go.transform, false); go.transform.localScale = Vector3.one * 0.1f; }
            else { go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(go.GetComponent<Collider>()); go.transform.localScale = new Vector3(0.09f, 0.09f, 0.03f); go.GetComponent<Renderer>().sharedMaterial = matBurn; }
            go.transform.position = b.pos + b.normal * 0.005f; go.transform.rotation = Quaternion.LookRotation(b.normal, Vector3.forward);
            burnGos.Add(go);
        }
    }

    void LateUpdate()
    {
        if (p == null || p.state == Procedure.State.Ended && toolRoot == null) return;
        // instrument: tip just off the wall (or 1.5 units out if the wall is far), shaft from the side of the view at the (lagging) probe angle
        var ray = cam.ViewportPointToRay(p.TipViewport(p.tipAng));
        float d = p.tipOnWall ? Mathf.Min(p.tipDist, 2.5f) - 0.05f : 1.5f;
        var tip = ray.origin + ray.direction * Mathf.Max(0.12f, d);
        toolRoot.transform.position = tip; toolRoot.transform.rotation = Quaternion.LookRotation(ray.direction, cam.transform.up);
        var anchorVp = p.TipViewport(p.probeAng); anchorVp = (anchorVp - new Vector2(0.5f, 0.5f)) * 1.35f + new Vector2(0.5f, 0.5f);
        var anchor = cam.ViewportToWorldPoint(new Vector3(anchorVp.x, anchorVp.y, 0.08f));
        var toTip = tip - anchor; shaft.transform.position = tip; shaft.transform.rotation = Quaternion.LookRotation(toTip.normalized, cam.transform.up);
        shaft.transform.localScale = new Vector3(0.5f, 0.5f, toTip.magnitude / 2.35f);

        bool live = p.state == Procedure.State.Play;
        if (p.washing && live) { if (!washPs.isPlaying) washPs.Play(); } else if (washPs.isPlaying) washPs.Stop();
        GameAudio.I?.Wash(p.washing && live); GameAudio.I?.Cautery(p.cauteryOn && live);
        bool fire = p.cauteryOn && live;
        cauteryLight.transform.position = tip; cauteryLight.intensity = fire ? 1.2f + 0.5f * Mathf.Sin(Time.time * 40) : 0;
        cauteryTipMat.SetColor("_EmissionColor", fire ? new Color(1.4f, 0.7f, 0.2f) : Color.black);
        if (fire) { if (!sparkPs.isPlaying) sparkPs.Play(); } else if (sparkPs.isPlaying) sparkPs.Stop();
        // banding cap pulse on fire, welling point pulse while bleeding, light flicker
        bandPulse = Mathf.Max(0, bandPulse - Time.deltaTime);
        capGo.transform.localScale = Vector3.one * (0.5f * (1 - 0.3f * Mathf.Sin(bandPulse / 0.5f * Mathf.PI)));
        if (wellGo.activeSelf)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (p.Case.kind == LesionKind.Varix ? 10 : 7.2f));
            float r = p.Case.lesionRadius * (0.22f + 0.12f * pulse);
            wellGo.transform.localScale = new Vector3(r * 2, r * 2, r * 1.2f);
            wellMat.SetColor("_EmissionColor", new Color(0.9f + 0.9f * pulse, 0.05f, 0.05f));
        }
        scopeLight.intensity = ScopeLightIntensity(p.z) * (1 + 0.025f * Mathf.Sin(Time.time * 17));
        if (Time.frameCount % 2 == 0) p.blood.UploadTexture();
    }

    void OnDestroy()
    {
        foreach (var g in cords) if (g) Destroy(g); foreach (var g in burnGos) if (g) Destroy(g);
        if (lesionRoot) Destroy(lesionRoot); if (toolRoot) Destroy(toolRoot); if (shaft) Destroy(shaft); if (cauteryLight) Destroy(cauteryLight.gameObject); if (scopeLight) Destroy(scopeLight.gameObject);
        GameAudio.I?.StopLoops();
    }
}
