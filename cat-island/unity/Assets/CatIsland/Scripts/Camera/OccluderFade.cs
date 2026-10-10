using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 고양이를 가리는 용품은 점무늬로 비친다: 카메라와 고양이 사이에 놓인 용품(숨숨집·캣타워 등)이 고양이를 가리면
    /// 그 용품만 SoftLit 의 _FADE 재질로 바꿔 40 %만 그린다. 고양이가 그 용품 안에 있으면(숨숨집에서 쉬기) 그대로 둔다.
    /// 다른 물체는 원래 재질 그대로라 그리기 비용이 늘지 않는다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OccluderFade : MonoBehaviour
    {
        public const float Shown = .4f;          // (가릴 때 남기는 비율)
        const float Every = .1f, Speed = 5f;     // (가림 검사 간격 s, 비치기·돌아오기 속도 /s)

        /// <summary>가림 검사에 넣을 장면 용품 (그릇·방석·캣타워: ItemTag 가 없는 것).</summary>
        public readonly List<Transform> extra = new List<Transform>();

        class Fader
        {
            public Transform root; public Renderer[] rs; public Material[][] shared, faded;
            public float fade = 1f; public bool want;
        }
        readonly Dictionary<Transform, Fader> faders = new Dictionary<Transform, Fader>();
        readonly List<Transform> items = new List<Transform>();
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        Camera cam; float next;

        void Awake() { cam = GetComponent<Camera>(); }

        void LateUpdate()
        {
            if (Time.unscaledTime >= next) { next = Time.unscaledTime + Every; Check(); }
            float dt = Time.unscaledDeltaTime;
            foreach (var f in faders.Values)
            {
                if (f == null || !f.root) continue;
                float goal = f.want ? Shown : 1f;
                if (Mathf.Approximately(f.fade, goal)) continue;
                f.fade = Mathf.MoveTowards(f.fade, goal, Speed * dt);
                Apply(f);
            }
        }

        void Check()
        {
            items.Clear();
            foreach (var t in ItemTag.All) if (t) items.Add(t.transform);
            foreach (var t in extra) if (t && t.gameObject.activeInHierarchy) items.Add(t);
            foreach (var f in faders.Values) if (f != null) f.want = false;
            Vector3 eye = transform.position;
            foreach (var cat in CatBrain.All)
            {
                if (!cat || !cat.isActiveAndEnabled) continue;
                Vector3 c = cat.transform.position;
                for (int k = 0; k < 2; k++)   // (몸통, 머리)
                {
                    var target = c + Vector3.up * (k == 0 ? .25f : .55f);
                    var ray = new Ray(eye, target - eye); float toCat = Vector3.Distance(eye, target);
                    foreach (var it in items)
                    {
                        var f = Get(it); if (f == null) continue;
                        var b = Bounds(f); if (!b.IntersectRay(ray, out float d) || d > toCat - .15f) continue;
                        if (c.x > b.min.x && c.x < b.max.x && c.z > b.min.z && c.z < b.max.z) continue;   // (고양이가 그 용품 안·위에 있다)
                        f.want = true;
                    }
                }
            }
            // (사라진 용품 정리)
            List<Transform> gone = null;
            foreach (var kv in faders) if (!kv.Key) (gone ??= new List<Transform>()).Add(kv.Key);
            if (gone != null) foreach (var k in gone) faders.Remove(k);
        }

        Fader Get(Transform t)
        {
            if (faders.TryGetValue(t, out var f)) return f;
            var rs = t.GetComponentsInChildren<Renderer>();
            var keep = new List<Renderer>();
            foreach (var r in rs) if (r is MeshRenderer || r is SkinnedMeshRenderer) keep.Add(r);
            if (keep.Count == 0) { faders[t] = null; return null; }
            f = new Fader { root = t, rs = keep.ToArray() };
            f.shared = new Material[f.rs.Length][]; f.faded = new Material[f.rs.Length][];
            for (int i = 0; i < f.rs.Length; i++) f.shared[i] = f.rs[i].sharedMaterials;
            faders[t] = f; return f;
        }

        static Bounds Bounds(Fader f)
        {
            var b = f.rs[0].bounds; for (int i = 1; i < f.rs.Length; i++) if (f.rs[i]) b.Encapsulate(f.rs[i].bounds);
            return b;
        }

        static void Apply(Fader f)
        {
            bool on = f.fade < .999f;
            for (int i = 0; i < f.rs.Length; i++)
            {
                var r = f.rs[i]; if (!r) continue;
                if (!on) { r.sharedMaterials = f.shared[i]; continue; }
                if (f.faded[i] == null)
                {
                    // (SoftLit 재질만 비치는 사본을 만든다: 사본은 SRP 배치를 그대로 쓴다)
                    var src = f.shared[i]; var dst = new Material[src.Length];
                    for (int k = 0; k < src.Length; k++)
                    {
                        if (src[k] && src[k].shader && src[k].shader.name == "CatIsland/SoftLit") { dst[k] = new Material(src[k]) { name = src[k].name + " (fade)" }; dst[k].EnableKeyword("_FADE"); }
                        else dst[k] = src[k];
                    }
                    f.faded[i] = dst;
                }
                foreach (var m in f.faded[i]) if (m && m.IsKeywordEnabled("_FADE")) m.SetFloat(FadeId, f.fade);
                r.sharedMaterials = f.faded[i];
            }
        }

        /// <summary>테스트용: 이 용품이 지금 비치고 있는가.</summary>
        public bool IsFaded(Transform item) => faders.TryGetValue(item, out var f) && f != null && f.fade < .999f;

        void OnDestroy()
        {
            foreach (var f in faders.Values)
            {
                if (f == null) continue;
                for (int i = 0; i < f.rs.Length; i++) { if (f.rs[i]) f.rs[i].sharedMaterials = f.shared[i]; if (f.faded[i] != null) foreach (var m in f.faded[i]) if (m && m.name.EndsWith(" (fade)")) Destroy(m); }
            }
        }
    }
}
