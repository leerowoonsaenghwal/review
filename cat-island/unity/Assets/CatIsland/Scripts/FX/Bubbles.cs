using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>고양이 머리 위 말풍선. 상태, 요청, 안내를 글자 없이 아이콘으로 보여 준다.</summary>
    public class StatusBubble : MonoBehaviour
    {
        Transform root;
        Material bubbleMat, iconMat;
        float scale, scaleVel, shown;
        public Icon? Current { get; private set; }
        float t;

        public static StatusBubble Create(Transform anchor)
        {
            var go = new GameObject("StatusBubble");
            go.transform.SetParent(anchor, false);
            var b = go.AddComponent<StatusBubble>();
            b.Build();
            return b;
        }

        void Build()
        {
            root = new GameObject("Root").transform;
            root.SetParent(transform, false);
            bubbleMat = Materials.NewBillboard(IconPainter.Get(Icon.Bubble));
            bubbleMat.renderQueue = 3100;
            iconMat = Materials.NewBillboard(IconPainter.Get(Icon.Heart));
            iconMat.renderQueue = 3101;
            Quad(root, "Bg", bubbleMat, Vector3.zero, 0.36f);
            Quad(root, "Icon", iconMat, new Vector3(0f, 0.03f, -0.01f), 0.25f);
            root.localScale = Vector3.zero;
        }

        static void Quad(Transform parent, string name, Material m, Vector3 pos, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Show(Icon icon)
        {
            if (Current == icon) return;
            Current = icon;
            iconMat.SetTexture("_MainTex", IconPainter.Get(icon));
            shown = 1f;
            scaleVel += 9f;
        }

        public void Hide() { Current = null; shown = 0f; }

        void LateUpdate()
        {
            t += Time.deltaTime;
            float k = 220f, d = 16f;
            scaleVel += (-(scale - shown) * k - scaleVel * d) * Time.deltaTime;
            scale += scaleVel * Time.deltaTime;
            float s = Mathf.Max(0f, scale);
            root.localScale = Vector3.one * s;
            root.localPosition = new Vector3(0f, Mathf.Sin(t * 2.4f) * 0.02f, 0f);
            var cam = Camera.main;
            if (cam) transform.rotation = cam.transform.rotation;
        }
    }

    /// <summary>하트, 음표, 반짝이가 떠오르는 효과.</summary>
    public class FxPool : MonoBehaviour
    {
        class P { public Transform t; public Material m; public Vector3 v; public float life, age, size, spin; public bool active; }
        readonly List<P> pool = new List<P>();
        public static FxPool Instance { get; private set; }
        public int ActiveCount { get; private set; }

        void Awake() { Instance = this; }

        public void Burst(Icon icon, Vector3 pos, int count, float spread = 0.15f, float size = 0.17f)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Get();
                p.m.SetTexture("_MainTex", IconPainter.Get(icon));
                p.t.position = pos + new Vector3(Random.Range(-spread, spread), Random.Range(0f, spread * 0.5f), Random.Range(-spread, spread));
                p.v = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.45f, 0.7f), Random.Range(-0.1f, 0.1f));
                p.life = Random.Range(0.9f, 1.3f);
                p.age = -i * 0.07f;
                p.size = size * Random.Range(0.8f, 1.15f);
                p.spin = Random.Range(-25f, 25f);
                p.t.localScale = Vector3.zero;
                p.active = true;
                p.t.gameObject.SetActive(true);
            }
        }

        P Get()
        {
            foreach (var p in pool) if (!p.active) return p;
            var go = new GameObject("Fx");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad();
            var r = go.AddComponent<MeshRenderer>();
            var m = Materials.NewBillboard(IconPainter.Get(Icon.Heart));
            m.renderQueue = 3110;
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var np = new P { t = go.transform, m = m };
            pool.Add(np);
            return np;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            float dt = Time.deltaTime;
            int n = 0;
            foreach (var p in pool)
            {
                if (!p.active) continue;
                p.age += dt;
                if (p.age < 0f) { p.t.localScale = Vector3.zero; n++; continue; }
                if (p.age >= p.life) { p.active = false; p.t.gameObject.SetActive(false); continue; }
                n++;
                float k = p.age / p.life;
                p.t.position += p.v * dt;
                p.v *= 1f - dt * 1.2f;
                float pop = k < 0.15f ? Mathf.SmoothStep(0f, 1.2f, k / 0.15f) : Mathf.Lerp(1.2f, 1f, Mathf.Clamp01((k - 0.15f) * 4f));
                p.t.localScale = Vector3.one * p.size * pop;
                if (cam) p.t.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, p.spin * Mathf.Sin(p.age * 4f));
                p.m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0.6f, 1f, k)));
            }
            ActiveCount = n;
        }
    }
}
