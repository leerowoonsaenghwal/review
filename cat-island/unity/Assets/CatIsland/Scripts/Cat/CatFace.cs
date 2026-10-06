using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 눈 모양·수염 고르기 (기획서 3부: 진한 눈 / 색 눈 / 테두리 눈, 짧은 / 긴 / 수염 점).
    /// 고양이 파일의 얼굴에는 모든 눈·수염이 '재질 이름__eye_dark', '__wh_long' 같은 따로 된 조각으로 들어 있다 (catmodel.js faceVariants).
    /// 고른 조각만 원래 재질 조각에 합쳐 새 메시를 만들고 나머지는 뺀다: 화면에 그리는 횟수는 그대로.
    /// 같은 품종·같은 선택은 메시를 함께 쓴다.
    /// </summary>
    public static class CatFace
    {
        public static readonly string[] Eyes = { "dark", "iris", "rim" };
        public static readonly string[] Whiskers = { "short", "long", "dots", "marks" };
        static readonly Dictionary<string, (Mesh mesh, Material[] mats)> cache = new Dictionary<string, (Mesh, Material[])>();

        /// <summary>품종 기본 눈 모양·수염 (고양이 파일의 clips.json face → _info.json). 모르면 dark / short.</summary>
        public static (string eye, string whisker) Defaults(string breed)
        {
            var t = Resources.Load<TextAsset>("Art/Cats/" + breed + "_info");
            var info = t ? JsonUtility.FromJson<CatRig.CatArtInfo>(t.text) : null;
            return (string.IsNullOrEmpty(info?.faceEye) ? "dark" : info.faceEye, string.IsNullOrEmpty(info?.faceWhisker) ? "short" : info.faceWhisker);
        }

        static string BaseName(Material m) => m ? m.name.Replace(" (Instance)", "") : "";

        /// <summary>이 얼굴에 들어 있는 고를 수 있는 조각 이름 (예: eye_dark, wh_long).</summary>
        public static IEnumerable<string> Variants(SkinnedMeshRenderer face) =>
            face.sharedMaterials.Select(BaseName).Where(n => n.Contains("__")).Select(n => n.Substring(n.IndexOf("__") + 2)).Distinct();

        /// <summary>eye / whisker 가 비었거나 이 얼굴에 없으면 기본(fallbackEye / fallbackWhisker, 그것도 없으면 첫 번째)을 쓴다.</summary>
        public static void Apply(SkinnedMeshRenderer face, string key, string eye, string whisker, string fallbackEye, string fallbackWhisker)
        {
            if (face == null) return;
            var vars = Variants(face).ToList();
            if (vars.Count == 0) return;   // (예전 에셋: 하나만 들어 있음)
            string Pick(string prefix, string want, string fb)
            {
                var have = vars.Where(v => v.StartsWith(prefix)).ToList();
                if (have.Count == 0) return null;
                foreach (var w in new[] { want, fb }) if (!string.IsNullOrEmpty(w) && have.Contains(prefix + w)) return prefix + w;
                return have[0];
            }
            var keep = new HashSet<string> { Pick("eye_", eye, fallbackEye), Pick("wh_", whisker, fallbackWhisker) };
            string ck = key + "|" + string.Join(",", keep.OrderBy(k => k));
            if (!cache.TryGetValue(ck, out var built) || built.mesh == null)
            {
                built = Build(face, keep);
                cache[ck] = built;
            }
            face.sharedMesh = built.mesh;
            face.sharedMaterials = built.mats;
        }

        static (Mesh, Material[]) Build(SkinnedMeshRenderer face, HashSet<string> keep)
        {
            var src = face.sharedMesh; var mats = face.sharedMaterials;
            var baseIdx = new List<int>();                      // 남길 원래 조각 (이름에 __ 가 없는 것)
            for (int i = 0; i < mats.Length && i < src.subMeshCount; i++) if (!BaseName(mats[i]).Contains("__")) baseIdx.Add(i);
            var tris = baseIdx.Select(i => new List<int>(src.GetTriangles(i))).ToList();
            for (int i = 0; i < mats.Length && i < src.subMeshCount; i++)
            {
                var n = BaseName(mats[i]); int cut = n.IndexOf("__"); if (cut < 0) continue;
                if (!keep.Contains(n.Substring(cut + 2))) continue;
                string owner = n.Substring(0, cut);
                int slot = baseIdx.FindIndex(b => BaseName(mats[b]) == owner || BaseName(mats[b]).EndsWith("_" + owner));
                if (slot < 0) { baseIdx.Add(i); tris.Add(new List<int>()); slot = baseIdx.Count - 1; }
                tris[slot].AddRange(src.GetTriangles(i));
            }
            var m = Object.Instantiate(src); m.name = src.name + "_picked";
            m.subMeshCount = baseIdx.Count;
            for (int s = 0; s < baseIdx.Count; s++) m.SetTriangles(tris[s], s, false);
            m.RecalculateBounds();
            return (m, baseIdx.Select(i => mats[i]).ToArray());
        }
    }
}
