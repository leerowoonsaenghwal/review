using System;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 사진으로 만든 고양이의 털 색 (기획서 3부: 사진 → 털 색·무늬). 품종 털 그림의 무늬는 그대로 두고 색만 바꾼다.
    /// 고양이 파일과 함께 굽는 털 마스크(RGBA = 기본·무늬·흰색·두 번째 색 비율, blender_finish.py)와 원래 색 칸(_slots.json)을
    /// SoftLit 셰이더(_COATMASK)에 넘긴다. 품종에 없는 무늬(턱시도·젖소·삼색·카오스·흰 바탕 태비)는 코리안 숏헤어 몸에
    /// 그 무늬로 구운 털 그림(korean_shorthair__tuxedo_coat …)을 입힌다. 사진은 쓰지 않는다: 사진에서 읽은 색 몇 개뿐.
    /// </summary>
    public static class CatCoat
    {
        [Serializable] public class Spec { public string pattern, variant, @base, dark, white, second; public float whiteLevel; }
        [Serializable] class Slots { public string pattern, @base, dark, white, second; public float lighten; }

        public static Spec Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { var s = JsonUtility.FromJson<Spec>(json); return s != null && !string.IsNullOrEmpty(s.@base) ? s : null; }
            catch (Exception) { return null; }
        }

        /// <summary>몸 메시(털)에 사진 고양이 색을 입힌다. 털 마스크가 없는 예전 에셋이나 잘못된 값이면 아무것도 바꾸지 않고 false.</summary>
        public static bool Apply(Renderer body, string breed, string coatJson)
        {
            var spec = Parse(coatJson);
            if (body == null || spec == null) return false;
            string tag = string.IsNullOrEmpty(spec.variant) ? breed : breed + "__" + spec.variant;
            var mask = Resources.Load<Texture2D>("Art/Cats/" + tag + "_coatmask");
            var slotsText = Resources.Load<TextAsset>("Art/Cats/" + tag + "_slots");
            if (mask == null || slotsText == null) { tag = breed; mask = Resources.Load<Texture2D>("Art/Cats/" + tag + "_coatmask"); slotsText = Resources.Load<TextAsset>("Art/Cats/" + tag + "_slots"); }
            if (mask == null || slotsText == null) return false;
            var old = JsonUtility.FromJson<Slots>(slotsText.text);
            var m = body.material;   // (이 고양이만의 재질 사본)
            if (tag != breed) { var tex = Resources.Load<Texture2D>("Art/Cats/" + tag + "_coat"); if (tex) m.SetTexture("_BaseMap", tex); }
            m.SetTexture("_CoatMask", mask);
            // (줄무늬 털은 굽을 때 크림색 쪽으로 10 % 밝힌다 (catgen makeCoat 'ac'): 원래 색·새 색 모두 같게 밝혀 맞춘다)
            Color L(Color c) => old.lighten > 0 ? Color.Lerp(c.linear, Cream.linear, old.lighten).gamma : c;
            Color oB = Col(old.@base, Color.gray), oD = Col(old.dark, Color.black), oW = Col(old.white, Color.white), oS = Col(old.second, new Color(.9f, .6f, .3f));
            m.SetColor("_OldBase", L(oB)); m.SetColor("_OldDark", L(oD)); m.SetColor("_OldWhite", L(oW)); m.SetColor("_OldSecond", L(oS));
            m.SetColor("_NewBase", L(Col(spec.@base, oB))); m.SetColor("_NewDark", L(Col(spec.dark, oD)));
            m.SetColor("_NewWhite", L(Col(spec.white, oW))); m.SetColor("_NewSecond", L(Col(spec.second, oS)));
            m.EnableKeyword("_COATMASK"); m.SetFloat("_UseCoatMask", 1f);
            return true;
        }

        static readonly Color Cream = new Color(1f, 248f / 255f, 236f / 255f);
        static Color Col(string hex, Color fallback) => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
