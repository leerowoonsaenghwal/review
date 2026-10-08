namespace CatIsland.Tests
{
    using System.Collections;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 상점·가방 그림: 용품마다 실제 모델을 비스듬히 위에서 찍은 작은 그림 (배경 투명, 256 px) → Resources/ItemIcons/&lt;모델&gt;.png.
    /// 모델이 없는 것(먹이 등)은 아이콘 그대로. 평소 테스트에서는 돌지 않는다: -testFilter CatIsland.Tests.ItemIconBake
    /// </summary>
    [Explicit]
    public class ItemIconBake
    {
        const int Size = 256;
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "CatIsland/Resources/ItemIcons"));

        [UnityTest]
        public IEnumerator BakeAll()
        {
            Directory.CreateDirectory(Dir);
            var camGo = new GameObject("IconCam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); cam.fieldOfView = 20f; cam.nearClipPlane = .05f; cam.farClipPlane = 50f;
            var lg = new GameObject("IconLight"); var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.25f; lg.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.78f, .76f, .8f);
            int n = 0;
            foreach (var model in CatIsland.Game.Catalog.Items.Select(d => d.model).Distinct())
            {
                var prefab = Resources.Load<GameObject>("Art/Items/" + model); if (!prefab) continue;
                var go = Object.Instantiate(prefab, new Vector3(100f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
                yield return null;
                var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { Object.Destroy(go); continue; }
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float radius = b.extents.magnitude;
                var dir = Quaternion.Euler(28f, 155f, 0f) * Vector3.back;   // (비스듬히 위, 살짝 옆)
                float dist = radius / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * 1.02f;
                cam.transform.position = b.center - dir * dist; cam.transform.LookAt(b.center);
                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); tex.Apply(); RenderTexture.active = null;
                cam.targetTexture = null; rt.Release();
                File.WriteAllBytes(Path.Combine(Dir, model + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex); Object.Destroy(go); n++;
                yield return null;
            }
            Object.Destroy(camGo); Object.Destroy(lg);
            Assert.Greater(n, 40, "item icons baked");
        }
    }
}
