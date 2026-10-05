using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>파이프라인 용품(Resources/Art/Items)을 불러온다. 배치 정보는 용품 JSON(anchors)을 따른다.</summary>
    public static class ItemLoader
    {
        public static GameObject Spawn(string id, Transform parent, Vector3 pos, float yaw)
        {
            var prefab = Resources.Load<GameObject>("Art/Items/" + id);
            if (prefab == null) throw new InvalidOperationException($"용품 에셋이 없습니다: {id}. tools/sync_art.py 와 CatIsland/Import Art 를 실행하세요.");
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
            var model = UnityEngine.Object.Instantiate(prefab, go.transform, false);
            model.name = "Model";
            return go;
        }

        public static string InfoText(string id) => Resources.Load<TextAsset>("Art/Items/" + id + "_info")?.text;
    }

    /// <summary>사료 그릇. 누르면 사료가 채워지고, 고양이가 먹으면 줄어든다.</summary>
    public class FoodBowl : MonoBehaviour
    {
        [Serializable] class Surface { public float y = 0.03f; public float r = 0.13f; }
        [Serializable] class Anchors { public Surface surface = new Surface(); public float rimTop = 0.038f; }
        [Serializable] class Info { public Anchors anchors = new Anchors(); }

        public float Food { get; private set; }
        public bool HasFood => Food > 0.02f;
        readonly List<Transform> kibbles = new List<Transform>();
        float wobble, wobbleVel;
        Transform visual;

        public static FoodBowl Create(Transform parent, Vector3 pos)
        {
            var go = ItemLoader.Spawn("food_bowl", parent, pos, 0f);
            var b = go.AddComponent<FoodBowl>();
            b.Build();
            return b;
        }

        void Build()
        {
            var info = JsonUtility.FromJson<Info>(ItemLoader.InfoText("food_bowl") ?? "{}");
            visual = transform.Find("Model");
            // 사료 알갱이: 그릇 표면(surface) 위에 낮은 더미. 고양이 혀가 닿는 높이와 같다
            var rng = new System.Random(5);
            var sphere = MeshFactory.Sphere();
            float r0 = info.anchors.surface.r * 0.92f, y0 = info.anchors.surface.y;
            var pile = Shapes.Pivot(transform, "Kibble", Vector3.zero);
            for (int i = 0; i < 46; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * r0;
                float h = y0 + (r0 - r) * 0.12f + (float)rng.NextDouble() * 0.004f;
                var k = Shapes.Make(pile, "K" + i, sphere, i % 3 == 0 ? Palette.Chestnut : Palette.Kibble,
                    new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r), new Vector3(0.026f, 0.018f, 0.022f), new Vector3(0f, i * 37f, 0f), false);
                kibbles.Add(k);
            }
            var col = gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.05f, 0f);
            col.radius = 0.3f; // 손가락으로 누르기 쉽게 실제보다 크게
            SetFood(0f);
        }

        public void Fill() { SetFood(1f); wobbleVel += 5f; }
        public void Consume(float amount) => SetFood(Food - amount);

        void SetFood(float f)
        {
            Food = Mathf.Clamp01(f);
            int visible = Mathf.CeilToInt(Food * kibbles.Count);
            for (int i = 0; i < kibbles.Count; i++) kibbles[i].gameObject.SetActive(i < visible);
        }

        void Update()
        {
            wobbleVel += (-wobble * 260f - wobbleVel * 10f) * Time.deltaTime;
            wobble += wobbleVel * Time.deltaTime;
            if (visual) visual.localScale = new Vector3(1f + wobble * 0.06f, 1f - wobble * 0.1f, 1f + wobble * 0.06f);
        }
    }

    /// <summary>방석. 고양이가 식빵 자세로 누워 잔다. 위 높이(top)만큼 고양이를 올린다.</summary>
    public class Cushion : MonoBehaviour
    {
        [Serializable] class Anchors { public float top = 0.134f; public float r = 0.384f; }
        [Serializable] class Info { public Anchors anchors = new Anchors(); }

        public float TopHeight { get; private set; } = 0.134f;
        public float Radius { get; private set; } = 0.384f;
        float squish, squishVel;
        Transform visual;

        public static Cushion Create(Transform parent, Vector3 pos)
        {
            var go = ItemLoader.Spawn("cushion", parent, pos, 0f);
            var c = go.AddComponent<Cushion>();
            c.Build();
            return c;
        }

        void Build()
        {
            var info = JsonUtility.FromJson<Info>(ItemLoader.InfoText("cushion") ?? "{}");
            TopHeight = info.anchors.top;
            Radius = info.anchors.r;
            visual = transform.Find("Model");
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius = 0.54f;
            col.height = 0.16f;
            col.center = new Vector3(0f, 0.07f, 0f);
        }

        public void Poke() { squishVel -= 4f; }

        void Update()
        {
            squishVel += (-squish * 200f - squishVel * 9f) * Time.deltaTime;
            squish += squishVel * Time.deltaTime;
            if (visual) visual.localScale = new Vector3(1f - squish * 0.05f, 1f + squish * 0.25f, 1f - squish * 0.05f);
        }
    }

    /// <summary>캣타워 1단. 고양이가 점프해 오르는 판 높이는 JSON decks[0].y.</summary>
    /// <summary>
    /// 고양이가 오르는 캣타워 (판 여러 개). 판의 자리·높이·크기는 용품 JSON 의 anchors.decks (x, z, y, size; 판 높이 0.4 m 단위).
    /// cat_tower_1 은 0.2 m 판 하나. 고양이는 한 층씩 뛰어 오르내린다 (CatBrain).
    /// </summary>
    public class CatTower : MonoBehaviour
    {
        [Serializable] class Deck { public float y = 0.2f; public float x, z; public float[] size; public bool round; }
        [Serializable] class Anchors { public Deck[] decks; }
        [Serializable] class Info { public Anchors anchors = new Anchors(); }
        public struct DeckSpot { public Vector3 local; public Vector2 size; public bool round; }

        public static readonly List<CatTower> All = new List<CatTower>();
        public string Id { get; private set; }
        public readonly List<DeckSpot> Decks = new List<DeckSpot>();
        public float DeckHeight => Decks.Count > 0 ? Decks[0].local.y : 0.2f;
        public Vector2 DeckSize => Decks.Count > 0 ? Decks[0].size : new Vector2(0.72f, 1f);
        public int TopDeck { get { int t = 0; for (int i = 1; i < Decks.Count; i++) if (Decks[i].local.y > Decks[t].local.y) t = i; return t; } }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public static CatTower Create(Transform parent, Vector3 pos, float yaw)
        {
            var go = ItemLoader.Spawn("cat_tower_1", parent, pos, yaw);
            var t = Attach(go, "cat_tower_1");
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.11f, 0f);
            col.size = new Vector3(0.8f, 0.22f, 1.08f);
            return t;
        }
        /// <summary>섬에 놓인 캣타워 모델에 판 정보를 붙인다.</summary>
        public static CatTower Attach(GameObject go, string id)
        {
            var t = go.GetComponent<CatTower>() ?? go.AddComponent<CatTower>(); t.Id = id; t.Decks.Clear();
            var info = JsonUtility.FromJson<Info>(ItemLoader.InfoText(id) ?? "{}");
            if (info.anchors.decks != null)
                foreach (var d in info.anchors.decks)
                    t.Decks.Add(new DeckSpot { local = new Vector3(d.x, d.y, d.z), size = d.size != null && d.size.Length == 2 ? new Vector2(d.size[0], d.size[1]) : new Vector2(.7f, .8f), round = d.round });
            if (t.Decks.Count == 0) t.Decks.Add(new DeckSpot { local = new Vector3(0, .2f, 0), size = new Vector2(.72f, 1f) });
            return t;
        }

        public float Height(int i) => i < 0 ? 0f : transform.position.y + Decks[i].local.y;
        public Vector3 Center(int i) { var w = transform.TransformPoint(new Vector3(Decks[i].local.x, 0, Decks[i].local.z)); w.y = Height(i); return w; }
        /// <summary>판 가장자리까지의 거리 (판 가운데에서 dir 방향으로).</summary>
        public float Extent(int i, Vector3 dir)
        {
            var l = transform.InverseTransformDirection(dir); var h = Decks[i].size * .5f;
            if (Decks[i].round) return h.x;
            return Mathf.Abs(l.x) * h.x + Mathf.Abs(l.z) * h.y;
        }
        public bool Inside(int i, Vector3 world, float margin)
        {
            var l = transform.InverseTransformPoint(world) - new Vector3(Decks[i].local.x, 0, Decks[i].local.z); var h = Decks[i].size * .5f;
            if (Decks[i].round) return new Vector2(l.x, l.z).magnitude <= h.x - margin;
            return Mathf.Abs(l.x) <= h.x - margin && Mathf.Abs(l.z) <= h.y - margin;
        }
        public Vector3 ClampInto(int i, Vector3 world, float margin)
        {
            var c = new Vector3(Decks[i].local.x, 0, Decks[i].local.z); var l = transform.InverseTransformPoint(world) - c; var h = Decks[i].size * .5f;
            if (Decks[i].round) { var v = new Vector2(l.x, l.z); if (v.magnitude > h.x - margin) v = v.normalized * (h.x - margin); l = new Vector3(v.x, 0, v.y); }
            else l = new Vector3(Mathf.Clamp(l.x, -h.x + margin, h.x - margin), 0, Mathf.Clamp(l.z, -h.y + margin, h.y - margin));
            var w = transform.TransformPoint(c + l); w.y = Height(i); return w;
        }
        /// <summary>지금 높이(level: -1 바닥)에서 한 번에 뛸 수 있는 다음 위 판 (0.85 m 이내에서 가장 낮은 것).</summary>
        public int NextUp(int level, Vector3 from)
        {
            float h = Height(level); int best = -1; float bh = float.MaxValue, bd = float.MaxValue;
            for (int i = 0; i < Decks.Count; i++)
            {
                float dh = Height(i) - h; if (dh <= .05f || dh > .85f) continue;
                float d = (new Vector3(Center(i).x, 0, Center(i).z) - new Vector3(from.x, 0, from.z)).magnitude;
                if (dh < bh - .01f || Mathf.Abs(dh - bh) < .01f && d < bd) { bh = dh; bd = d; best = i; }
            }
            return best;
        }
        /// <summary>내려갈 다음 판 (바로 아래 층, 없으면 -1 바닥).</summary>
        public int NextDown(int level, Vector3 from)
        {
            float h = Height(level); int best = -1; float bh = -1, bd = float.MaxValue;
            for (int i = 0; i < Decks.Count; i++)
            {
                float dh = h - Height(i); if (dh <= .05f || dh > .85f) continue;
                float d = (new Vector3(Center(i).x, 0, Center(i).z) - new Vector3(from.x, 0, from.z)).magnitude;
                if (Height(i) > bh + .01f || Mathf.Abs(Height(i) - bh) < .01f && d < bd) { bh = Height(i); bd = d; best = i; }
            }
            if (best < 0 && h > .85f) { for (int i = 0; i < Decks.Count; i++) if (Height(i) < h - .05f && (best < 0 || Height(i) > Height(best))) best = i; }   // (멀어도 가장 가까운 아래 층)
            return best;
        }
    }

    /// <summary>
    /// 장난감 같은 작은 섬 (docs/ART_DIRECTION.md 4장): 그린 듯한 풀밭, 뒤쪽의 언덕 층(둥근 모서리 절벽), 뭉툭한 나무,
    /// 튤립 무리, 모래 띠 → 얕은 물 띠 → 청록 바다. 가운데에 나무 마루와 러그 (집 안 자리).
    /// 고양이(몸길이 약 1 m)와 용품 크기에 맞춘 비율. 시안(style_compare.html)의 크기는 고양이 비율로 2.56배 (모델 단위 / METERS).
    /// </summary>
    public static class IslandBuilder
    {
        public const float Radius = 6.6f;
        // 바닥 구역 (발소리 재질): 마루, 러그, 모래밭. 나머지는 풀밭
        public static readonly Vector3 DeckCenter = new Vector3(0f, 0f, 0.5f);
        public const float DeckRadius = 3.8f;
        public static readonly Vector3 RugCenter = new Vector3(0f, 0f, -0.5f);
        public const float RugRadius = 1.7f;
        public static readonly Vector3 SandCenter = new Vector3(-3.4f, 0f, -3.2f);
        // 마당 (섬 구역 2): 섬 오른쪽에 이어진 풀밭. 열리기 전에는 나무 울타리로 막혀 있다
        public static readonly Vector3 YardCenter = new Vector3(9.4f, 0f, 0.2f);
        public const float YardRadius = 4.4f;
        public const float SandRadius = 1.2f;

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>섬 장식 중 고양이가 지나가면 안 되는 것 (위치, 반경): 덤불, 나무 줄기, 꽃.</summary>
        public static readonly List<(string name, Vector3 pos, float radius)> Solids = new List<(string, Vector3, float)>();

        public static Surface SurfaceAt(Vector3 p)
        {
            if (Flat(p, RugCenter) < RugRadius) return Surface.Rug;
            if (Flat(p, DeckCenter) < DeckRadius) return Surface.Wood;
            if (Flat(p, SandCenter) < SandRadius) return Surface.Sand;
            return Surface.Grass;
        }

        // 언덕 층: 섬 뒤쪽 호 (각도는 +x 에서 반시계, +z 가 뒤)
        public const float TierInner = 5.4f, TierOuter = 6.75f, TierHeight = 0.8f, TierEdge = 0.18f;
        public const float TierFrom = 32f, TierTo = 148f;
        const float MockScale = 2.56f;   // 시안 1 m = 게임 2.56 단위 (고양이 크기 비율)

        public static Transform Build(Transform parent)
        {
            Solids.Clear();
            var root = Shapes.Pivot(parent, "Island", Vector3.zero);
            var sphere = MeshFactory.Sphere();
            float D = Radius * 2f;

            var grass = Materials.Painted("Grass", PaintedTextures.Grass(), Vector2.one / 9.6f, true);
            var sand = Materials.Painted("Sand", PaintedTextures.Sand(), Vector2.one / 2.5f, true);

            // 땅: 풀밭 원판, 그 아래 모래 띠가 물속으로 잠긴다
            Shapes.Make(root, "Grass", MeshFactory.IslandTop(), grass, Vector3.zero, new Vector3(D, 1.4f, D), default, false);
            Shapes.Make(root, "Soil", MeshFactory.IslandSoil(), Palette.Hex("e6cf93"), new Vector3(0f, -0.07f, 0f), new Vector3(D - 0.1f, 2.6f, D - 0.1f), default, false);
            Shapes.Make(root, "Beach", MeshFactory.Lathe("beach", new List<Vector2> {
                new Vector2(9.0f, -0.42f), new Vector2(8.2f, -0.26f), new Vector2(7.5f, -0.13f), new Vector2(7.0f, -0.06f), new Vector2(6.6f, -0.03f), new Vector2(6.2f, -0.02f) }, 96),
                sand, Vector3.zero, Vector3.one, default, false);
            // 물: 얕은 물 띠(밝은 청록) → 바다(청록). 반짝임은 약하게
            var shallow = Materials.Painted("Shallow", Texture2D.whiteTexture, Vector2.one, false, 0.12f, Palette.Hex("8ee6e4"));
            var sea = Materials.Painted("Sea", Texture2D.whiteTexture, Vector2.one, false, 0.12f, Palette.Hex("3fc4dc"));
            Shapes.Make(root, "Shallow", MeshFactory.Lathe("shallow", new List<Vector2> { new Vector2(8.9f, -0.115f), new Vector2(6.9f, -0.115f) }, 96), shallow, Vector3.zero, Vector3.one, default, false);
            Shapes.Make(root, "Sea", MeshFactory.Lathe("sea", new List<Vector2> { new Vector2(90f, -0.12f), new Vector2(40f, -0.12f), new Vector2(20f, -0.12f), new Vector2(12f, -0.12f), new Vector2(8.85f, -0.12f) }, 96), sea, Vector3.zero, Vector3.one, default, false);

            // 마루 (집 안 자리): 나무 판자 / 러그: 짜임 무늬
            Shapes.Make(root, "Deck", MeshFactory.RoundedCylinder(0.04f), Materials.Painted("Planks", PaintedTextures.Planks(), Vector2.one / 2f, true), DeckCenter + new Vector3(0f, -0.046f, 0f), new Vector3(DeckRadius * 2f, 0.05f, DeckRadius * 2f));
            Shapes.Make(root, "Rug", MeshFactory.RoundedCylinder(0.06f), Materials.Painted("Weave", PaintedTextures.Weave(), Vector2.one, true), RugCenter + new Vector3(0f, -0.028f, 0f), new Vector3(RugRadius * 2f, 0.04f, RugRadius * 2f), default, false);
            // 모래밭: 섬 앞 왼쪽 가장자리. 조개 두 개와 조약돌
            Shapes.Make(root, "Sand", MeshFactory.RoundedCylinder(0.08f), sand, SandCenter + new Vector3(0f, -0.04f, 0f), new Vector3(SandRadius * 2f, 0.05f, SandRadius * 2f), default, false);
            Shapes.Make(root, "Shell1", sphere, Palette.Hex("ff9f8a"), SandCenter + new Vector3(0.45f, 0.02f, -0.3f), new Vector3(0.14f, 0.06f, 0.12f), new Vector3(0f, 30f, 0f), false);
            Shapes.Make(root, "Shell2", sphere, Palette.Hex("fff4e0"), SandCenter + new Vector3(-0.35f, 0.02f, 0.4f), new Vector3(0.11f, 0.05f, 0.1f), new Vector3(0f, -20f, 0f), false);
            Shapes.Make(root, "Pebble", sphere, Palette.Hex("c9c4ba"), SandCenter + new Vector3(0.1f, 0.02f, 0.55f), new Vector3(0.12f, 0.07f, 0.1f), default, false);

            BuildTier(root, grass);
            BuildYard(root, grass, sand);

            // 뭉툭한 나무: 언덕 위 셋, 땅 위 둘 (나무마다 크기·색을 조금씩 다르게)
            var rng = new System.Random(21);
            Vector3 OnTier(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * r, TierHeight + 0.07f, Mathf.Sin(a) * r); }
            Tree(root, "Tree0", OnTier(58f, 6.05f), 0.8f, Palette.Hex("3f9a3e"), rng);
            Tree(root, "Tree1", OnTier(94f, 6.15f), 0.7f, Palette.Hex("4aa845"), rng);
            Tree(root, "Tree2", OnTier(126f, 6.0f), 0.78f, Palette.Hex("3f9a3e"), rng);
            Tree(root, "Tree3", new Vector3(-4.6f, 0f, -2.0f), 0.75f, Palette.Hex("3f9a3e"), rng, true);
            Tree(root, "Tree4", new Vector3(4.6f, 0f, -2.2f), 0.7f, Palette.Hex("4aa845"), rng, true);

            // 튤립 무리 (빨강·노랑·하양, 3~4송이씩). 카메라 앞은 비워 둔다
            Color red = Palette.Hex("ff5d6c"), yellow = Palette.Hex("ffd23f"), white = Palette.Hex("ffffff");
            var clumps = new (Vector3 pos, Color c, int n)[]
            {
                (new Vector3(-4.6f, 0f, 1.4f), red, 4), (new Vector3(4.5f, 0f, 1.0f), yellow, 4), (new Vector3(-2.6f, 0f, 4.0f), white, 3),
                (new Vector3(2.9f, 0f, 3.6f), red, 4), (new Vector3(3.6f, 0f, -2.6f), yellow, 3), (new Vector3(-4.3f, 0f, -0.5f), white, 4),
                (OnTier(76f, 6.0f), yellow, 3), (OnTier(110f, 5.95f), red, 4), (OnTier(40f, 6.1f), white, 3),
            };
            for (int i = 0; i < clumps.Length; i++)
            {
                var (pos, c, n) = clumps[i];
                var g = Shapes.Pivot(root, "Tulips" + i, pos, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f));
                if (pos.y < 0.1f) Solids.Add(("Tulips" + i, new Vector3(pos.x, 0f, pos.z), 0.3f));
                for (int k = 0; k < n; k++)
                {
                    var off = new Vector3((k % 2) * 0.31f - 0.155f, 0f, (k / 2) * 0.31f - 0.155f) + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.08f;
                    Tulip(g, "T" + k, off, c, 0.9f + (float)rng.NextDouble() * 0.2f);
                }
            }

            var ground = Shapes.Pivot(root, "GroundCollider", new Vector3(0f, -0.05f, 0f));
            var gc = ground.gameObject.AddComponent<BoxCollider>();
            gc.size = new Vector3(D, 0.1f, D);
            return root;
        }

        /// <summary>언덕 층: 둥근 모서리의 절벽(돌 무늬) + 살짝 넘치는 풀 뚜껑. 양 끝은 둥근 기둥으로 막는다.</summary>
        static void BuildTier(Transform root, Material grass)
        {
            float H = TierHeight, e = TierEdge, mid = (TierInner + TierOuter) * 0.5f, hw = (TierOuter - TierInner) * 0.5f;
            float arcLen = Mathf.Deg2Rad * (TierTo - TierFrom) * TierInner;
            const float stoneTile = 1.75f;   // 시안: 돌 무늬 한 장이 가로 1.75 m, 세로 0.8 m (절벽 높이가 시안과 같아 크기도 그대로)

            // 단면 (반지름, 높이): 바깥 아래 → 바깥 위(둥근 모서리) → 안쪽 위(둥근 모서리) → 안쪽 아래. 법선이 바깥을 향하는 순서
            List<Vector2> CliffProfile(float rin, float rout, bool closedTop)
            {
                var p = new List<Vector2> { new Vector2(rout + 0.06f, -0.16f) };
                MeshFactory.Arc(p, new Vector2(rout - e, H - e), e, 0f, 90f, 6);
                if (closedTop) { p.Add(new Vector2(0f, H)); return p; }
                MeshFactory.Arc(p, new Vector2(rin + e, H - e), e, 90f, 180f, 6);
                p.Add(new Vector2(rin + 0.04f, -0.04f));
                return p;
            }
            // 풀 뚜껑: 절벽 위를 덮고 둥근 입술로 조금 넘친다
            List<Vector2> CapProfile(float rin, float rout, bool closed)
            {
                const float lip = 0.08f;
                var p = new List<Vector2>();
                MeshFactory.Arc(p, new Vector2(rout - lip * 0.6f, H + 0.0f), lip, -70f, 90f, 6);
                if (closed) { p.Add(new Vector2(0f, H + lip)); return p; }
                MeshFactory.Arc(p, new Vector2(rin + lip * 0.6f, H + 0.0f), lip, 90f, 250f, 6);
                return p;
            }

            var cliff = Materials.Painted("Cliff", PaintedTextures.Cliff(), new Vector2(arcLen / stoneTile, 1f / 0.8f), false);
            int seg = 72;
            Shapes.Make(root, "TierCliff", MeshFactory.Lathe("tiercliff", CliffProfile(TierInner, TierOuter, false), seg, TierFrom, TierTo, true), cliff, Vector3.zero, Vector3.one);
            Shapes.Make(root, "TierTop", MeshFactory.Lathe("tiertop", CapProfile(TierInner, TierOuter, false), seg, TierFrom, TierTo), grass, Vector3.zero, Vector3.one, default, false);

            // 양 끝 둥근 기둥 (단면이 원이라 끝이 둥글다). 뚜껑은 겹침 깜빡임이 없게 2 mm 높게
            var endCliff = Materials.Painted("CliffEnd", PaintedTextures.Cliff(), new Vector2(2f * Mathf.PI * hw / stoneTile, 1f / 0.8f), false);
            foreach (float deg in new[] { TierFrom, TierTo })
            {
                float a = deg * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(a) * mid, 0f, Mathf.Sin(a) * mid);
                Shapes.Make(root, "TierEnd" + deg, MeshFactory.Lathe("tierend", CliffProfile(0f, hw, true), 40, 0f, 360f, true), endCliff, c, Vector3.one);
                Shapes.Make(root, "TierEndTop" + deg, MeshFactory.Lathe("tierendtop", CapProfile(0f, hw, true), 40), grass, c + new Vector3(0f, 0.002f, 0f), Vector3.one, default, false);
            }

            // 길찾기: 언덕을 원 여러 개로 막는다 (고양이는 아래 땅에서만 다닌다)
            for (float deg = TierFrom; deg <= TierTo + 0.01f; deg += 8f)
            {
                float a = deg * Mathf.Deg2Rad;
                Solids.Add(("Tier" + Mathf.RoundToInt(deg), new Vector3(Mathf.Cos(a) * mid, 0f, Mathf.Sin(a) * mid), hw));
            }
        }

        /// <summary>마당: 오른쪽에 이어진 둥근 풀밭 (같은 높이), 이음목에 나무 울타리와 문. 열리면 문이 열린다 (YardGate).</summary>
        static void BuildYard(Transform root, Material grass, Material sand)
        {
            float D = YardRadius * 2f;
            var y = Shapes.Pivot(root, "Yard", YardCenter);
            Shapes.Make(y, "YardGrass", MeshFactory.IslandTop(), grass, Vector3.zero, new Vector3(D, 1.4f, D), default, false);
            Shapes.Make(y, "YardSoil", MeshFactory.IslandSoil(), Palette.Hex("e6cf93"), new Vector3(0f, -0.07f, 0f), new Vector3(D - 0.1f, 2.6f, D - 0.1f), default, false);
            Shapes.Make(y, "YardBeach", MeshFactory.Lathe("yardbeach", new List<Vector2> { new Vector2(6.6f, -0.42f), new Vector2(5.6f, -0.2f), new Vector2(4.8f, -0.06f), new Vector2(4.4f, -0.03f), new Vector2(4.0f, -0.02f) }, 96), sand, Vector3.zero, Vector3.one, default, false);
            // 울타리: 두 섬이 만나는 곳 (x ≈ 6), 가운데 문
            var gate = Shapes.Pivot(root, "YardGate", new Vector3(5.9f, 0f, 0.3f), new Vector3(0, 90, 0));
            var wood = Palette.Hex("e2b483");
            for (int i = -3; i <= 3; i++)
            {
                if (i == 0) continue;
                Shapes.Make(gate, "Post" + i, MeshFactory.RoundedCylinder(.03f), wood, new Vector3(i * .42f, .32f, 0), new Vector3(.12f, .64f, .12f));
            }
            Shapes.Make(gate, "RailL", MeshFactory.Capsule(.04f), wood, new Vector3(-.84f, .42f, 0), new Vector3(.08f, 1.3f, .08f), new Vector3(0, 0, 90));
            Shapes.Make(gate, "RailR", MeshFactory.Capsule(.04f), wood, new Vector3(.84f, .42f, 0), new Vector3(.08f, 1.3f, .08f), new Vector3(0, 0, 90));
            var door = Shapes.Pivot(gate, "Door", new Vector3(-.21f, 0, 0));
            Shapes.Make(door, "Board", MeshFactory.RoundedCylinder(.03f), Palette.Hex("c99a6b"), new Vector3(.21f, .3f, 0), new Vector3(.4f, .55f, .06f));
            Solids.Add(("YardFence", new Vector3(5.9f, 0f, 0.3f), 0.5f));
        }

        /// <summary>마당이 열리면 울타리 문이 열린다.</summary>
        public static void OpenYardGate(Transform islandRoot)
        {
            var door = islandRoot ? islandRoot.Find("YardGate/Door") : null; if (door) door.localRotation = Quaternion.Euler(0, -100, 0);
        }

        /// <summary>뭉툭한 장난감 나무: 짧은 줄기 + 큰 동그란 잎 덩어리 넷 (시안 크기 × 2.56).</summary>
        static void Tree(Transform root, string name, Vector3 pos, float size, Color leaf, System.Random rng, bool solid = false)
        {
            float s = size * MockScale;
            var t = Shapes.Pivot(root, name, pos, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f));
            if (solid) Solids.Add((name, new Vector3(pos.x, 0f, pos.z), 0.16f * s + 0.05f));
            var trunk = MeshFactory.Lathe("trunk", new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.16f, 0f), new Vector2(0.11f, 0.7f), new Vector2(0.11f, 0.7f), new Vector2(0f, 0.7f) }, 16);
            Shapes.Make(t, "Trunk", trunk, Palette.Hex("8b5e3c"), Vector3.zero, Vector3.one * s);
            // 잎색을 나무마다 조금씩 (밝기 ±4%)
            float j = 1f + ((float)rng.NextDouble() - 0.5f) * 0.08f;
            var c = new Color(leaf.r * j, leaf.g * j, leaf.b * j);
            foreach (var (dx, dy, dz, rr) in new[] { (0f, 1.0f, 0f, 0.5f), (-0.3f, 0.82f, 0.1f, 0.36f), (0.3f, 0.84f, 0.05f, 0.38f), (0f, 1.35f, 0f, 0.34f) })
                Shapes.Make(t, "Leaf", MeshFactory.Sphere(), c, new Vector3(dx, dy, dz) * s, new Vector3(rr * 2f, rr * 1.8f, rr * 2f) * s);
        }

        /// <summary>튤립 한 송이: 줄기 + 달걀꼴 꽃 + 잎 둘 (시안 크기 × 2.56).</summary>
        static void Tulip(Transform parent, string name, Vector3 at, Color c, float size)
        {
            float s = MockScale * size;
            var f = Shapes.Pivot(parent, name, at);
            Shapes.Make(f, "Stem", MeshFactory.Capsule(0.3f), Palette.Hex("3e9a3a"), new Vector3(0f, 0.08f, 0f) * s, new Vector3(0.02f, 0.16f, 0.02f) * s, default, false);
            Shapes.Make(f, "Bloom", MeshFactory.Sphere(), c, new Vector3(0f, 0.18f, 0f) * s, new Vector3(0.09f, 0.1125f, 0.09f) * s);
            foreach (float side in new[] { -1f, 1f })
                Shapes.Make(f, "Leaf", MeshFactory.Sphere(), Palette.Hex("4fae44"), new Vector3(side * 0.03f, 0.05f, 0f) * s, new Vector3(0.028f, 0.104f, 0.072f) * s, new Vector3(0f, 0f, side * -28.6f), false);
        }
    }
}
