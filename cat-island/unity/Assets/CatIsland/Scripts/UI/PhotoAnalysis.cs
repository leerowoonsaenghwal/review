using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CatIsland.UI
{
    /// <summary>iOS 사진 고르기 다리 (Plugins/iOS/CatPhoto.mm).</summary>
    public class NativePhoto : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CatPhoto_Pick(string receiver);
#else
        static void CatPhoto_Pick(string receiver) { }
#endif
        static NativePhoto inst; static Action<string> pending;
        public static void Pick(Action<string> done)
        {
            if (!inst) inst = new GameObject("NativePhoto").AddComponent<NativePhoto>();
            pending = done; CatPhoto_Pick(inst.gameObject.name);
        }
        void OnPicked(string path) { var p = pending; pending = null; p?.Invoke(path); }
    }

    /// <summary>
    /// 사진에서 털을 읽는다 (prototype/3d/photo2cat.js 의 규칙을 옮김): 조명 보정 → 가운데(고양이) 영역의 Lab 색을 k-평균
    /// → 바탕·진한 색·흰 부분 비율 → 무늬 → 가까운 품종, 눈 모양·수염 제안 (ART_DIRECTION 3-1). 모두 기기 안 계산.
    /// </summary>
    public static class PhotoAnalysis
    {
        public static PhotoCat Read(Texture2D tex)
        {
            int w = 96, h = Mathf.Max(8, Mathf.RoundToInt(96f * tex.height / Mathf.Max(1, tex.width)));
            var px = Resize(tex, w, h);
            // 조명 보정: 회색 세계(살짝) + 밝은 쪽이 0.85 가 되게
            var lin = px.Select(c => new Vector3(Lin(c.r), Lin(c.g), Lin(c.b))).ToArray();
            var Y = lin.Select(v => .2126f * v.x + .7152f * v.y + .0722f * v.z).ToArray();
            Vector3 sum = Vector3.zero; int n = 0; for (int i = 0; i < lin.Length; i++) if (Y[i] > .01f && Y[i] < .9f) { sum += lin[i]; n++; }
            float m = (sum.x + sum.y + sum.z) / 3f; if (m <= 0) m = 1;
            var gain = new Vector3(Gain(m, sum.x), Gain(m, sum.y), Gain(m, sum.z));
            float p95 = Y.OrderBy(v => v).ElementAt((int)(Y.Length * .95f)); float exposure = Mathf.Clamp(.85f / Mathf.Max(p95, 1e-3f), 1, 4);
            // 고양이 영역: 가운데 타원 (사진 고양이는 대개 가운데) - iOS 에서 피사체 마스크로 바꿀 수 있다
            var pts = new List<Vector3>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float dx = (x + .5f) / w - .5f, dy = (y + .5f) / h - .5f; if (dx * dx / .1f + dy * dy / .12f > 1) continue;
                    var v = Vector3.Scale(lin[y * w + x], gain) * exposure; pts.Add(Lab(Mathf.Min(1, v.x), Mathf.Min(1, v.y), Mathf.Min(1, v.z)));
                }
            var cl = KMeans(pts, 3);
            var white = cl.Where(c => c.c.x > 82 && Chroma(c.c) < 14).Sum(c => c.f);
            var colored = cl.Where(c => !(c.c.x > 82 && Chroma(c.c) < 14)).OrderByDescending(c => c.f).ToList();
            Vector3 baseC = colored.Count > 0 ? colored[0].c : cl[0].c; Vector3 dark = colored.Count > 1 ? colored[1].c : baseC;
            string pattern;
            bool orange = colored.Any(c => Hue(c.c) > 35 && Hue(c.c) < 80 && Chroma(c.c) > 22 && c.f > .12f), black = colored.Any(c => c.c.x < 30 && c.f > .12f);
            if (white > .75f) pattern = "solid";
            else if (orange && black && white > .12f) pattern = "calico";
            else if (orange && black) pattern = "tortie";
            else if (white > .3f && dark.x < 35) pattern = "tuxedo";
            else if (white > .25f) pattern = "bicolor";
            else if (colored.Count > 1 && Mathf.Abs(baseC.x - dark.x) > 16 && colored[1].f > .2f && Mathf.Abs(Hue(baseC) - Hue(dark)) < 40) pattern = "mackerel";
            else pattern = "solid";
            if (pattern == "solid" && white > .75f) baseC = cl.OrderByDescending(c => c.c.x).First().c;
            float L = baseC.x; bool grey = Chroma(baseC) < 12;
            // 품종 고르기: 털 무늬가 같은 몸을 고른다 (게임은 그 몸의 털 무늬에 사진 색만 입힌다: CatCoat).
            // 턱시도·젖소·삼색·카오스·흰 바탕 태비는 품종 대신 코리안 숏헤어 몸 + 그 무늬의 털 (variant)
            string variant = "";
            if (pattern == "calico" || pattern == "tortie") variant = pattern;
            else if (pattern == "tuxedo" || pattern == "bicolor") variant = white > .5f ? "cow" : "tuxedo";
            else if (pattern == "mackerel" && white > .15f) variant = "cheese_white";
            string breed = variant != "" ? "korean_shorthair" : pattern switch
            {
                "mackerel" => Hue(baseC) > 35 && Hue(baseC) < 95 && !grey ? "korean_shorthair" : "american_shorthair",
                _ => L > 88 ? "turkish_angora" : L < 30 ? "bombay" : grey ? "russian_blue" : "korean_shorthair",
            };
            string eye = L < 30 ? "iris" : "dark", whisk = "short";
            // 삼색·카오스: 기본 = 어두운 색, 두 번째 = 주황 / 흰색은 사진의 흰 털 색 그대로
            var orangeC = colored.Where(c => Hue(c.c) > 35 && Hue(c.c) < 80 && Chroma(c.c) > 22).Select(c => c.c).DefaultIfEmpty(dark).First();
            var blackC = colored.Where(c => c.c.x < 30).Select(c => c.c).DefaultIfEmpty(baseC).First();
            if (variant == "calico" || variant == "tortie") { baseC = blackC; dark = blackC * .8f; }
            if (variant == "tuxedo" || variant == "cow") dark = baseC * .8f;
            var whiteC = cl.Where(c => c.c.x > 82 && Chroma(c.c) < 14).Select(c => c.c).DefaultIfEmpty(new Vector3(96, 0, 3)).First();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string json = $"{{\"pattern\":\"{pattern}\",\"variant\":\"{variant}\",\"base\":\"{Hex(baseC)}\",\"dark\":\"{Hex(dark)}\",\"white\":\"{Hex(whiteC)}\",\"second\":\"{Hex(orangeC)}\",\"whiteLevel\":{white.ToString("0.00", inv)}}}";
            return new PhotoCat { breed = breed, eyeStyle = eye, whiskerStyle = whisk, coatJson = json };
        }

        static float Gain(float m, float s) => Mathf.Clamp(Mathf.Pow(m / (s > 0 ? s : m), .35f), .88f, 1.15f);
        static Color[] Resize(Texture2D t, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
            var o = new Texture2D(w, h, TextureFormat.RGBA32, false); o.ReadPixels(new Rect(0, 0, w, h), 0, 0); o.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var c = o.GetPixels(); UnityEngine.Object.Destroy(o); return c;
        }
        static float Lin(float c) => c <= .04045f ? c / 12.92f : Mathf.Pow((c + .055f) / 1.055f, 2.4f);
        static float Gam(float c) => Mathf.Clamp01(c <= .0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1 / 2.4f) - .055f);
        public static Vector3 Lab(float r, float g, float b)
        {
            float x = (.4124f * r + .3576f * g + .1805f * b) / .95047f, y = .2126f * r + .7152f * g + .0722f * b, z = (.0193f * r + .1192f * g + .9505f * b) / 1.08883f;
            float F(float t) => t > .008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
            x = F(x); y = F(y); z = F(z); return new Vector3(116 * y - 16, 500 * (x - y), 200 * (y - z));
        }
        static string Hex(Vector3 lab)
        {
            float fy = (lab.x + 16) / 116, fx = fy + lab.y / 500, fz = fy - lab.z / 200;
            float G(float t) => t * t * t > .008856f ? t * t * t : (t - 16f / 116f) / 7.787f;
            float x = G(fx) * .95047f, y = G(fy), z = G(fz) * 1.08883f;
            var c = new Color(Gam(3.2406f * x - 1.5372f * y - .4986f * z), Gam(-.9689f * x + 1.8758f * y + .0415f * z), Gam(.0557f * x - .204f * y + 1.057f * z));
            return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();
        }
        static float Chroma(Vector3 c) => Mathf.Sqrt(c.y * c.y + c.z * c.z);
        static float Hue(Vector3 c) => (Mathf.Atan2(c.z, c.y) * Mathf.Rad2Deg + 360) % 360;

        struct Cl { public Vector3 c; public float f; }
        static List<Cl> KMeans(List<Vector3> pts, int k)
        {
            if (pts.Count == 0) return new List<Cl> { new Cl { c = new Vector3(60, 0, 0), f = 1 } };
            var rnd = new System.Random(7); var cs = new List<Vector3> { pts[rnd.Next(pts.Count)] };
            while (cs.Count < k) { var far = pts.OrderByDescending(p => cs.Min(c => (p - c).sqrMagnitude)).First(); cs.Add(far); }
            var lab = new int[pts.Count];
            for (int it = 0; it < 12; it++)
            {
                var acc = new Vector3[k]; var cnt = new int[k];
                for (int i = 0; i < pts.Count; i++) { int b = 0; float bd = float.MaxValue; for (int j = 0; j < k; j++) { float d = (pts[i] - cs[j]).sqrMagnitude; if (d < bd) { bd = d; b = j; } } lab[i] = b; acc[b] += pts[i]; cnt[b]++; }
                for (int j = 0; j < k; j++) if (cnt[j] > 0) cs[j] = acc[j] / cnt[j];
            }
            var cnt2 = new int[k]; foreach (var l in lab) cnt2[l]++;
            return Enumerable.Range(0, k).Select(j => new Cl { c = cs[j], f = cnt2[j] / (float)pts.Count }).OrderByDescending(c => c.f).ToList();
        }
    }
}
