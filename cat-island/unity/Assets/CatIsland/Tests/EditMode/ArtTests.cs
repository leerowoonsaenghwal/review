using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CatIsland.Tests
{
    /// <summary>
    /// 고양이 파일 점검 (모든 품종): 얼굴 조각이 제대로인가. 빈 재질 조각이 '인덱스 없는 프리미티브'로 나가면 얼굴 전체에 쓰레기 삼각형이
    /// 깔리고 수염이 망가졌다 (2026-10-08). 눈 감기(Blink)는 눈 조각만 움직여야 하고, 수염 조각은 그대로여야 한다.
    /// </summary>
    public class ArtTests
    {
        [Test]
        public void EveryCatFace_PartsAreWhole_BlinkMovesOnlyEyes()
        {
            var cats = Resources.LoadAll<GameObject>("Art/Cats");
            Assert.GreaterOrEqual(cats.Length, 30, "고양이 프리팹");
            foreach (var prefab in cats)
            {
                var inst = Object.Instantiate(prefab);
                try
                {
                    var face = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh && r.sharedMesh.blendShapeCount > 0);
                    Assert.NotNull(face, prefab.name + ": 얼굴");
                    var m = face.sharedMesh; int blink = m.GetBlendShapeIndex("Blink");
                    for (int sm = 0; sm < m.subMeshCount; sm++)
                    {
                        int verts = m.GetTriangles(sm).Distinct().Count();
                        Assert.Less(verts, m.vertexCount * .6f, $"{prefab.name}: 조각 {face.sharedMaterials[sm].name} 이 얼굴 정점 대부분을 덮음 (쓰레기 삼각형)");
                    }
                    if (blink < 0) continue;
                    var a = new Mesh(); var b = new Mesh();
                    face.SetBlendShapeWeight(blink, 0); face.BakeMesh(a);
                    face.SetBlendShapeWeight(blink, 100); face.BakeMesh(b);
                    var va = a.vertices; var vb = b.vertices;
                    for (int sm = 0; sm < m.subMeshCount; sm++)
                    {
                        var name = face.sharedMaterials[sm].name;
                        if (!name.Contains("__wh_")) continue;
                        float mx = m.GetTriangles(sm).Distinct().Max(i => (va[i] - vb[i]).magnitude);
                        Assert.Less(mx, .001f, $"{prefab.name}: 눈을 감으면 수염({name})이 움직임");
                        Assert.Greater(m.GetIndexCount(sm), 300, $"{prefab.name}: 수염 조각({name}) 삼각형이 너무 적음");
                    }
                }
                finally { Object.DestroyImmediate(inst); }
            }
        }
    }
}
