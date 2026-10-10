namespace CatIsland.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 브랜드 그림 (앱 아이콘·심볼): 게임과 같은 3D·같은 재질·같은 빛으로 찍는다 - 작은 풀밭 섬(흙 옆면이 보이는 장난감 섬),
    /// 원목 데크 2 x 2 칸, 그 위에 정면을 보고 앉은 코리안 숏헤어, 튤립·덤불. 배경 없이(투명) 2048 px → Shots/brand/symbol_raw.png.
    /// 배경·변형·워드마크는 prototype/3d/brand_make.py 가 만든다. 평소 테스트에서는 돌지 않는다: -testFilter CatIsland.Tests.BrandArt
    /// </summary>
    [Explicit]
    public class BrandArt
    {
        const int Size = 2048;
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots/brand"));

        [UnityTest]
        public IEnumerator Symbol()
        {
            Directory.CreateDirectory(Dir);
            WorldStyle.Off();   // (둥근 세상 휨 없이)
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.86f, .9f, .96f); RenderSettings.ambientEquatorColor = new Color(.84f, .84f, .8f); RenderSettings.ambientGroundColor = new Color(.62f, .6f, .56f);
            var sunGo = new GameObject("Sun"); var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.25f; sun.color = new Color(1f, .97f, .9f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .55f; sunGo.transform.rotation = Quaternion.Euler(48f, 205f, 0f); RenderSettings.sun = sun;

            var root = new GameObject("BrandIsland").transform; var at = new Vector3(0f, 0f, 0f); root.position = at;
            const float D = 1.7f;
            var grass = Materials.Painted("BrandGrass", PaintedTextures.Grass(), Vector2.one / 4.2f, true);
            Shapes.Make(root, "Grass", MeshFactory.IslandTop(), grass, Vector3.zero, new Vector3(D, 1.4f, D), default, false);
            Shapes.Make(root, "Soil", MeshFactory.IslandSoil(), Palette.Hex("e6cf93"), new Vector3(0f, -0.07f, 0f), new Vector3(D - .05f, 1.0f, D - .05f), default, false);
            // 원목 데크 2 x 2 칸: 게임 타일과 같은 무늬·두께의 네모 판 (칸 경계에 무늬를 맞춤)
            {
                float h = WorldSync.Cell, y = FloorTiles.Top, yb = -.02f; var c = new Vector3(0f, 0f, .02f);
                var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
                void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 nrm) { int i = v.Count; v.Add(a); v.Add(b); v.Add(cc); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nrm); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
                float x0 = c.x - h, x1 = c.x + h, z0 = c.z - h, z1 = c.z + h;
                Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up);
                Quad(new Vector3(x1, y, z0), new Vector3(x1, yb, z0), new Vector3(x0, yb, z0), new Vector3(x0, y, z0), Vector3.back);
                Quad(new Vector3(x0, y, z1), new Vector3(x0, yb, z1), new Vector3(x1, yb, z1), new Vector3(x1, y, z1), Vector3.forward);
                Quad(new Vector3(x0, y, z0), new Vector3(x0, yb, z0), new Vector3(x0, yb, z1), new Vector3(x0, y, z1), Vector3.left);
                Quad(new Vector3(x1, y, z1), new Vector3(x1, yb, z1), new Vector3(x1, yb, z0), new Vector3(x1, y, z0), Vector3.right);
                var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
                var deckMat = Materials.Painted("BrandDeck", PaintedTextures.Tile("deck_honey"), Vector2.one / WorldSync.Cell, true);
                deckMat.SetTextureOffset("_BaseMap", new Vector2(-x0 / WorldSync.Cell, -z0 / WorldSync.Cell));
                var dg = new GameObject("Deck"); dg.transform.SetParent(root, false); dg.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = dg.AddComponent<MeshRenderer>(); mr.sharedMaterial = deckMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 꽃과 덤불 (섬 가장자리, 고양이 양옆 뒤)
            var rng = new System.Random(7);
            IslandBuilder.Bush(root, "BushL", new Vector3(-.6f, 0f, -.42f), .5f, rng);
            IslandBuilder.Bush(root, "BushR", new Vector3(.62f, 0f, -.36f), .44f, rng);
            void Clump(string n, Vector3 p, Color c, int k) { var g = Shapes.Pivot(root, n, p); for (int i = 0; i < k; i++) IslandBuilder.Tulip(g, "T" + i, new Vector3((i % 2) * .17f - .085f, 0f, (i / 2) * .17f - .085f), c, .62f); }
            Clump("TulipsL", new Vector3(-.62f, 0f, .3f), Palette.Hex("ff5d6c"), 3);
            Clump("TulipsR", new Vector3(.64f, 0f, .26f), Palette.Hex("ffd23f"), 3);

            // 고양이: 데크 위에 앉아 정면을 본다
            var catGo = new GameObject("BrandCat"); catGo.SetActive(false); catGo.transform.position = new Vector3(0f, FloorTiles.Top + .008f, -.02f); catGo.transform.rotation = Quaternion.identity;   // (카메라 쪽을 본다)
            var rig = catGo.AddComponent<CatRig>(); rig.breed = "korean_shorthair"; catGo.SetActive(true);
            rig.Request(Posture.Sit);
            for (float t = 0; t < 5f; t += Time.deltaTime) yield return null;   // (앉는 동작이 끝나고, 앉아 있기 동작에서 고개가 정면일 때)
            Debug.Log($"[Brand] posture {rig.Current}");

            // 카메라: 살짝 위에서, 고양이 얼굴이 크게
            var camGo = new GameObject("BrandCam"); var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 26f; cam.nearClipPlane = .05f; cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); cam.allowMSAA = true;
            var look = new Vector3(0f, .26f, .05f);
            cam.transform.position = look + Quaternion.Euler(18f, 180f, 0f) * Vector3.back * 4.1f; cam.transform.LookAt(look);
            QualitySettings.shadowDistance = 20f;

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); tex.Apply(); RenderTexture.active = null;
            cam.targetTexture = null; rt.Release();
            File.WriteAllBytes(Path.Combine(Dir, "symbol_raw.png"), tex.EncodeToPNG());
            Object.Destroy(tex); Object.Destroy(camGo); Object.Destroy(catGo); Object.Destroy(root.gameObject); Object.Destroy(sunGo);
            Assert.Pass(Path.Combine(Dir, "symbol_raw.png"));
        }
    }
}
