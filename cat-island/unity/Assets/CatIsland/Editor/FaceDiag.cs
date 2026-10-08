using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CatIsland.EditorTools
{
    /// <summary>얼굴 점검: 눈 감기(Blink)·입 벌림을 켰을 때 조각(재질)마다 정점이 얼마나 움직이는지. 수염이 움직이면 표정 모양이 잘못 붙은 것.</summary>
    public static class FaceDiag
    {
        public static void Run()
        {
            foreach (var breed in new[] { "korean_shorthair", "persian" })
            {
                var prefab = Resources.Load<GameObject>("Art/Cats/" + breed);
                var inst = Object.Instantiate(prefab);
                var face = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.blendShapeCount > 0);
                var m = face.sharedMesh;
                var a = new Mesh(); var b = new Mesh();
                for (int s = 0; s < m.blendShapeCount; s++)
                {
                    for (int k = 0; k < m.blendShapeCount; k++) face.SetBlendShapeWeight(k, 0);
                    face.BakeMesh(a);
                    face.SetBlendShapeWeight(s, 100);
                    face.BakeMesh(b);
                    var va = a.vertices; var vb = b.vertices;
                    for (int sm = 0; sm < m.subMeshCount; sm++)
                    {
                        var tri = m.GetTriangles(sm).Distinct().ToArray();
                        float mx = 0; foreach (var i in tri) mx = Mathf.Max(mx, (va[i] - vb[i]).magnitude);
                        Debug.Log($"[FaceDiag] {breed} shape {m.GetBlendShapeName(s)} submesh {sm} ({face.sharedMaterials[sm].name}) verts {tri.Length} maxMove {mx * 1000:F1} mm");
                    }
                }
                Debug.Log($"[FaceDiag] {breed} mesh verts {m.vertexCount} readable {m.isReadable}");
                Object.DestroyImmediate(inst);
            }
        }
    }
}
