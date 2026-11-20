using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>A small toolkit for building uGUI screens from code. TextMeshPro when its resources are present, legacy Text otherwise.</summary>
public static class UIKit
{
    public static readonly Color Bg = Hex("0b0f13"), Panel = Hex("131a20"), Panel2 = Hex("1a232b"), Line = Hex("26323c"), Fg = Hex("e8eef2"), Muted = Hex("8c9aa5");
    public static readonly Color Wash = Hex("4fc3f7"), Cautery = Hex("ffb347"), Banding = Hex("b69cff"), Bad = Hex("ff5d57"), Ok = Hex("5fd38d"), Warn = Hex("ffc857"), Coffee = Hex("c8955a");
    public static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
    public static Color A(Color c, float a) { c.a = a; return c; }

    static bool? tmpOk;
    public static bool TmpAvailable { get { if (tmpOk == null) { try { tmpOk = TMP_Settings.defaultFontAsset != null; } catch { tmpOk = false; } } return tmpOk.Value; } }
    static Font legacyFont;
    static Font LegacyFont => legacyFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    static Sprite roundSprite;
    public static Sprite Round { get { if (roundSprite == null) roundSprite = MakeRound(); return roundSprite; } }

    static Sprite MakeRound()
    {
        int s = 32, r = 9; var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, r, s - r), cy = Mathf.Clamp(y + 0.5f, r, s - r);
            float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
        }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 1, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
    }

    public static Canvas CreateCanvas(string name, int order = 0)
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
        var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1280, 720); sc.matchWidthOrHeight = 0.5f;
        return c;
    }

    public static RectTransform Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax; return rt;
    }
    public static RectTransform Fill(GameObject go, float pad = 0) => Rect(go, Vector2.zero, Vector2.one, new Vector2(pad, pad), new Vector2(-pad, -pad));
    public static RectTransform Place(GameObject go, Vector2 anchor, Vector2 size, Vector2 offset = default)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = offset; return rt;
    }

    public static Image PanelEl(Transform parent, string name, Color color, bool rounded = true)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var im = go.GetComponent<Image>(); im.color = color; if (rounded) { im.sprite = Round; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 1; }
        Fill(go); return im;
    }

    public static Image Outline(Image panel, Color color, float width = 1.5f)
    {
        var o = panel.gameObject.GetComponent<Outline>() ?? panel.gameObject.AddComponent<Outline>();
        o.effectColor = color; o.effectDistance = new Vector2(width, -width); o.useGraphicAlpha = false; return panel;
    }

    /// <summary>A text element that is TMP or legacy Text underneath.</summary>
    public class Txt
    {
        public GameObject go; public TMP_Text tmp; public Text legacy;
        public string text { get => tmp ? tmp.text : legacy.text; set { if (tmp) tmp.text = value; else legacy.text = value; } }
        public Color color { set { if (tmp) tmp.color = value; else legacy.color = value; } }
        public float size { set { if (tmp) tmp.fontSize = value; else legacy.fontSize = Mathf.RoundToInt(value); } }
        public RectTransform rt => go.GetComponent<RectTransform>();
        public void Align(TextAlignmentOptions a)
        {
            if (tmp) tmp.alignment = a;
            else legacy.alignment = a == TextAlignmentOptions.Center ? TextAnchor.MiddleCenter : a == TextAlignmentOptions.Left ? TextAnchor.MiddleLeft : a == TextAlignmentOptions.Right ? TextAnchor.MiddleRight : a == TextAlignmentOptions.TopLeft ? TextAnchor.UpperLeft : a == TextAlignmentOptions.Top ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
        }
        public void Bold(bool b) { if (tmp) tmp.fontStyle = b ? FontStyles.Bold : FontStyles.Normal; else legacy.fontStyle = b ? FontStyle.Bold : FontStyle.Normal; }
        public void Wrap(bool w) { if (tmp) tmp.enableWordWrapping = w; else legacy.horizontalOverflow = w ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow; }
        public void Raycast(bool r) { if (tmp) tmp.raycastTarget = r; else legacy.raycastTarget = r; }
    }

    public static Txt Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
    {
        var go = new GameObject("Label", typeof(RectTransform)); go.transform.SetParent(parent, false);
        var t = new Txt { go = go };
        if (TmpAvailable) { t.tmp = go.AddComponent<TextMeshProUGUI>(); t.tmp.richText = true; }
        else { t.legacy = go.AddComponent<Text>(); t.legacy.font = LegacyFont; t.legacy.supportRichText = true; t.legacy.verticalOverflow = VerticalOverflowMode.Overflow; }
        t.text = text; t.size = size; t.color = color; t.Align(align); t.Bold(bold); t.Wrap(true); t.Raycast(false);
        Fill(go); return t;
    }

    public static Button Button(Transform parent, string text, Action onClick, Color bg, Color fg, float fontSize = 16, bool bold = true)
    {
        var im = PanelEl(parent, "Button " + text, bg);
        var b = im.gameObject.AddComponent<Button>(); b.targetGraphic = im;
        var cb = b.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); cb.selectedColor = Color.white; b.colors = cb;
        var l = Label(im.transform, text, fontSize, fg, TextAlignmentOptions.Center, bold);
        Fill(l.go, 6);
        b.onClick.AddListener(() => { GameAudio.I?.Click(); onClick?.Invoke(); });
        return b;
    }

    public static Image ImageEl(Transform parent, Sprite s, Color? tint = null)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var im = go.GetComponent<Image>(); im.sprite = s; im.preserveAspect = true; im.color = tint ?? Color.white; im.raycastTarget = false; Fill(go); return im;
    }

    public static VerticalLayoutGroup VStack(Transform parent, float spacing, RectOffset pad = null, TextAnchor align = TextAnchor.UpperCenter)
    {
        var go = new GameObject("VStack", typeof(RectTransform)); go.transform.SetParent(parent, false); Fill(go);
        var v = go.AddComponent<VerticalLayoutGroup>(); v.spacing = spacing; v.padding = pad ?? new RectOffset(); v.childAlignment = align;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false; return v;
    }
    public static HorizontalLayoutGroup HStack(Transform parent, float spacing, TextAnchor align = TextAnchor.MiddleCenter, bool expandW = false)
    {
        var go = new GameObject("HStack", typeof(RectTransform)); go.transform.SetParent(parent, false); Fill(go);
        var h = go.AddComponent<HorizontalLayoutGroup>(); h.spacing = spacing; h.childAlignment = align;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = expandW; h.childForceExpandHeight = false; return h;
    }
    public static LayoutElement Size(GameObject go, float w = -1, float h = -1, float flexW = -1)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (w >= 0) { le.preferredWidth = w; le.minWidth = w; } if (h >= 0) { le.preferredHeight = h; le.minHeight = h; } if (flexW >= 0) le.flexibleWidth = flexW; return le;
    }
    public static GameObject Spacer(Transform parent, float h) { var go = new GameObject("Spacer", typeof(RectTransform)); go.transform.SetParent(parent, false); Size(go, -1, h); return go; }

    public static Slider SliderEl(Transform parent, float value, Action<float> onChange)
    {
        var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider)); root.transform.SetParent(parent, false);
        var bg = PanelEl(root.transform, "Bg", Line); Rect(bg.gameObject, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -4), new Vector2(0, 4));
        var fillArea = new GameObject("FillArea", typeof(RectTransform)); fillArea.transform.SetParent(root.transform, false); Rect(fillArea, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -4), new Vector2(0, 4));
        var fill = PanelEl(fillArea.transform, "Fill", Wash);
        var handleArea = new GameObject("HandleArea", typeof(RectTransform)); handleArea.transform.SetParent(root.transform, false); Fill(handleArea);
        var handle = PanelEl(handleArea.transform, "Handle", Fg); Place(handle.gameObject, new Vector2(0, 0.5f), new Vector2(18, 18));
        var s = root.GetComponent<Slider>(); s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle; s.minValue = 0; s.maxValue = 1; s.value = value;
        s.onValueChanged.AddListener(v => onChange(v));
        return s;
    }

    /// <summary>A single-line text field (TMP_InputField when TMP is present, otherwise InputField).</summary>
    public static Selectable TextField(Transform parent, string value, string placeholder, int maxLen, Action<string> onChange, Action onSubmit = null)
    {
        var box = PanelEl(parent, "TextField", Panel2); Outline(box, Line);
        if (TmpAvailable)
        {
            var f = box.gameObject.AddComponent<TMP_InputField>();
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); vp.transform.SetParent(box.transform, false); Fill(vp, 10);
            var txt = Label(vp.transform, "", 20, Fg); txt.Wrap(false); Fill(txt.go);
            var ph = Label(vp.transform, placeholder, 20, A(Muted, 0.7f)); ph.Wrap(false); Fill(ph.go);
            f.textViewport = vp.GetComponent<RectTransform>(); f.textComponent = txt.tmp; f.placeholder = ph.tmp; f.characterLimit = maxLen; f.text = value;
            f.onValueChanged.AddListener(v => onChange(v)); f.onSubmit.AddListener(_ => onSubmit?.Invoke()); f.targetGraphic = box;
            return f;
        }
        else
        {
            var f = box.gameObject.AddComponent<InputField>();
            var txt = Label(box.transform, "", 20, Fg); txt.Wrap(false); Fill(txt.go, 10);
            var ph = Label(box.transform, placeholder, 20, A(Muted, 0.7f)); ph.Wrap(false); Fill(ph.go, 10);
            f.textComponent = txt.legacy; f.placeholder = ph.legacy; f.characterLimit = maxLen; f.text = value;
            f.onValueChanged.AddListener(v => onChange(v)); f.onEndEdit.AddListener(_ => onSubmit?.Invoke()); f.targetGraphic = box;
            return f;
        }
    }

    public static void Hold(GameObject go, Action down, Action up)
    {
        var et = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var d = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown }; d.callback.AddListener(_ => down()); et.triggers.Add(d);
        var u = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp }; u.callback.AddListener(_ => up()); et.triggers.Add(u);
        var x = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; x.callback.AddListener(_ => up()); et.triggers.Add(x);
    }
}
