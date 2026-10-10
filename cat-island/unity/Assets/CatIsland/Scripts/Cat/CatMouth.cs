using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatIsland
{
    /// <summary>
    /// 벌린 입 (야옹·웃음·하품): 새 그림체(큰 머리)에서는 얼굴 모양 MouthOpen 의 입 안이 주둥이 털 뒤에 있어 화면에 안 보이고,
    /// 'w' 입선만 안으로 들어가 입이 사라진 것처럼 보인다. 그래서 'w' 입선 자리의 주둥이 겉면에 입 모양(진한 타원 + 혀)을 붙이고
    /// 벌린 만큼 아래로 키운다. 자리는 고양이 파일의 MouthOpen 에서 읽는다 (입 안은 한 점에 모여 있고, 'w' 는 겉면에 퍼져 있다).
    /// </summary>
    public class CatMouth : MonoBehaviour
    {
        Transform shape; Vector2 full;   // (다 벌렸을 때 너비·높이, 뼈 기준 길이)

        public static CatMouth Build(SkinnedMeshRenderer face, int mouthIdx, SkinnedMeshRenderer body, Transform cat)
        {
            var mesh = face.sharedMesh; if (mouthIdx < 0 || !mesh.isReadable) return null;
            var pos = mesh.vertices; var d = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(mouthIdx, mesh.GetBlendShapeFrameCount(mouthIdx) - 1, d, null, null);
            // 움직이는 점 중 가장 많이 겹친 자리 = 접혀 숨은 입 안, 나머지 = 'w' 입선
            var count = new Dictionary<Vector3Int, int>(); var moved = new List<int>();
            Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 10000f);
            for (int i = 0; i < d.Length; i++) if (d[i].sqrMagnitude > 1e-8f) { moved.Add(i); var k = Key(pos[i]); count[k] = count.TryGetValue(k, out var c) ? c + 1 : 1; }
            if (moved.Count == 0) return null;
            Vector3Int hidden = default; int best = 0; foreach (var kv in count) if (kv.Value > best) { best = kv.Value; hidden = kv.Key; }
            var w = moved.FindAll(i => Key(pos[i]) != hidden); if (w.Count < 4) return null;
            Vector3 c0 = Vector3.zero, n0; foreach (var i in w) c0 += pos[i];
            c0 /= w.Count;
            // 위·앞 방향: 고양이 몸 기준 (만들 때 고양이는 서 있다)
            var upW = face.transform.InverseTransformDirection(cat.up).normalized;
            n0 = Vector3.ProjectOnPlane(face.transform.InverseTransformDirection(cat.forward), upW).normalized;
            Vector3 up = upW, right = Vector3.Cross(up, n0).normalized;
            float span = 0f, low = 0f; foreach (var i in w) { var o = pos[i] - c0; span = Mathf.Max(span, Mathf.Abs(Vector3.Dot(o, right))); low = Mathf.Min(low, Vector3.Dot(o, up)); }
            // 입선 점들이 가장 많이 붙은 뼈 (보통 머리)
            var bw = mesh.boneWeights; var score = new float[face.bones.Length];
            foreach (var i in w) { var b = bw[i]; score[b.boneIndex0] += b.weight0; score[b.boneIndex1] += b.weight1; score[b.boneIndex2] += b.weight2; score[b.boneIndex3] += b.weight3; }
            int bi = 0; for (int i = 1; i < score.Length; i++) if (score[i] > score[bi]) bi = i;
            var bone = face.bones[bi]; var bind = mesh.bindposes[bi];

            var go = new GameObject("Mouth"); go.transform.SetParent(bone, false);
            var top = c0 + up * (low * .4f);   // ('w' 가운데 조금 아래)
            // 주둥이 털 겉면까지 앞으로: 입 자리 바로 둘레의 몸·얼굴 점 중 가장 앞에 있는 깊이 + 조금
            float front = Vector3.Dot(top, n0);
            void Surf(Vector3 p) { var o = p - top; if ((o - n0 * Vector3.Dot(o, n0)).sqrMagnitude < span * span * .12f) front = Mathf.Max(front, Vector3.Dot(p, n0)); }
            if (body && body.sharedMesh && body.sharedMesh.isReadable) foreach (var p in body.sharedMesh.vertices) Surf(face.transform.InverseTransformPoint(body.transform.TransformPoint(p)));
            for (int i = 0; i < pos.Length; i++) if (d[i].sqrMagnitude <= 1e-8f) Surf(pos[i]);
            top += n0 * (front - Vector3.Dot(top, n0) + span * .02f);
            go.transform.localPosition = bind.MultiplyPoint3x4(top);
            go.transform.localRotation = Quaternion.LookRotation(bind.MultiplyVector(-n0), bind.MultiplyVector(up));
            var m = go.AddComponent<CatMouth>();
            float unit = bind.MultiplyVector(right).magnitude;
            m.full = new Vector2(span * 1.8f, span * 2.4f) * unit;
            m.shape = new GameObject("Shape").transform; m.shape.SetParent(go.transform, false);
            Disc(m.shape, "Inside", new Color(.36f, .14f, .16f), Vector2.zero, Vector2.one, 0f);
            Disc(m.shape, "Tongue", new Color(.93f, .52f, .58f), new Vector2(0f, -.3f), new Vector2(.62f, .42f), -.02f);
            m.Set(0f);
            return m;
        }

        /// <summary>벌린 정도 0~1. 조금 벌리면 옆으로 넓은 웃는 입, 많이 벌리면 동그랗게 큰 입.</summary>
        public void Set(float open)
        {
            bool on = open > .04f; if (shape.gameObject.activeSelf != on) shape.gameObject.SetActive(on);
            if (!on) return;
            float h = full.y * Mathf.Lerp(.3f, 1f, open) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .2f, open));   // (조금만 벌려도 보이는 작은 입)
            shape.localScale = new Vector3(full.x * Mathf.Lerp(.75f, 1f, open), h, full.x);
            shape.localPosition = new Vector3(0f, -full.y * .12f - h * .5f, 0f);   // (위 가장자리는 'w' 바로 아래에 두고 아래로 벌린다)
        }

        // 지름 1 짜리 납작한 타원 (가장자리가 살짝 뒤로 휘어 둥근 주둥이에 붙는다)
        static void Disc(Transform parent, string name, Color color, Vector2 at, Vector2 size, float z)
        {
            const int N = 28; var v = new Vector3[N + 1]; var tri = new int[N * 3];
            v[0] = new Vector3(at.x, at.y, z);
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N; var p = new Vector2(Mathf.Cos(a) * size.x * .5f, Mathf.Sin(a) * size.y * .5f) + at;
                v[i + 1] = new Vector3(p.x, p.y, z + .22f);
                tri[i * 3] = 0; tri[i * 3 + 1] = (i + 1) % N + 1; tri[i * 3 + 2] = i + 1;   // (밖(-z)에서 보이는 면)
            }
            var mesh = new Mesh { name = "Mouth" + name }; mesh.vertices = v; mesh.triangles = tri; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Materials.Soft(color); r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
