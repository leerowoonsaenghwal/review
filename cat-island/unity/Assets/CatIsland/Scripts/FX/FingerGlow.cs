using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 쓰다듬을 때 손은 화면에 없다: 손가락이 고양이에 닿은 곳에만 은은한 빛 (ART_DIRECTION 10-4). 손을 떼면 천천히 사라진다.
    /// </summary>
    public class FingerGlow : MonoBehaviour
    {
        static FingerGlow inst;
        Material mat; float lastSeen = -9f, alpha; Vector3 target;

        public static void Show(Vector3 worldPos)
        {
            if (!inst) inst = Create();
            inst.target = worldPos; inst.lastSeen = Time.time;
        }

        static FingerGlow Create()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "FingerGlow";
            Destroy(go.GetComponent<Collider>());
            var g = go.AddComponent<FingerGlow>();
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "glow" };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float d = new Vector2((x + .5f) / 32f - 1f, (y + .5f) / 32f - 1f).magnitude, a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    tex.SetPixel(x, y, new Color(1f, .97f, .9f, a));
                }
            tex.Apply();
            g.mat = Materials.NewBillboard(tex); go.GetComponent<MeshRenderer>().sharedMaterial = g.mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * .16f;
            return g;
        }

        void LateUpdate()
        {
            bool on = Time.time - lastSeen < .12f;
            alpha = Mathf.MoveTowards(alpha, on ? .5f : 0f, Time.deltaTime * (on ? 4f : 1.5f));
            var cam = Camera.main;
            if (on) transform.position = Vector3.Lerp(transform.position, target, alpha < .05f ? 1f : 1f - Mathf.Exp(-20f * Time.deltaTime));
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            GetComponent<MeshRenderer>().enabled = alpha > .005f;
        }
    }
}
