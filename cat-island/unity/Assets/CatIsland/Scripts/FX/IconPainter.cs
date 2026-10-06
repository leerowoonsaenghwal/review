using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    public enum Icon { Bubble, Heart, Fish, Sleep, Hand, Exclaim, Note, Sparkle, Dust }

    /// <summary>
    /// 말풍선 아이콘을 거리장(SDF)으로 그린다. 그림 파일이 없어도 번역할 글자 없이 상태를 보여 준다.
    /// 두꺼운 둥근 선 + 단색 채움 (기획서 3부 7장 아이콘 규칙).
    /// </summary>
    public static class IconPainter
    {
        const int Size = 128;
        static readonly Dictionary<Icon, Texture2D> cache = new Dictionary<Icon, Texture2D>();

        public static Texture2D Get(Icon icon)
        {
            if (cache.TryGetValue(icon, out var t) && t != null) return t;
            t = Paint(icon);
            cache[icon] = t;
            return t;
        }

        // ---- 거리 함수 (음수 = 안쪽). 좌표는 -1..1 ----
        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
        {
            Vector2 q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
            return (q.magnitude - 1f) * Mathf.Min(r.x, r.y);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // 부호 있는 삼각형 거리 (근사)
            float d = Mathf.Min(Mathf.Min(Segment(p, a, b, 0f), Segment(p, b, c, 0f)), Segment(p, c, a, 0f));
            bool inside = Side(p, a, b) >= 0 == Side(p, b, c) >= 0 && Side(p, b, c) >= 0 == Side(p, c, a) >= 0;
            return inside ? -d : d;
        }

        static float Side(Vector2 p, Vector2 a, Vector2 b) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static float Heart(Vector2 p, float s)
        {
            p /= s;
            p.y += 0.15f;
            float a = Circle(p, new Vector2(-0.28f, 0.18f), 0.32f);
            float b = Circle(p, new Vector2(0.28f, 0.18f), 0.32f);
            float c = Triangle(p, new Vector2(-0.57f, 0.07f), new Vector2(0.57f, 0.07f), new Vector2(0f, -0.62f));
            return Mathf.Min(Mathf.Min(a, b), c) * s;
        }

        static float Union(params float[] d)
        {
            float m = float.MaxValue;
            foreach (var x in d) m = Mathf.Min(m, x);
            return m;
        }

        struct Layer { public Func<Vector2, float> sdf; public Color color; }

        static Texture2D Paint(Icon icon)
        {
            var layers = new List<Layer>();
            Color outline = Palette.Cocoa;
            Color bubble = Palette.Cream;

            switch (icon)
            {
                case Icon.Bubble:
                    layers.Add(new Layer { sdf = p => Union(Circle(p, new Vector2(0, 0.1f), 0.78f), Triangle(p, new Vector2(-0.2f, -0.55f), new Vector2(0.2f, -0.55f), new Vector2(0f, -0.95f))) - 0.0f, color = bubble });
                    break;
                case Icon.Heart:
                    layers.Add(new Layer { sdf = p => Heart(p, 0.95f), color = Palette.StrawberryMilk });
                    layers.Add(new Layer { sdf = p => Ellipse(p, new Vector2(-0.33f, 0.2f), new Vector2(0.1f, 0.15f)), color = new Color(1f, 0.82f, 0.86f) });
                    break;
                case Icon.Fish:
                    layers.Add(new Layer { sdf = p => Union(Ellipse(p, new Vector2(0.1f, 0f), new Vector2(0.55f, 0.33f)), Triangle(p, new Vector2(-0.38f, 0f), new Vector2(-0.82f, 0.34f), new Vector2(-0.82f, -0.34f)) - 0.04f), color = Palette.Sky });
                    layers.Add(new Layer { sdf = p => Circle(p, new Vector2(0.38f, 0.06f), 0.07f), color = Palette.Cocoa });
                    layers.Add(new Layer { sdf = p => Segment(p, new Vector2(0.12f, 0.2f), new Vector2(0.12f, -0.2f), 0.035f), color = new Color(0.62f, 0.8f, 0.9f) });
                    break;
                case Icon.Sleep:
                    layers.Add(new Layer { sdf = p => Union(
                        Segment(p, new Vector2(-0.55f, 0.35f), new Vector2(0.05f, 0.35f), 0.08f),
                        Segment(p, new Vector2(0.05f, 0.35f), new Vector2(-0.55f, -0.25f), 0.08f),
                        Segment(p, new Vector2(-0.55f, -0.25f), new Vector2(0.05f, -0.25f), 0.08f)), color = Palette.Lavender });
                    layers.Add(new Layer { sdf = p => Union(
                        Segment(p, new Vector2(0.28f, 0.68f), new Vector2(0.62f, 0.68f), 0.06f),
                        Segment(p, new Vector2(0.62f, 0.68f), new Vector2(0.28f, 0.32f), 0.06f),
                        Segment(p, new Vector2(0.28f, 0.32f), new Vector2(0.62f, 0.32f), 0.06f)), color = Palette.Lavender });
                    break;
                case Icon.Hand:
                    // 굵고 단순한 손바닥 (손가락 4개 + 엄지)
                    layers.Add(new Layer { sdf = p => Union(
                        Ellipse(p, new Vector2(0.05f, -0.28f), new Vector2(0.42f, 0.36f)),
                        Segment(p, new Vector2(-0.27f, -0.1f), new Vector2(-0.3f, 0.36f), 0.12f),
                        Segment(p, new Vector2(-0.03f, -0.05f), new Vector2(-0.04f, 0.6f), 0.12f),
                        Segment(p, new Vector2(0.21f, -0.05f), new Vector2(0.23f, 0.52f), 0.12f),
                        Segment(p, new Vector2(0.42f, -0.12f), new Vector2(0.47f, 0.28f), 0.11f),
                        Segment(p, new Vector2(-0.3f, -0.38f), new Vector2(-0.62f, -0.1f), 0.12f)), color = Palette.Peach });
                    break;
                case Icon.Exclaim:
                    layers.Add(new Layer { sdf = p => Union(Segment(p, new Vector2(0f, 0.55f), new Vector2(0f, -0.05f), 0.13f), Circle(p, new Vector2(0f, -0.45f), 0.13f)), color = Palette.Butter });
                    break;
                case Icon.Note:
                    layers.Add(new Layer { sdf = p => Union(Ellipse(p, new Vector2(-0.12f, -0.4f), new Vector2(0.22f, 0.17f)), Segment(p, new Vector2(0.08f, -0.4f), new Vector2(0.08f, 0.55f), 0.06f), Segment(p, new Vector2(0.08f, 0.55f), new Vector2(0.42f, 0.35f), 0.07f)), color = Palette.Butter });
                    break;
                case Icon.Sparkle:
                    layers.Add(new Layer { sdf = p => Union(Ellipse(p, Vector2.zero, new Vector2(0.14f, 0.7f)), Ellipse(p, Vector2.zero, new Vector2(0.7f, 0.14f))), color = Palette.Butter });
                    break;
                case Icon.Dust:   // (착지 먼지: 작은 구름 뭉치)
                    layers.Add(new Layer { sdf = p => Union(Circle(p, new Vector2(-0.3f, -0.1f), 0.38f), Circle(p, new Vector2(0.28f, -0.12f), 0.34f), Circle(p, new Vector2(0f, 0.22f), 0.42f)), color = Palette.Cream });
                    break;
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Icon_" + icon, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[Size * Size];
            float aa = 2.5f / Size;
            float line = 0.07f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    // 아이콘 크기를 텍스처 안쪽 85%로
                    Vector2 q = p / 0.85f;
                    Color c = new Color(outline.r, outline.g, outline.b, 0f);
                    // 전체 실루엣 외곽선
                    float silhouette = float.MaxValue;
                    foreach (var l in layers) silhouette = Mathf.Min(silhouette, l.sdf(q));
                    float oa = 1f - Mathf.Clamp01((silhouette - line) / aa + 0.5f);
                    c = new Color(outline.r, outline.g, outline.b, oa);
                    foreach (var l in layers)
                    {
                        float d = l.sdf(q);
                        float a = 1f - Mathf.Clamp01(d / aa + 0.5f);
                        if (a > 0f) c = Color.Lerp(c, new Color(l.color.r, l.color.g, l.color.b, 1f), a);
                    }
                    px[y * Size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}
