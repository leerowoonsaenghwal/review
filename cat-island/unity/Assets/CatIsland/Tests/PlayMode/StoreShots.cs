namespace CatIsland.Tests
{
    using System.Collections;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// 앱스토어 스크린샷 원본 (docs/APPSTORE.md 6장): 게임 화면 그대로, 버튼 띠 없이. 문구 띠는 tools/store_shots.py 가 얹는다.
    /// 평소 테스트에서는 돌지 않는다 ([Explicit]): tools/store_shots.sh 로 실행.
    /// </summary>
    [Explicit]
    public class StoreShots : SceneFixture
    {
        static readonly (string name, int w, int h)[] Sizes = { ("69", 1320, 2868), ("65", 1284, 2778) };
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Shots/store_raw"));

        void Capture(string name)
        {
            Directory.CreateDirectory(Dir);
            var cam = Cam;
            foreach (var (size, w, h) in Sizes)
            {
                var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Dir, $"{name}_{size}.png"), tex.EncodeToPNG());
                Object.Destroy(tex); cam.targetTexture = null; rt.Release();
            }
            Debug.Log("[StoreShots] " + name);
        }

        void HideUI(bool hide) { foreach (var n in new[] { "Top", "Bottom", "Side", "Hint_shadow" }) game.UI.Root.Find(n)?.gameObject.SetActive(!hide); }

        [UnityTest]
        public IEnumerator Capture_All()
        {
            var g = game.Logic; g.S.catSlots = 6; g.AddCoins(99999);
            var nabi = g.AddCat("korean_shorthair", "나비", CatIsland.Game.Personality.Playful);
            game.SyncCats(); game.WorldLink.Refresh(); HideUI(true);
            yield return new WaitForSeconds(1f);

            // 1. 쓰다듬기: 가까이, 하트 (쓰다듬는 중에 사진 시점으로)
            Cat.Needs.SetForTest(1f, 1f); Cat.NoteUserActivity();
            Cat.transform.SetPositionAndRotation(new Vector3(0f, 0f, -0.6f), Quaternion.Euler(0f, 150f, 0f)); Cat.ForceState(CatState.Idle);
            game.IslandCam.follow = Cat.transform; game.IslandCam.zoomLevel = 1; game.IslandCam.SnapNow(); yield return null;
            yield return Stroke(() => BackPoint, 2f, () => Cat.Pet.Pleasure > .45f);   // (웃는 얼굴까지: 더 쓰다듬으면 발라당)
            var headFocus = new GameObject("HeadFocus").transform; game.IslandCam.follow = headFocus;
            game.IslandCam.BeginPhoto();
            for (int k = 0; k < 2; k++)   // (쓰다듬으면 고양이가 몸을 돌린다: 돌린 뒤 머리 쪽에서 다시 맞춘다)
            {
                game.IslandCam.FramePhoto(Cat.Rig.Head.eulerAngles.y + 180f - 25f, 18f, 3.0f);
                headFocus.position = Vector3.Lerp(Cat.Rig.Head.position, Cat.Rig.BodyZone.position, .35f) - Vector3.up * .4f;   // (머리와 몸 사이)
                yield return Stroke(() => BackPoint, .6f, () => Cat.Pet.Pleasure > .6f);
            }
            Debug.Log($"[StoreShots] cat yaw {Cat.transform.eulerAngles.y:F0} head yaw {Cat.Rig.Head.eulerAngles.y:F0} cam yaw {game.IslandCam.transform.eulerAngles.y:F0}");
            Capture("1_petting");
            game.IslandCam.EndPhoto(); game.IslandCam.follow = Cat.transform; Object.Destroy(headFocus.gameObject);

            // 2. 꾸미기: 방석·숨숨집·화분을 놓으면 바로 써 본다
            foreach (var (id, x, z) in new[] { ("cushion", 7, 9), ("hideout", 10, 6), ("plant_pot", 5, 6), ("mouse_toy", 8, 11) })
                if (g.Buy(id)) g.Place(id, CatIsland.Game.Zone.Indoor, x, z, 2);
            // (데크 타일: 가운데 원목 마루, 앞으로 징검돌 - 섬마다 다른 바닥)
            void Lay(string tile, int x0, int z0, int x1, int z1) { for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { if (g.Tiles(tile) <= 0) g.BuyTiles(tile); g.LayTile(tile, CatIsland.Game.Zone.Indoor, x, z); } }
            g.AddCoins(5000); Lay("deck_honey", 4, 5, 13, 12); Lay("stone_path", 8, 1, 9, 4); Lay("tile_mint", 1, 6, 3, 8);
            game.WorldLink.Refresh();
            Time.timeScale = 3f;
            for (float t = 0; t < 25f && Cat.State != CatState.UseItem; t += Time.deltaTime) yield return null;
            Time.timeScale = 1f; yield return new WaitForSeconds(1.2f);
            game.IslandCam.zoomLevel = 1; game.IslandCam.follow = Cat.transform; game.IslandCam.SnapNow(); yield return null;
            Capture("2_decorate");

            // 3. 캣타워: 꼭대기
            if (g.Buy("tower_tall") && g.Place("tower_tall", CatIsland.Game.Zone.Indoor, 6, 5, 2))
            {
                game.WorldLink.Refresh(); yield return null;
                var tall = CatTower.All.FirstOrDefault(t => t.Id == "tower_tall");
                if (tall)
                {
                    Time.timeScale = 4f; Cat.ClimbTo(tall, tall.TopDeck);
                    for (float t = 0; t < 60f && !(Cat.OnTower && Cat.transform.position.y > 1.9f); t += Time.deltaTime) yield return null;
                    Time.timeScale = 1f; yield return new WaitForSeconds(1.5f);
                    game.IslandCam.zoomLevel = 0; game.IslandCam.SnapNow(); yield return null;
                    Capture("3_tower");
                    Cat.OnTapGround(new Vector3(0, 0, -1.5f));
                    Time.timeScale = 4f; for (float t = 0; t < 40f && Cat.OnTower; t += Time.deltaTime) yield return null; Time.timeScale = 1f;
                }
            }

            // 4. 품종: 여러 품종이 모여 앉은 섬
            foreach (var (b, n) in new[] { ("persian", "보리"), ("munchkin", "콩"), ("siamese", "달이"), ("maine_coon", "호두") }) g.AddCat(b, n, CatIsland.Game.Personality.Easygoing);
            game.SyncCats(); yield return null;
            var all = Object.FindObjectsByType<CatBrain>(FindObjectsSortMode.None).Where(c => c.isActiveAndEnabled && c.Data != null).ToList();
            var center = new Vector3(1.3f, 0f, -1.3f);   // (캣타워에 가리지 않는 자리)
            for (int k = 0; k < all.Count; k++)
            {
                float a = (k - (all.Count - 1) / 2f) * .5f;
                all[k].transform.SetPositionAndRotation(center + new Vector3(a, 0f, Mathf.Abs(a) * .35f), Quaternion.Euler(0f, 180f - a * 25f, 0f));
                all[k].Needs.SetForTest(1f, 1f); all[k].ForceState(CatState.Idle);
            }
            var mid = new GameObject("ShotFocus").transform; mid.position = center + new Vector3(0, 0, .25f);
            game.IslandCam.follow = mid; game.IslandCam.BeginPhoto(); game.IslandCam.FramePhoto(0f, 26f, 7.2f);
            yield return new WaitForSeconds(1.2f);
            Capture("4_breeds");

            // 5. 손님: 오늘의 손님 고양이와 우리 고양이
            game.IslandCam.EndPhoto();
            g.S.guestBreed = "scottish_fold"; g.S.guestTreated = false; g.S.guestGone = false;
            game.SyncGuest(); yield return new WaitForSeconds(.5f);
            var guest = GameObject.Find("GuestCat");
            if (guest)
            {
                guest.transform.SetPositionAndRotation(center + new Vector3(-.35f, 0f, -.5f), Quaternion.Euler(0f, 165f, 0f));
                all[0].transform.SetPositionAndRotation(center + new Vector3(.35f, 0f, -.4f), Quaternion.Euler(0f, 215f, 0f)); all[0].ForceState(CatState.Idle);
                foreach (var k in all.Skip(1)) k.gameObject.SetActive(false);
                mid.position = center + new Vector3(0, 0, -.45f);
                game.IslandCam.BeginPhoto(); game.IslandCam.FramePhoto(10f, 24f, 4.6f);
                yield return new WaitForSeconds(1.2f);
                Capture("5_guest");
                foreach (var k in all.Skip(1)) k.gameObject.SetActive(true);
                game.IslandCam.EndPhoto();
            }

            // 6. 하루: 노을 섬
            game.IslandCam.follow = Cat.transform; game.IslandCam.zoomLevel = 0; game.IslandCam.SnapNow();
            DayCycle.HourOverride = 18.6f; game.Day.Apply(18.6f); yield return null; yield return null;
            Capture("6_sunset");
            DayCycle.HourOverride = 13f; HideUI(false);
        }
    }
}
