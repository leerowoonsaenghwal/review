using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 세상의 그린 듯한 무늬를 코드로 그린다 (docs/ART_DIRECTION.md 2-2, 4장; 시안 style_compare.html 의 캔버스 무늬와 같은 규칙).
    /// 모든 무늬는 이어 붙여도 이음매가 없다 (가장자리를 넘는 도형은 반대편에 이어 그린다).
    /// </summary>
    public static class PaintedTextures
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>풀밭: 바탕 풀색 + 밝고 어두운 둥근 얼룩 + 세 잎 클로버 + 아주 작은 흰·노란 꽃 점. 한 장이 9.6 m.</summary>
        public static Texture2D Grass() => Get("grass", 1024, p =>
        {
            p.Fill(Palette.Hex("74c254"));
            Color lite = Palette.Hex("7cc85b"), dark = Palette.Hex("6dbb4e"), clover = Palette.Hex("63b247");
            for (int i = 0; i < 880; i++) { float r = 5f + p.R() * 12f; p.Ellipse(p.R() * p.N, p.R() * p.N, r, r * 0.7f, p.R() * 3f, p.R() < 0.5f ? lite : dark); }
            for (int i = 0; i < 280; i++)
            {
                float x = p.R() * p.N, y = p.R() * p.N, a0 = p.R() * 6.28f;
                for (int k = 0; k < 3; k++) { float a = a0 + k * 2.094f; p.Ellipse(x + Mathf.Cos(a) * 4f, y + Mathf.Sin(a) * 4f, 4f, 4f, 0f, clover); }
            }
            for (int i = 0; i < 104; i++) p.Ellipse(p.R() * p.N, p.R() * p.N, 3.2f, 3.2f, 0f, p.R() < 0.6f ? Palette.Hex("fdfbef") : Palette.Hex("ffe27a"));
        });

        /// <summary>절벽 옆면: 모래색 바탕에 둥근 돌 무늬 (가로로 긴 타원).</summary>
        public static Texture2D Cliff() => Get("cliff", 512, p =>
        {
            p.Fill(Palette.Hex("c9a57a"));
            Color a = Palette.Hex("bd9970"), b = Palette.Hex("d4b288");
            for (int i = 0; i < 360; i++) p.Ellipse(p.R() * p.N, p.R() * p.N, 24f + p.R() * 44f, 14f + p.R() * 12f, 0f, p.R() < 0.5f ? a : b);
        });

        /// <summary>모래: 바탕 + 알갱이 점. 한 장이 2.5 m.</summary>
        public static Texture2D Sand() => Get("sand", 256, p =>
        {
            p.Fill(Palette.Hex("f5e2a8"));
            for (int i = 0; i < 300; i++) p.Ellipse(p.R() * p.N, p.R() * p.N, 1.6f, 1.6f, 0f, p.R() < 0.5f ? Palette.Hex("ecd595") : Palette.Hex("fbefc6"));
        });

        /// <summary>마루: 나무 판자 (줄기색 계열, 결 무늬, 판 사이 홈). 한 장이 2 m, 판자 8장.</summary>
        public static Texture2D Planks() => Get("planks", 512, p =>
        {
            Color baseC = Palette.Hex("e2b483"), grain = Palette.Hex("cc9d6c"), gap = Palette.Hex("a8774c");
            p.Fill(baseC);
            int plank = p.N / 8;
            for (int k = 0; k < 8; k++)
            {
                // 판마다 색을 조금씩 다르게
                var c = Color.Lerp(baseC, k % 3 == 0 ? Palette.Hex("e9bf90") : k % 3 == 1 ? Palette.Hex("d9a977") : baseC, 0.7f);
                p.Rect(0, k * plank + 2, p.N, plank - 4, c);
                for (int g = 0; g < 7; g++) p.Ellipse(p.R() * p.N, k * plank + 5 + p.R() * (plank - 10), 40f + p.R() * 90f, 1.4f, 0f, grain);
                p.Ellipse(p.R() * p.N, k * plank + plank * 0.5f, 6f, 3.5f, 0f, gap);   // 옹이
                p.Rect(0, k * plank, p.N, 2, gap);
                p.Rect((k * 197) % p.N, k * plank, 3, plank, gap);                      // 판 끝 이음
            }
        });

        /// <summary>러그: 굵은 짜임 무늬 (민트). 한 장이 1 m.</summary>
        public static Texture2D Weave() => Get("weave", 256, p =>
        {
            Color a = Palette.Hex("8fdcc8"), b = Palette.Hex("7ccdb9"), c = Palette.Hex("a8e8d6");
            p.Fill(a);
            int cell = 16;
            for (int y = 0; y < p.N; y += cell)
                for (int x = 0; x < p.N; x += cell)
                {
                    bool over = ((x + y) / cell) % 2 == 0;
                    if (over) p.Ellipse(x + cell * 0.5f, y + cell * 0.5f, cell * 0.48f, cell * 0.3f, 0f, c);
                    else p.Ellipse(x + cell * 0.5f, y + cell * 0.5f, cell * 0.3f, cell * 0.48f, 0f, b);
                }
        });

        static Texture2D Get(string key, int n, System.Action<Painter> draw)
        {
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            int h = 7; foreach (char ch in key) h = (h * 31 + ch) & 0x7fffff;   // (늘 같은 그림이 되게 고정 씨앗)
            var p = new Painter(n, h);
            draw(p);
            t = p.ToTexture(key);
            cache[key] = t;
            return t;
        }

        /// <summary>작은 그림판: 부드러운 가장자리(1 px)의 타원·사각형을 이어 붙는 그림으로 칠한다.</summary>
        public class Painter
        {
            public readonly int N;
            readonly Color[] px;
            int seed;
            public Painter(int n, int seed) { N = n; px = new Color[n * n]; this.seed = seed % 2147483646 + 1; }

            /// <summary>0~1 난수 (시안과 같은 Park–Miller).</summary>
            public float R() { seed = (int)((seed * 16807L) % 2147483647L); return seed / 2147483647f; }

            public void Fill(Color c) { for (int i = 0; i < px.Length; i++) px[i] = c; }

            public void Rect(int x0, int y0, int w, int h, Color c)
            {
                for (int y = y0; y < y0 + h; y++)
                    for (int x = x0; x < x0 + w; x++)
                        px[Wrap(y) * N + Wrap(x)] = c;
            }

            public void Ellipse(float cx, float cy, float rx, float ry, float rot, Color c)
            {
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot), ext = Mathf.Max(rx, ry) + 1.5f;
                int x0 = Mathf.FloorToInt(cx - ext), x1 = Mathf.CeilToInt(cx + ext), y0 = Mathf.FloorToInt(cy - ext), y1 = Mathf.CeilToInt(cy + ext);
                float m = Mathf.Min(rx, ry);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float u = (dx * cs + dy * sn) / rx, v = (-dx * sn + dy * cs) / ry;
                        float d = (Mathf.Sqrt(u * u + v * v) - 1f) * m;          // (대략 픽셀 단위 거리)
                        float a = Mathf.Clamp01(0.5f - d);
                        if (a <= 0f) continue;
                        int i = Wrap(y) * N + Wrap(x);
                        px[i] = Color.Lerp(px[i], c, a);
                    }
            }

            int Wrap(int v) { v %= N; return v < 0 ? v + N : v; }

            public Texture2D ToTexture(string name)
            {
                var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                t.SetPixels(px);
                t.Apply(true, true);
                return t;
            }
        }
    }

    /// <summary>
    /// 세상의 빛·안개·둥근 세상 값 (docs/ART_DIRECTION.md 2-1, 4장).
    /// 시안(style_compare.html)은 카메라가 고양이에서 5.4 떨어져 있다: 안개와 휨은 카메라 거리에 맞춰 같은 화면 인상이 되게 늘린다.
    /// </summary>
    public static class WorldStyle
    {
        public static readonly Color Sky = Palette.Hex("8fd6f2");
        const float MockDist = 5.4f;          // 시안 카메라 거리
        const float FogNear = 9f, FogFar = 20f, CurveStart = 2f, CurveK = 0.03f;

        /// <summary>카메라가 바뀔 때마다: 초점(땅 위), 보는 방향, 초점까지 거리.</summary>
        public static void Apply(Vector3 focus, Vector3 forward, float dist)
        {
            float s = dist / MockDist;
            Vector2 f = new Vector2(forward.x, forward.z);
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector2.up;
            Shader.SetGlobalVector("_CurveCenterDir", new Vector4(focus.x, focus.z, f.x, f.y));
            Shader.SetGlobalVector("_CurveParams", new Vector4(CurveK / s, CurveStart * s, 0f, 0f));
            RenderSettings.fogStartDistance = FogNear * s;
            RenderSettings.fogEndDistance = FogFar * s;
        }

        public static void Off()
        {
            Shader.SetGlobalVector("_CurveParams", Vector4.zero);
        }
    }
}
