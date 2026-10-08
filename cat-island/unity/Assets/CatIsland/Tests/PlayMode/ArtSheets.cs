namespace CatIsland.Tests
{
    using System.Collections;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 그림 점검 시트 (DEV_LOG 0-7): 33품종 얼굴을 한 장에, 코리안 숏헤어의 눈 3 × 수염 3 조합을 한 장에. Shots/sheets/ 에 저장.
    /// 평소 테스트에서는 돌지 않는다 ([Explicit]): -testFilter CatIsland.Tests.ArtSheets
    /// </summary>
    [Explicit]
    public class ArtSheets
    {
        const int Tile = 300;
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots/sheets"));

        static (Camera cam, Light light) Stage()
        {
            var camGo = new GameObject("SheetCam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.98f, .96f, .9f); cam.fieldOfView = 24f;
            var lg = new GameObject("SheetLight"); var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.3f;
            lg.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.75f, .74f, .78f);
            return (cam, l);
        }

        static Texture2D Shot(Camera cam, CatRig rig)
        {
            var head = rig.Head.position;
            cam.transform.position = head + rig.transform.forward * 1.9f + Vector3.up * .15f;
            cam.transform.LookAt(head + Vector3.up * .05f);
            var rt = new RenderTexture(Tile, Tile, 24) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt; var t = new Texture2D(Tile, Tile, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0); t.Apply();
            RenderTexture.active = null; cam.targetTexture = null; rt.Release();
            return t;
        }

        static void Save(string name, Texture2D[] tiles, int cols)
        {
            int rows = (tiles.Length + cols - 1) / cols; var sheet = new Texture2D(cols * Tile, rows * Tile, TextureFormat.RGB24, false);
            var bg = new Color32(250, 245, 230, 255); var fill = new Color32[sheet.width * sheet.height]; for (int i = 0; i < fill.Length; i++) fill[i] = bg; sheet.SetPixels32(fill);
            for (int i = 0; i < tiles.Length; i++) sheet.SetPixels((i % cols) * Tile, (rows - 1 - i / cols) * Tile, Tile, Tile, tiles[i].GetPixels());
            sheet.Apply(); Directory.CreateDirectory(Dir); File.WriteAllBytes(Path.Combine(Dir, name + ".png"), sheet.EncodeToPNG());
        }

        static IEnumerator Cat(string breed, string eye, string whisker, System.Action<CatRig> got)
        {
            var go = new GameObject("SheetCat_" + breed); go.SetActive(false);
            var rig = go.AddComponent<CatRig>(); rig.breed = breed; rig.eyeStyle = eye; rig.whiskerStyle = whisker;
            go.SetActive(true); go.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            rig.Request(Posture.Sit);
            for (int i = 0; i < 50; i++) yield return null;   // (앉은 자세로)
            got(rig);
        }

        [UnityTest]
        public IEnumerator BreedFaces_And_EyeWhiskerGrid()
        {
            var (cam, light) = Stage();
            var breeds = CatIsland.Game.Catalog.Breeds; var tiles = new Texture2D[breeds.Length];
            for (int i = 0; i < breeds.Length; i++)
            {
                CatRig rig = null; yield return Cat(breeds[i].id, "", "", r => rig = r);
                tiles[i] = Shot(cam, rig); Object.Destroy(rig.gameObject); yield return null;
            }
            Save("breeds", tiles, 7);
            var eyes = new[] { "dark", "iris", "rim" }; var whiskers = new[] { "short", "long", "dots" }; var grid = new Texture2D[9];
            for (int e = 0; e < 3; e++) for (int w = 0; w < 3; w++)
                {
                    CatRig rig = null; yield return Cat("korean_shorthair", eyes[e], whiskers[w], r => rig = r);
                    grid[e * 3 + w] = Shot(cam, rig); Object.Destroy(rig.gameObject); yield return null;
                }
            Save("eye_whisker", grid, 3);
            Object.Destroy(cam.gameObject); Object.Destroy(light.gameObject);
            Assert.Pass("Shots/sheets/breeds.png, eye_whisker.png");
        }
    }
}
