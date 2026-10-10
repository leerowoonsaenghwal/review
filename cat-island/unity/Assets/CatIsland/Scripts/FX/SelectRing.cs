using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// '지금 고양이' 표시: 고른 고양이 발밑에 분홍 고리가 퍼졌다가 1.6초에 걸쳐 작아지며 사라진다 (고양이를 따라 움직인다).
    /// </summary>
    public class SelectRing : MonoBehaviour
    {
        static SelectRing inst;
        Transform target; float t = 9f; MeshRenderer mr; Material mat;

        public static void Show(Transform cat)
        {
            if (!inst)
            {
                var go = new GameObject("SelectRing"); inst = go.AddComponent<SelectRing>();
                // (납작한 고리: 안 0.30 m · 밖 0.40 m, 땅 위 2 cm)
                var mesh = MeshFactory.Lathe("selectring", new System.Collections.Generic.List<Vector2> { new Vector2(.40f, .02f), new Vector2(.30f, .02f) }, 48);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; inst.mr = go.AddComponent<MeshRenderer>();
                inst.mat = new Material(Materials.Soft(Palette.Hex("FF7F9F"))); inst.mat.SetFloat("_Emission", .7f); inst.mr.sharedMaterial = inst.mat;
                inst.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            inst.target = cat; inst.t = 0f; inst.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            t += Time.unscaledDeltaTime;
            if (!target || t > 1.6f) { gameObject.SetActive(false); return; }
            float grow = Mathf.SmoothStep(0f, 1f, t / .25f), fade = 1f - Mathf.SmoothStep(0f, 1f, (t - 1f) / .6f);
            var p = target.position; transform.position = new Vector3(p.x, Mathf.Max(p.y, FloorTiles.HeightAt(p)) + .005f, p.z);
            transform.localScale = Vector3.one * (.6f + .4f * grow) * Mathf.Lerp(.7f, 1f, fade);
            if (fade <= .02f) gameObject.SetActive(false);
        }
    }
}

namespace CatIsland
{
    /// <summary>바닥을 누른 자리: 크림색 고리가 0.5초 동안 퍼지며 사라진다 (고양이가 그리로 간다는 표시, 소리 대신).</summary>
    public class TapRipple : MonoBehaviour
    {
        static TapRipple inst;
        float t = 9f; Vector3 at; Material mat;

        public static void Show(Vector3 world)
        {
            if (!inst)
            {
                var go = new GameObject("TapRipple"); inst = go.AddComponent<TapRipple>();
                var mesh = MeshFactory.Lathe("tapripple", new System.Collections.Generic.List<Vector2> { new Vector2(.20f, .015f), new Vector2(.16f, .015f) }, 40);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>();
                inst.mat = new Material(Materials.Soft(Palette.Hex("FBF6E6"))); inst.mat.SetFloat("_Emission", .85f); mr.sharedMaterial = inst.mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            inst.at = new Vector3(world.x, FloorTiles.HeightAt(world) + .006f, world.z); inst.t = 0f; inst.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            t += Time.unscaledDeltaTime;
            if (t > .5f) { gameObject.SetActive(false); return; }
            float u = t / .5f;
            transform.position = at; transform.localScale = new Vector3(1f + 1.6f * u, 1f, 1f + 1.6f * u) * (1f - .3f * u * u);
        }
    }
}
