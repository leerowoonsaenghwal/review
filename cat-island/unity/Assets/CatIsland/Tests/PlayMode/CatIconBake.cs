namespace CatIsland.Tests
{
    using System.Collections;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 고양이 목록·도감 그림: 품종마다 앉은 얼굴(머리와 어깨)을 정면에서, 배경 투명 256 px → Resources/CatIcons/&lt;품종&gt;.png.
    /// 평소 테스트에서는 돌지 않는다: -testFilter CatIsland.Tests.CatIconBake
    /// </summary>
    [Explicit]
    public class CatIconBake
    {
        const int Size = 256;
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "CatIsland/Resources/CatIcons"));

        [UnityTest]
        public IEnumerator BakeAll()
        {
            Directory.CreateDirectory(Dir);
            var camGo = new GameObject("IconCam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); cam.fieldOfView = 22f;
            var lg = new GameObject("IconLight"); var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.25f; lg.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.78f, .76f, .8f);
            int n = 0;
            foreach (var b in CatIsland.Game.Catalog.Breeds)
            {
                var go = new GameObject("IconCat_" + b.id); go.SetActive(false); go.transform.position = new Vector3(200f, 0f, 0f);
                var rig = go.AddComponent<CatRig>(); rig.breed = b.id; go.SetActive(true);
                rig.Request(Posture.Sit);
                for (int i = 0; i < 50; i++) yield return null;
                var head = rig.Head.position;
                cam.transform.position = head + go.transform.forward * 1.75f + Vector3.up * .1f;
                cam.transform.LookAt(head + Vector3.up * .02f);
                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); tex.Apply(); RenderTexture.active = null;
                cam.targetTexture = null; rt.Release();
                File.WriteAllBytes(Path.Combine(Dir, b.id + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex); Object.Destroy(go); n++;
                yield return null;
            }
            Object.Destroy(camGo); Object.Destroy(lg);
            Assert.AreEqual(CatIsland.Game.Catalog.Breeds.Length, n);
        }
    }
}
