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

        /// <summary>자세 바꾸기 필름 (발라당 눕기·일어나기, 자다 일어나기): 비스듬히 옆에서 0.12초마다 → Shots/sheets/posture_*.png</summary>
        [UnityTest]
        public IEnumerator PostureFilm()
        {
            var (cam, light) = Stage(); cam.fieldOfView = 30f;
            foreach (var id in new[] { "korean_shorthair", "munchkin" })
            {
                CatRig rig = null; yield return Cat(id, "", "", r => rig = r);
                cam.transform.position = rig.transform.position + Quaternion.Euler(0f, 55f, 0f) * Vector3.forward * 2.6f + Vector3.up * 1.0f; cam.transform.LookAt(rig.transform.position + Vector3.up * .25f);
                IEnumerator Film(string name, Posture from, Posture to, float secs)
                {
                    rig.Request(from); for (float t = 0; t < 4f && (rig.Current != from || rig.Busy); t += Time.deltaTime) yield return null;
                    for (float t = 0; t < .5f; t += Time.deltaTime) yield return null;
                    var frames = new System.Collections.Generic.List<Texture2D>(); rig.Request(to);
                    for (float t = 0, next = 0; t < secs; t += Time.deltaTime) { if (t >= next) { next += .12f; frames.Add(ShotFixed(cam)); } yield return null; }
                    Save($"posture_{id}_{name}", frames.ToArray(), 8); foreach (var f in frames) Object.Destroy(f);
                }
                yield return Film("flop_from_sit", Posture.Sit, Posture.Flop, 2.2f);
                yield return Film("flop_from_stand", Posture.Stand, Posture.Flop, 2.2f);
                yield return Film("flop_up", Posture.Flop, Posture.Stand, 2.4f);
                yield return Film("flop_to_sit", Posture.Flop, Posture.Sit, 3.2f);
                yield return Film("sleep_up", Posture.Sleep, Posture.Stand, 2.4f);
                Object.Destroy(rig.gameObject); yield return null;
            }
            Object.Destroy(cam.gameObject); Object.Destroy(light.gameObject);
        }

        static Texture2D ShotFixed(Camera cam)
        {
            var rt = new RenderTexture(Tile, Tile, 24) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var t = new Texture2D(Tile, Tile, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0); t.Apply();
            RenderTexture.active = null; cam.targetTexture = null; rt.Release();
            return t;
        }

        /// <summary>벌린 입 (CatMouth): 33품종 웃음(0.5)·크게(1) 를 한 장에 → Shots/sheets/mouths.png. 주둥이에 붙어 있고 떠 보이지 않는지.</summary>
        [UnityTest]
        public IEnumerator BreedMouths()
        {
            var (cam, light) = Stage();
            var breeds = CatIsland.Game.Catalog.Breeds; var tiles = new Texture2D[breeds.Length * 2];
            for (int i = 0; i < breeds.Length; i++)
            {
                CatRig rig = null; yield return Cat(breeds[i].id, "", "", r => rig = r);
                rig.smile = .5f; for (float t = 0; t < .6f; t += Time.deltaTime) yield return null; tiles[i * 2] = Shot(cam, rig);
                rig.smile = 0f; rig.mouthOpen = 1f; for (float t = 0; t < .6f; t += Time.deltaTime) yield return null; tiles[i * 2 + 1] = Shot(cam, rig);
                Object.Destroy(rig.gameObject); yield return null;
            }
            Save("mouths", tiles, 10);
            // 혀를 내미는 동작(입맛 다시기·세수 핥기)에서 입 모양이 혀를 가리지 않는지
            var lick = new System.Collections.Generic.List<Texture2D>();
            foreach (var id in new[] { "korean_shorthair", "persian", "siamese", "munchkin" })
            {
                CatRig rig = null; yield return Cat(id, "", "", r => rig = r);
                foreach (var clip in new[] { "LickLips", "GroomFace" })
                {
                    if (!rig.HasClip(clip)) continue;
                    rig.PlayAction(clip); float len = rig.ClipLength(clip);
                    foreach (var at in new[] { .2f, .35f, .5f })
                    { while (rig.ActionTime < len * at && rig.ActionClip != null) yield return null; lick.Add(Shot(cam, rig)); }
                    rig.StopAction(); for (float t = 0; t < .5f; t += Time.deltaTime) yield return null;
                }
                Object.Destroy(rig.gameObject); yield return null;
            }
            Save("mouths_lick", lick.ToArray(), 6);
            Object.Destroy(cam.gameObject); Object.Destroy(light.gameObject);
            Assert.Pass("Shots/sheets/mouths.png");
        }
    }
}
