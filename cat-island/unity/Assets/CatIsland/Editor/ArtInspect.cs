using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatIsland.EditorTools
{
    /// <summary>가져온 FBX의 구조를 로그로 출력한다 (뼈, 동작, 재질, 크기). 통합 작업 확인용.</summary>
    public static class ArtInspect
    {
        public static void Run()
        {
            AssetDatabase.Refresh();
            foreach (var path in new[]
            {
                "Assets/CatIsland/Art/Cats/korean_shorthair.fbx",
                "Assets/CatIsland/Art/Items/food_bowl/food_bowl.fbx",
                "Assets/CatIsland/Art/Items/cushion/cushion.fbx",
                "Assets/CatIsland/Art/Items/cat_tower_1/cat_tower_1.fbx",
            })
            {
                var sb = new StringBuilder();
                sb.AppendLine("[ArtInspect] ===== " + path);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { Debug.LogError("[ArtInspect] missing " + path); continue; }
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                sb.AppendLine($"animType={imp.animationType} motionNode='{imp.motionNodeName}' scale={imp.globalScale} useFileScale={imp.useFileScale} materialImport={imp.materialImportMode}");
                Dump(go.transform, sb, 0, 4);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    sb.AppendLine($"renderer {r.name} {r.GetType().Name} bounds={r.bounds.center:F3} size={r.bounds.size:F3}");
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        var tex = m.GetTexturePropertyNames().Select(n => n + ":" + (m.GetTexture(n) ? m.GetTexture(n).name : "-"));
                        sb.AppendLine($"   mat {m.name} shader={m.shader.name} {string.Join(",", tex)}");
                    }
                    if (r is SkinnedMeshRenderer smr)
                    {
                        var mesh = smr.sharedMesh;
                        sb.AppendLine($"   mesh tris={mesh.triangles.Length / 3} verts={mesh.vertexCount} bones={smr.bones.Length} root={smr.rootBone?.name} blend=[{string.Join(",", Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName))}] colors={mesh.colors32.Length > 0}");
                    }
                    else if (r.GetComponent<MeshFilter>())
                    {
                        var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                        sb.AppendLine($"   mesh tris={mesh.triangles.Length / 3} verts={mesh.vertexCount} uv={mesh.uv.Length > 0} colors={mesh.colors32.Length > 0}");
                    }
                }
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                sb.AppendLine("clips(" + clips.Length + "): " + string.Join(", ", clips.Select(c => $"{c.name}({c.length:F2}s)")));
                var jump = clips.FirstOrDefault(c => c.name.EndsWith("JumpUp"));
                if (jump)
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(jump).Where(b => b.path.EndsWith("Root") || b.path == "").Take(12))
                    {
                        var curve = AnimationUtility.GetEditorCurve(jump, b);
                        sb.AppendLine($"   jump curve '{b.path}' {b.propertyName} keys={curve.length} first={curve.Evaluate(0):F3} mid={curve.Evaluate(jump.length * 0.6f):F3} last={curve.Evaluate(jump.length):F3}");
                    }
                }
                Debug.Log(sb.ToString());
            }
        }

        static void Dump(Transform t, StringBuilder sb, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            sb.AppendLine(new string(' ', depth * 2) + t.name + $"  pos={t.localPosition:F3} rot={t.localEulerAngles:F0} scl={t.localScale:F2}");
            foreach (Transform c in t) Dump(c, sb, depth + 1, maxDepth);
        }
    }
}
