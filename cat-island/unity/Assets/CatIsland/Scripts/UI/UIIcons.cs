using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatIsland.UI
{
    public enum UIIcon
    {
        Coin, Jelly, Shop, Bag, Tasks, Cat, Camera, Gear, Close, Star, Paw, Fish, Gift, Walk, Sprout, Hammer, Home, Fence, Check, Lock, Plus, Heart,
        Book, Calendar, AdPlay, Bell, Note, Speaker, Cloud, Back, Yarn, Moon, Info, Rotate, Trash
    }

    /// <summary>
    /// 화면 아이콘을 거리 함수로 그린다 (그림 파일·글자 없이). 두꺼운 둥근 코코아 선 + 단색 채움 (기획서 3부 7장).
    /// 동물의 숲의 나뭇잎 마크 같은 모양은 쓰지 않는다 (ART_DIRECTION 8장).
    /// </summary>
    public static class UIIcons
    {
        const int N = 96;
        static readonly Dictionary<UIIcon, Sprite> cache = new Dictionary<UIIcon, Sprite>();
        public static Sprite Sprite(UIIcon i)
        {
            if (cache.TryGetValue(i, out var s) && s) return s;
            var t = Paint(i); s = UnityEngine.Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100);
            cache[i] = s; return s;
        }

        // ---- 거리 함수 (좌표 -1..1, 음수 = 안)
        static float Circle(Vector2 p, float x, float y, float r) => (p - new Vector2(x, y)).magnitude - r;
        static float Box(Vector2 p, float x, float y, float hx, float hy, float r = 0)
        { var q = new Vector2(Mathf.Abs(p.x - x) - hx + r, Mathf.Abs(p.y - y) - hy + r); return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r; }
        static float Seg(Vector2 p, float ax, float ay, float bx, float by, float r)
        { var a = new Vector2(ax, ay); var ba = new Vector2(bx, by) - a; var pa = p - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba)); return (pa - ba * h).magnitude - r; }
        static float Ell(Vector2 p, float x, float y, float rx, float ry) { var q = new Vector2((p.x - x) / rx, (p.y - y) / ry); return (q.magnitude - 1) * Mathf.Min(rx, ry); }
        static float U(params float[] d) { float m = 1e9f; foreach (var x in d) m = Mathf.Min(m, x); return m; }
        static float Sub(float a, float b) => Mathf.Max(a, -b);
        static float Tri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d = U(Seg(p, a.x, a.y, b.x, b.y, 0), Seg(p, b.x, b.y, c.x, c.y, 0), Seg(p, c.x, c.y, a.x, a.y, 0));
            float s1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x), s2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x), s3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
            bool inside = (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0); return inside ? -d : d;
        }
        static float Heart(Vector2 p, float x, float y, float s)
        { p = (p - new Vector2(x, y)) / s; p.y += .12f; return U(Circle(p, -.28f, .2f, .32f), Circle(p, .28f, .2f, .32f), Tri(p, new Vector2(-.58f, .08f), new Vector2(.58f, .08f), new Vector2(0, -.62f))) * s; }
        static float PawPrint(Vector2 p, float x, float y, float s)
        { p = (p - new Vector2(x, y)) / s; return U(Ell(p, 0, -.25f, .42f, .34f), Circle(p, -.5f, .2f, .17f), Circle(p, -.18f, .45f, .17f), Circle(p, .18f, .45f, .17f), Circle(p, .5f, .2f, .17f)) * s; }

        struct L { public Func<Vector2, float> d; public Color c; public bool line; public L(Func<Vector2, float> d, Color c, bool line = true) { this.d = d; this.c = c; this.line = line; } }

        static Texture2D Paint(UIIcon icon)
        {
            Color cocoa = Theme.Cocoa, cream = Theme.Cream, straw = Theme.Strawberry, butter = Theme.Butter, sea = Theme.Sea, grass = Theme.Grass, peach = Theme.Peach, latte = Theme.Latte;
            var Ls = new List<L>();
            switch (icon)
            {
                case UIIcon.Coin: Ls.Add(new L(p => Circle(p, 0, 0, .78f), butter)); Ls.Add(new L(p => PawPrint(p, 0, -.02f, .55f), Theme.Hex("E9A93A"), false)); break;
                case UIIcon.Jelly: Ls.Add(new L(p => Box(p, 0, -.05f, .62f, .55f, .45f), straw)); Ls.Add(new L(p => Ell(p, -.25f, .22f, .14f, .1f), cream, false)); break;
                case UIIcon.Shop: Ls.Add(new L(p => Box(p, 0, -.2f, .7f, .48f, .1f), cream)); Ls.Add(new L(p => Box(p, 0, .42f, .82f, .2f, .14f), straw)); Ls.Add(new L(p => Box(p, 0, -.35f, .2f, .33f, .06f), sea)); break;
                case UIIcon.Bag: Ls.Add(new L(p => Sub(Seg(p, -.3f, .45f, .3f, .45f, .14f), Seg(p, -.3f, .45f, .3f, .45f, .04f)), cocoa, false)); Ls.Add(new L(p => Box(p, 0, -.15f, .68f, .58f, .25f), peach)); break;
                case UIIcon.Tasks: Ls.Add(new L(p => Box(p, 0, 0, .6f, .78f, .14f), cream)); for (int i = 0; i < 3; i++) { float y = .4f - i * .38f; Ls.Add(new L(p => Seg(p, -.05f, y, .4f, y, .06f), latte, false)); Ls.Add(new L(p => Circle(p, -.32f, y, .1f), straw, false)); } break;
                case UIIcon.Cat:
                    Ls.Add(new L(p => U(Ell(p, 0, -.12f, .72f, .62f), Tri(p, new Vector2(-.66f, .1f), new Vector2(-.18f, .4f), new Vector2(-.6f, .85f)), Tri(p, new Vector2(.66f, .1f), new Vector2(.18f, .4f), new Vector2(.6f, .85f))), Theme.Hex("C9C4BB")));
                    Ls.Add(new L(p => U(Circle(p, -.26f, -.05f, .09f), Circle(p, .26f, -.05f, .09f)), cocoa, false)); Ls.Add(new L(p => Ell(p, 0, -.25f, .08f, .05f), straw, false)); break;
                case UIIcon.Camera: Ls.Add(new L(p => Box(p, 0, -.1f, .8f, .55f, .18f), sea)); Ls.Add(new L(p => Box(p, -.25f, .5f, .22f, .12f, .06f), sea)); Ls.Add(new L(p => Circle(p, .05f, -.1f, .3f), cream)); break;
                case UIIcon.Gear: Ls.Add(new L(p => { float a = Mathf.Atan2(p.y, p.x), r = p.magnitude; return Sub(r - (.62f + .14f * Mathf.Clamp(Mathf.Cos(a * 8) * 3, -1, 1)), Circle(p, 0, 0, .22f)); }, latte)); break;
                case UIIcon.Close: Ls.Add(new L(p => U(Seg(p, -.45f, -.45f, .45f, .45f, .1f), Seg(p, -.45f, .45f, .45f, -.45f, .1f)), cocoa, false)); break;
                case UIIcon.Star: Ls.Add(new L(p => { float a = Mathf.Atan2(p.x, p.y), r = p.magnitude; float k = Mathf.Cos(Mathf.Repeat(a + Mathf.PI / 5, 2 * Mathf.PI / 5) - Mathf.PI / 5); return r - Mathf.Lerp(.36f, .8f, Mathf.Pow(Mathf.Clamp01((k - .81f) / .19f), 1.2f)); }, butter)); break;
                case UIIcon.Paw: Ls.Add(new L(p => PawPrint(p, 0, 0, .85f), straw)); break;
                case UIIcon.Fish: Ls.Add(new L(p => U(Ell(p, -.1f, 0, .6f, .36f), Tri(p, new Vector2(.42f, 0), new Vector2(.85f, .38f), new Vector2(.85f, -.38f))), sea)); Ls.Add(new L(p => Circle(p, -.4f, .06f, .07f), cocoa, false)); break;
                case UIIcon.Gift: Ls.Add(new L(p => Box(p, 0, -.2f, .65f, .5f, .1f), straw)); Ls.Add(new L(p => Box(p, 0, .38f, .75f, .16f, .08f), straw)); Ls.Add(new L(p => U(Box(p, 0, -.05f, .1f, .68f), Ell(p, -.22f, .6f, .2f, .13f), Ell(p, .22f, .6f, .2f, .13f)), butter, false)); break;
                case UIIcon.Walk: Ls.Add(new L(p => PawPrint(p, -.35f, -.32f, .42f), latte)); Ls.Add(new L(p => PawPrint(p, .32f, .3f, .42f), latte)); break;
                case UIIcon.Sprout: Ls.Add(new L(p => Seg(p, 0, -.7f, 0, .1f, .07f), grass)); Ls.Add(new L(p => U(Ell(p, -.3f, .25f, .32f, .17f), Ell(p, .3f, .32f, .32f, .17f)), grass)); Ls.Add(new L(p => Box(p, 0, -.62f, .5f, .18f, .08f), Theme.Hex("C99A6B"))); break;
                case UIIcon.Hammer: Ls.Add(new L(p => Seg(p, -.4f, -.6f, .2f, .2f, .1f), Theme.Hex("C99A6B"))); Ls.Add(new L(p => Box(p, .3f, .42f, .42f, .17f, .08f), latte)); break;
                case UIIcon.Home: Ls.Add(new L(p => Box(p, 0, -.25f, .58f, .45f, .08f), cream)); Ls.Add(new L(p => Tri(p, new Vector2(-.8f, .12f), new Vector2(.8f, .12f), new Vector2(0, .8f)), straw)); Ls.Add(new L(p => Box(p, 0, -.42f, .14f, .28f, .06f), Theme.Hex("C99A6B"))); break;
                case UIIcon.Fence: for (int i = 0; i < 3; i++) { float x = -.5f + i * .5f; Ls.Add(new L(p => U(Box(p, x, -.1f, .13f, .6f, .05f), Tri(p, new Vector2(x - .13f, .48f), new Vector2(x + .13f, .48f), new Vector2(x, .72f))), Theme.Hex("E2B483"))); } Ls.Add(new L(p => Box(p, 0, 0, .78f, .09f), Theme.Hex("C99A6B"))); break;
                case UIIcon.Check: Ls.Add(new L(p => U(Seg(p, -.5f, 0, -.12f, -.4f, .13f), Seg(p, -.12f, -.4f, .55f, .45f, .13f)), grass)); break;
                case UIIcon.Lock: Ls.Add(new L(p => Sub(Box(p, 0, .25f, .38f, .45f, .38f), Box(p, 0, .25f, .2f, .3f, .2f)), latte)); Ls.Add(new L(p => Box(p, 0, -.25f, .6f, .45f, .12f), butter)); break;
                case UIIcon.Plus: Ls.Add(new L(p => U(Seg(p, -.5f, 0, .5f, 0, .13f), Seg(p, 0, -.5f, 0, .5f, .13f)), cocoa, false)); break;
                case UIIcon.Heart: Ls.Add(new L(p => Heart(p, 0, 0, .95f), straw)); break;
                case UIIcon.Book: Ls.Add(new L(p => Box(p, 0, 0, .66f, .78f, .12f), sea)); Ls.Add(new L(p => PawPrint(p, .05f, .05f, .35f), cream, false)); break;
                case UIIcon.Calendar: Ls.Add(new L(p => Box(p, 0, -.1f, .72f, .65f, .14f), cream)); Ls.Add(new L(p => Box(p, 0, .45f, .72f, .18f, .12f), straw)); Ls.Add(new L(p => PawPrint(p, 0, -.15f, .38f), straw, false)); break;
                case UIIcon.AdPlay: Ls.Add(new L(p => Box(p, 0, 0, .78f, .58f, .2f), butter)); Ls.Add(new L(p => Tri(p, new Vector2(-.2f, -.3f), new Vector2(-.2f, .3f), new Vector2(.32f, 0)), cream)); break;
                case UIIcon.Bell: Ls.Add(new L(p => U(Box(p, 0, .05f, .45f, .5f, .4f), Box(p, 0, -.38f, .62f, .1f, .08f)), butter)); Ls.Add(new L(p => Circle(p, 0, -.62f, .12f), latte)); break;
                case UIIcon.Note: Ls.Add(new L(p => U(Ell(p, -.3f, -.45f, .25f, .2f), Seg(p, -.08f, -.45f, -.08f, .6f, .07f), Seg(p, -.08f, .6f, .4f, .42f, .08f)), straw)); break;
                case UIIcon.Speaker: Ls.Add(new L(p => U(Box(p, -.45f, 0, .18f, .25f, .05f), Tri(p, new Vector2(-.35f, 0), new Vector2(.1f, .55f), new Vector2(.1f, -.55f))), latte)); Ls.Add(new L(p => Sub(Sub(Circle(p, .1f, 0, .62f), Circle(p, .1f, 0, .48f)), Box(p, -.4f, 0, .5f, 1f)), latte, false)); break;
                case UIIcon.Cloud: Ls.Add(new L(p => U(Circle(p, -.3f, -.1f, .32f), Circle(p, .1f, .1f, .42f), Circle(p, .45f, -.15f, .28f), Box(p, .05f, -.25f, .55f, .18f, .15f)), Theme.Sky)); break;
                case UIIcon.Back: Ls.Add(new L(p => U(Seg(p, .3f, .5f, -.2f, 0, .12f), Seg(p, -.2f, 0, .3f, -.5f, .12f)), cocoa, false)); break;
                case UIIcon.Yarn: Ls.Add(new L(p => Circle(p, 0, 0, .7f), straw)); Ls.Add(new L(p => U(Seg(p, -.6f, -.1f, .5f, .45f, .04f), Seg(p, -.45f, -.45f, .6f, .1f, .04f), Seg(p, -.2f, .6f, .2f, -.65f, .04f)), Theme.Hex("D95C78"), false)); break;
                case UIIcon.Moon: Ls.Add(new L(p => Sub(Circle(p, 0, 0, .7f), Circle(p, .35f, .25f, .55f)), butter)); break;
                case UIIcon.Info: Ls.Add(new L(p => Circle(p, 0, 0, .78f), Theme.Sky)); Ls.Add(new L(p => U(Seg(p, 0, -.4f, 0, .05f, .1f), Circle(p, 0, .38f, .11f)), cocoa, false)); break;
                case UIIcon.Rotate: Ls.Add(new L(p => Sub(Sub(Circle(p, 0, 0, .6f), Circle(p, 0, 0, .42f)), Box(p, .4f, .4f, .4f, .4f)), sea)); Ls.Add(new L(p => Tri(p, new Vector2(.2f, .75f), new Vector2(.2f, .15f), new Vector2(.62f, .45f)), sea)); break;
                case UIIcon.Trash: Ls.Add(new L(p => Box(p, 0, -.15f, .5f, .6f, .12f), latte)); Ls.Add(new L(p => Box(p, 0, .58f, .65f, .1f, .05f), latte)); break;
                default: Ls.Add(new L(p => Circle(p, 0, 0, .6f), latte)); break;
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "icon_" + icon, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N]; float px1 = 2f / N, stroke = .085f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2((x + .5f) / N * 2 - 1, (y + .5f) / N * 2 - 1); var c = new Color(cocoa.r, cocoa.g, cocoa.b, 0);
                    foreach (var l in Ls)
                    {
                        float d = l.d(p);
                        if (l.line) { float ao = Mathf.Clamp01(.5f - (d - stroke) / px1); c = Over(c, new Color(cocoa.r, cocoa.g, cocoa.b, ao)); }
                        float a = Mathf.Clamp01(.5f - d / px1); c = Over(c, new Color(l.c.r, l.c.g, l.c.b, a));
                    }
                    px[y * N + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true); return tex;
        }
        static Color Over(Color b, Color f) { float a = f.a + b.a * (1 - f.a); if (a < 1e-5f) return new Color(f.r, f.g, f.b, 0); var c = (f * f.a + b * b.a * (1 - f.a)) / a; c.a = a; return c; }
    }
}
