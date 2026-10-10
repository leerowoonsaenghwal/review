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
