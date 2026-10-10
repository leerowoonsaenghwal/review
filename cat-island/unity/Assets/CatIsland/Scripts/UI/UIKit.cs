using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CatIsland.UI
{
    /// <summary>브랜드 색·글꼴·크기 (docs/BRAND.md, assets/brand/brand_tokens.json). 검정·순수 흰색 없음.</summary>
    public static class Theme
    {
        public static readonly Color Cream = Hex("FBF6E6"), MilkTea = Hex("F6F1E3"), Cocoa = Hex("6F5A40"), CocoaDeep = Hex("5A4632"), Latte = Hex("A08A6A");
        public static readonly Color Grass = Hex("8FCA5E"), Forest = Hex("58A043"), ForestText = Hex("3F7A2E"), Butter = Hex("F9CF7A"), Peach = Hex("F2A7A0");
        public static readonly Color Strawberry = Hex("FF7F9F"), StrawberryLight = Hex("FF9AB3"), StrawberryText = Hex("B83D61"), Sea = Hex("86CFE6"), Sky = Hex("BFE6F6"), Sand = Hex("F7E2AD");
        public static readonly Color StarNight = Hex("2B3266"), StarViolet = Hex("6B5B9A"), StarCloud = Hex("D9D2F0");
        public static readonly Color Shade = new Color(0.27f, 0.2f, 0.13f, 0.32f);      // (모달 뒤를 살짝 어둡게: 검정 대신 코코아)
        public const float MinTouch = 44f, CardRadius = 16f;
        public const int Large = 30, Title = 23, Body = 18, Caption = 15, Tiny = 12;   // pt (기준 화면 390 pt 폭)

        static Font jua;
        public static Font Font => jua ? jua : (jua = Resources.Load<Font>("Fonts/Jua"));
        public static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        // ---- 둥근 사각형 스프라이트 (9-slice): 크기별로 한 번 그린다
        static readonly Dictionary<(int, int), Sprite> rounded = new Dictionary<(int, int), Sprite>();
        /// <summary>모서리 반지름 r(px), 테두리 두께 border(px, 0 이면 채움만). 흰색으로 그리고 Image.color 로 칠한다.</summary>
        public static Sprite Rounded(int r, int border = 0)
        {
            if (rounded.TryGetValue((r, border), out var s) && s) return s;
            int n = r * 2 + 4; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = $"round{r}_{border}" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + .5f, r, n - r), cy = Mathf.Clamp(y + .5f, r, n - r);
                    float d = Mathf.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy)) - (r - 1);
                    float a = Mathf.Clamp01(.5f - d);
                    if (border > 0) a *= Mathf.Clamp01(.5f + d + border);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
            rounded[(r, border)] = s; return s;
        }
    }

    /// <summary>UI 를 코드로 짓는 도구. 모든 버튼은 44 pt 이상, 둥근 카드, 누르면 살짝 눌렸다 튀어나온다.</summary>
    public static class Kit
    {
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false); return rt;
        }
        public static RectTransform Fill(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t); return rt; }
        public static RectTransform Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        { rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size; return rt; }

        public static Image Box(Transform parent, string name, Color color, int radius = 16, int border = 0)
        {
            var rt = Rect(parent, name); var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Theme.Rounded(Mathf.Max(2, radius * 2), border * 2); img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 2f; img.color = color;
            return img;
        }
        /// <summary>카드: 크림 바탕 + 아주 연한 그림자.</summary>
        public static Image Card(Transform parent, string name, Color? color = null)
        {
            var shadow = Box(parent, name + "_shadow", new Color(Theme.Cocoa.r, Theme.Cocoa.g, Theme.Cocoa.b, .12f), 18);
            var card = Box(shadow.transform, name, color ?? Theme.Cream, 16);
            Fill(card.rectTransform, 0, 0, 0, 3);
            return card;
        }
        public static Text Label(Transform parent, string text, int size = Theme.Body, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter, string name = "Label")
        {
            var rt = Rect(parent, name); var t = rt.gameObject.AddComponent<Text>();
            t.font = Theme.Font; t.fontSize = size; t.color = color ?? Theme.Cocoa; t.alignment = align; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = Mathf.Max(10, size - 6); t.resizeTextMaxSize = size;   // (긴 문구도 잘리지 않게 조금 줄인다)
            return t;
        }
        public static Image IconImage(Transform parent, UIIcon icon, float size, string name = "Icon")
        {
            var rt = Rect(parent, name); var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UIIcons.Sprite(icon); img.preserveAspect = true; img.raycastTarget = false;
            rt.sizeDelta = new Vector2(size, size); return img;
        }

        public enum Style { Primary, Secondary, Plain }
        /// <summary>버튼. Primary: 딸기우유 바탕 + 진한 코코아 글자, Secondary: 크림 + 코코아 테두리.</summary>
        public static Button Btn(Transform parent, string label, Action onClick, Style style = Style.Primary, UIIcon? icon = null, string name = null)
        {
            var bg = style == Style.Plain ? Box(parent, name ?? "Btn_" + label, new Color(1, 1, 1, 0), 22) : Box(parent, name ?? "Btn_" + label, style == Style.Primary ? Theme.StrawberryLight : Theme.Cream, 22);
            if (style == Style.Secondary) { var line = Box(bg.transform, "Line", Theme.Cocoa, 22, 2); Fill(line.rectTransform); line.raycastTarget = false; }
            var b = bg.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => onClick?.Invoke());
            bg.gameObject.AddComponent<Press>();
            var row = Rect(bg.transform, "Row"); Fill(row, 12, 4, 12, 4);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.childAlignment = TextAnchor.MiddleCenter; h.spacing = 6; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (icon.HasValue) { var ic = IconImage(row, icon.Value, 28); var le = ic.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = le.preferredHeight = 28; }
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label(row, label, Theme.Body, style == Style.Primary ? Theme.CocoaDeep : Theme.Cocoa); t.resizeTextForBestFit = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 28;
            }
            var lem = bg.gameObject.AddComponent<LayoutElement>(); lem.minHeight = Theme.MinTouch; lem.minWidth = Theme.MinTouch;
            return b;
        }
        /// <summary>둥근 아이콘 버튼 (아래 띠 메뉴): 아이콘 + 작은 이름.</summary>
        public static Button IconBtn(Transform parent, UIIcon icon, string caption, Action onClick, float size = 60, Color? bg = null)
        {
            var root = Rect(parent, "IconBtn_" + icon); root.sizeDelta = new Vector2(size, size + (caption != null ? 18 : 0));
            var circle = Box(root, "Circle", bg ?? Theme.Cream, (int)(size / 2)); Anchor(circle.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(size, size));
            var line = Box(circle.transform, "Line", Theme.Cocoa, (int)(size / 2), 2); Fill(line.rectTransform); line.raycastTarget = false;
            var ic = IconImage(circle.transform, icon, size * .6f); Anchor(ic.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .6f, size * .6f));
            if (caption != null) { var t = Label(root, caption, Theme.Tiny + 1); Anchor(t.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(size + 16, 18)); Halo(t); }
            var b = circle.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None; b.onClick.AddListener(() => onClick?.Invoke());
            circle.gameObject.AddComponent<Press>();
            var le = root.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = size; le.preferredHeight = size + (caption != null ? 18 : 0);
            return b;
        }
        /// <summary>섬 위에 바로 놓이는 글자: 크림색 테두리를 둘러 밤·그늘에서도 읽히게.</summary>
        public static Text Halo(Text t)
        {
            foreach (var d in new[] { new Vector2(1.5f, -1.5f), new Vector2(-1.5f, 1.5f) })
            { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(Theme.Cream.r, Theme.Cream.g, Theme.Cream.b, .95f); o.effectDistance = d; o.useGraphicAlpha = true; }
            return t;
        }
        /// <summary>작은 빨간 점 (새 것이 있을 때).</summary>
        public static Image Badge(Transform parent)
        {
            var d = Box(parent, "Badge", Theme.Strawberry, 7); Anchor(d.rectTransform, new Vector2(1, 1), new Vector2(.5f, .5f), new Vector2(-6, -6), new Vector2(14, 14));
            d.raycastTarget = false; return d;
        }
        public static VerticalLayoutGroup VList(RectTransform rt, float spacing = 10, int pad = 0)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = spacing; v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false; return v;
        }
        public static HorizontalLayoutGroup HList(RectTransform rt, float spacing = 10, int pad = 0)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = spacing; h.padding = new RectOffset(pad, pad, pad, pad);
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false; h.childAlignment = TextAnchor.MiddleCenter; return h;
        }
        public static LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }
        /// <summary>세로로 넘기는 목록 (화면이 작아도 내용이 잘리지 않게).</summary>
        public static RectTransform Scroll(Transform parent, out RectTransform content)
        {
            var view = Rect(parent, "Scroll"); var sr = view.gameObject.AddComponent<ScrollRect>(); view.gameObject.AddComponent<RectMask2D>();
            var img = view.gameObject.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0);
            content = Rect(view, "Content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            VList(content, 10, 2); content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Elastic; sr.scrollSensitivity = 20;
            return view;
        }
    }

    /// <summary>누르면 살짝 작아졌다가 놓으면 통 튀어나온다 + 작은 소리·약한 진동 (ART_DIRECTION 11장).</summary>
    public class Press : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public static Action OnPress;              // (소리·진동: 게임 쪽에서 연결)
        Vector3 baseScale = Vector3.one; Coroutine co;
        void Awake() => baseScale = transform.localScale;
        public void OnPointerDown(PointerEventData e) { Run(Down()); OnPress?.Invoke(); }
        public void OnPointerUp(PointerEventData e) => Run(Pop());
        public void OnPointerExit(PointerEventData e) { if (transform.localScale.x < baseScale.x) Run(Pop()); }
        void Run(IEnumerator e) { if (co != null) StopCoroutine(co); if (isActiveAndEnabled) co = StartCoroutine(e); }
        IEnumerator Down() { for (float t = 0; t < .08f; t += Time.unscaledDeltaTime) { transform.localScale = baseScale * Mathf.Lerp(1f, .93f, t / .08f); yield return null; } transform.localScale = baseScale * .93f; }
        IEnumerator Pop()
        {
            for (float t = 0; t < .22f; t += Time.unscaledDeltaTime) { float u = t / .22f; transform.localScale = baseScale * (1f + .07f * Mathf.Sin(u * Mathf.PI) * (1 - u) - .07f * (1 - u) * (1 - u)); yield return null; }
            transform.localScale = baseScale;
        }
    }

    /// <summary>노치·홈 표시줄을 피한다 (아이폰 SE ~ Pro Max).</summary>
    [ExecuteAlways]
    public class SafeArea : MonoBehaviour
    {
        Rect last; Vector2Int lastScreen;
        /// <summary>테스트: 다른 기기의 안전 영역 (0~1 비율). 비우면 실제 화면.</summary>
        public static Rect NormOverride;
        void Update() => Apply();
        public void Apply()
        {
            var rt = (RectTransform)transform;
            if (NormOverride.width > 0) { rt.anchorMin = NormOverride.min; rt.anchorMax = NormOverride.max; rt.offsetMin = rt.offsetMax = Vector2.zero; last = default; return; }
            var sa = Screen.safeArea; var scr = new Vector2Int(Screen.width, Screen.height);
            if (sa == last && scr == lastScreen) return; last = sa; lastScreen = scr;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height); rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
