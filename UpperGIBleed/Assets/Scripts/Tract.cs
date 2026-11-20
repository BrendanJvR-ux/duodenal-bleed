using UnityEngine;

/// <summary>
/// The tract mesh from Blender (Resources/Art/tract.fbx) with its baked mucosa texture, a MeshCollider for the
/// instrument raycasts, and a transparent overlay that shows the blood simulation texture.
/// </summary>
public class Tract : MonoBehaviour
{
    public const int Layer = 8;                 // "Wall" layer: reserved by the scene builder
    public MeshCollider Collider { get; private set; }
    public Mesh Mesh { get; private set; }
    Material wallMat, overlayMat;
    public Vector3 EndCapNormal { get; private set; }

    public static Tract Create()
    {
        var prefab = Resources.Load<GameObject>("Art/tract");
        if (prefab == null) { Debug.LogError("Art/tract.fbx missing from Resources"); return null; }
        var root = new GameObject("Tract");
        var t = root.AddComponent<Tract>();
        var inst = Instantiate(prefab, root.transform);
        inst.name = "TractMesh";
        var mf = inst.GetComponentInChildren<MeshFilter>();
        var mr = mf.GetComponent<MeshRenderer>();
        t.Setup(inst, mf, mr);
        return t;
    }

    void Setup(GameObject inst, MeshFilter mf, MeshRenderer mr)
    {
        // The Blender script builds the tube along its +Y, which the FBX exporter maps to Unity +Z. Verify, and fix if not.
        var b = mf.sharedMesh.bounds;
        int longest = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
        var mt = mf.transform;
        mt.localPosition = Vector3.zero; mt.localRotation = Quaternion.identity; mt.localScale = Vector3.one;
        inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
        if (longest == 1) mt.localRotation = Quaternion.Euler(90, 0, 0);          // +Y -> +Z
        else if (longest == 0) mt.localRotation = Quaternion.Euler(0, -90, 0);    // +X -> +Z
        float len = b.size[longest];
        float scale = Anatomy.End / len;
        mt.localScale = Vector3.one * scale;
        // put the start of the tube at z = 0
        var wb = mr.bounds;
        mt.localPosition += new Vector3(0, 0, -wb.min.z);
        wb = mr.bounds;
        Debug.Log($"Tract: mesh axis {longest}, scale x{scale:F3}, world bounds {wb.min} .. {wb.max}, {mf.sharedMesh.vertexCount} verts");

        // make a readable copy of the mesh so we can check winding and build the overlay
        Mesh = Instantiate(mf.sharedMesh); Mesh.name = "TractRuntime";
        mf.sharedMesh = Mesh;
        mf.gameObject.layer = Layer;
        Collider = mf.gameObject.AddComponent<MeshCollider>();
        Collider.sharedMesh = Mesh;
        // the camera is inside: faces must point inward. Probe from the axis outward; if nothing is hit, flip.
        var origin = mt.TransformPoint(Vector3.zero); origin = Anatomy.Axis(3f);
        if (!Physics.Raycast(origin, Vector3.right, out _, 5f, 1 << Layer))
        {
            Debug.Log("Tract: flipping winding (faces pointed outward)");
            var tri = Mesh.triangles; for (int i = 0; i < tri.Length; i += 3) { int x = tri[i]; tri[i] = tri[i + 2]; tri[i + 2] = x; }
            Mesh.triangles = tri;
            var n = Mesh.normals; for (int i = 0; i < n.Length; i++) n[i] = -n[i]; Mesh.normals = n;
            Collider.sharedMesh = null; Collider.sharedMesh = Mesh;
        }
        else
        {
            // normals should point inward too (towards the axis)
            var n = Mesh.normals; var v = Mesh.vertices; int bad = 0;
            for (int i = 0; i < n.Length; i += 97) { var wp = mt.TransformPoint(v[i]); var toAxis = (Anatomy.Axis(wp.z) - wp); if (Vector3.Dot(mt.TransformDirection(n[i]), toAxis) < 0) bad++; }
            if (bad > n.Length / 97 / 2) { Debug.Log("Tract: flipping normals"); for (int i = 0; i < n.Length; i++) n[i] = -n[i]; Mesh.normals = n; }
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        wallMat = new Material(shader) { name = "Mucosa" };
        var albedo = Resources.Load<Texture2D>("Art/tract_albedo");
        wallMat.SetTexture("_BaseMap", albedo);
        wallMat.SetColor("_BaseColor", new Color(0.98f, 0.95f, 0.93f));
        wallMat.SetFloat("_Smoothness", 0.74f);
        wallMat.SetFloat("_Metallic", 0f);
        wallMat.SetFloat("_SpecularHighlights", 1f);
        mr.sharedMaterial = wallMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // blood overlay: the same mesh pushed slightly inward along the (inward) normals, transparent
        var ov = new GameObject("BloodOverlay");
        ov.transform.SetParent(mt, false);
        var om = Instantiate(Mesh); om.name = "BloodOverlayMesh";
        var ov_v = om.vertices; var ov_n = om.normals;
        for (int i = 0; i < ov_v.Length; i++) ov_v[i] += ov_n[i] * (0.012f / mt.localScale.x);
        om.vertices = ov_v; om.RecalculateBounds();
        ov.AddComponent<MeshFilter>().sharedMesh = om;
        var ovr = ov.AddComponent<MeshRenderer>();
        overlayMat = new Material(shader) { name = "Blood" };
        MakeTransparent(overlayMat);
        overlayMat.SetColor("_BaseColor", Color.white);
        overlayMat.SetFloat("_Smoothness", 0.9f);
        var clear = new Texture2D(1, 1, TextureFormat.RGBA32, false); clear.SetPixel(0, 0, Color.clear); clear.Apply();
        overlayMat.SetTexture("_BaseMap", clear);                      // nothing to show until a case sets the blood texture
        ovr.sharedMaterial = overlayMat;
        ovr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ovr.receiveShadows = false;
    }

    public static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    public void SetBloodTexture(Texture2D tex) { overlayMat.SetTexture("_BaseMap", tex); }

    /// <summary>Where a ray from origin along dir meets the wall.</summary>
    public bool Raycast(Vector3 origin, Vector3 dir, out RaycastHit hit, float max = 12f) => Physics.Raycast(origin, dir, out hit, max, 1 << Layer);

    /// <summary>The wall point at angle th round the lumen at depth w (raycast from the axis), with its inward normal.</summary>
    public bool WallPoint(float th, float w, out Vector3 point, out Vector3 normal)
    {
        var o = Anatomy.Axis(w); var d = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0);
        if (Raycast(o, d, out var hit, 6f)) { point = hit.point; normal = hit.normal; return true; }
        point = o + d * Anatomy.Radius(w); normal = -d; return false;
    }
}
