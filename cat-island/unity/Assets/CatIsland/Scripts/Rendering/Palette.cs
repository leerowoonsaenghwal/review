using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>기획서 3부 4장 색표. 검정과 순수 흰색은 쓰지 않는다.</summary>
    public static class Palette
    {
        public static readonly Color Cream = Hex("fbf6e6");
        public static readonly Color MilkTea = Hex("f6f1e3");
        public static readonly Color Cocoa = Hex("6f5a40");
        public static readonly Color Latte = Hex("a08a6a");
        public static readonly Color Grass = Hex("b6dd8f");
        public static readonly Color Sky = Hex("c7e3f2");
        public static readonly Color Butter = Hex("f9cf7a");
        public static readonly Color StrawberryMilk = Hex("ff7f9f");
        public static readonly Color Peach = Hex("f2a7a0");
        public static readonly Color Lavender = Hex("c9bdea");
        public static readonly Color Mint = Hex("a8e0cf");
        public static readonly Color Apricot = Hex("f7b98a");
        public static readonly Color Chestnut = Hex("8a5a44");
        public static readonly Color SunDay = Hex("fff4dc");

        // 고양이 (치즈 태비 + 흰 턱받이)
        public static readonly Color CatFur = Hex("f3b979");
        public static readonly Color CatStripe = Hex("dd8f52");
        public static readonly Color CatWhite = Hex("fbf3e4");
        public static readonly Color CatNose = Hex("f29a9a");
        public static readonly Color CatInnerEar = Hex("f6b7b0");
        public static readonly Color CatEye = Hex("3e3226");
        public static readonly Color CatEyeShine = Hex("fffaf0");

        public static readonly Color Soil = Hex("c9a77c");
        public static readonly Color GrassDark = Hex("9fcd78");
        public static readonly Color Wood = Hex("f1dcb5");
        public static readonly Color Kibble = Hex("b9814f");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }

    public static class Materials
    {
        static Material softBase, billboardBase;
        static readonly Dictionary<Color, Material> soft = new Dictionary<Color, Material>();

        public static Material Soft(Color c)
        {
            if (soft.TryGetValue(c, out var m) && m != null) return m;
            if (softBase == null) softBase = Resources.Load<Material>("CatSoftLit");
            m = new Material(softBase) { name = "Soft_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c);
            soft[c] = m;
            return m;
        }

        /// <summary>그린 무늬 텍스처를 쓰는 세상 재질 (땅·절벽·바다). worldUV 면 세상 좌표 xz 로 깐다 (tiling = 1 / 한 장의 크기 m).</summary>
        public static Material Painted(string name, Texture2D tex, Vector2 tiling, bool worldUV, float gloss = 0f, Color? tint = null)
        {
            if (softBase == null) softBase = Resources.Load<Material>("CatSoftLit");
            var m = new Material(softBase) { name = "Painted_" + name };
            m.SetColor("_BaseColor", tint ?? Color.white);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetFloat("_GroundAO", 0f);        // (땅 자체는 어둡게 하지 않는다: 시안 색 그대로)
            m.SetFloat("_RimStrength", 0f);
            m.SetFloat("_Gloss", gloss);
            m.SetFloat("_WorldUV", worldUV ? 1f : 0f);
            if (worldUV) m.EnableKeyword("_WORLDUV"); else m.DisableKeyword("_WORLDUV");
            return m;
        }

        /// <summary>개별 인스턴스 (투명도 애니메이션 등).</summary>
        public static Material NewBillboard(Texture2D tex)
        {
            if (billboardBase == null) billboardBase = Resources.Load<Material>("CatBillboard");
            var m = new Material(billboardBase) { name = "Billboard_" + (tex ? tex.name : "none") };
            m.SetTexture("_MainTex", tex);
            return m;
        }
    }
}
