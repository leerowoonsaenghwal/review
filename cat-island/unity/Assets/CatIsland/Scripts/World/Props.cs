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
    public class CatTower : MonoBehaviour
    {
        [Serializable] class Deck { public float y = 0.2f; public float z; }
        [Serializable] class Anchors { public Deck[] decks; }
        [Serializable] class Info { public Anchors anchors = new Anchors(); }

        public float DeckHeight { get; private set; } = 0.2f;

        public static CatTower Create(Transform parent, Vector3 pos, float yaw)
        {
            var go = ItemLoader.Spawn("cat_tower_1", parent, pos, yaw);
            var t = go.AddComponent<CatTower>();
            var info = JsonUtility.FromJson<Info>(ItemLoader.InfoText("cat_tower_1") ?? "{}");
            if (info.anchors.decks != null && info.anchors.decks.Length > 0) t.DeckHeight = info.anchors.decks[0].y;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.11f, 0f);
            col.size = new Vector3(0.8f, 0.22f, 1.08f);
            return t;
        }
    }

    /// <summary>
    /// 장난감 같은 작은 섬. 잔디 원판 + 흙 + 나무 마루, 덤불, 나무, 꽃.
    /// 고양이(몸길이 약 1 m)와 용품 크기에 맞춘 비율.
    /// </summary>
    public static class IslandBuilder
    {
        public const float Radius = 6.6f;

        public static Transform Build(Transform parent)
        {
            var root = Shapes.Pivot(parent, "Island", Vector3.zero);
            var sphere = MeshFactory.Sphere();
            float D = Radius * 2f;

            Shapes.Make(root, "Grass", MeshFactory.IslandTop(), Palette.Grass, Vector3.zero, new Vector3(D, 1.4f, D));
            Shapes.Make(root, "Soil", MeshFactory.IslandSoil(), Palette.Soil, new Vector3(0f, -0.07f, 0f), new Vector3(D - 0.1f, 2.6f, D - 0.1f), default, false);

            // 마루 (집 안 자리)
            Shapes.Make(root, "Deck", MeshFactory.RoundedCylinder(0.04f), Palette.Wood, new Vector3(0f, -0.046f, 0.5f), new Vector3(7.6f, 0.05f, 7.6f));
            Shapes.Make(root, "Rug", MeshFactory.RoundedCylinder(0.06f), Palette.Mint, new Vector3(0f, -0.028f, -0.5f), new Vector3(3.4f, 0.04f, 3.4f), default, false);

            var rng = new System.Random(21);
            for (int i = 0; i < 11; i++)
            {
                float a = Mathf.Lerp(15f, 165f, i / 10f) * Mathf.Deg2Rad;
                float r = Radius - 0.9f + (float)rng.NextDouble() * 0.4f;
                var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float s = 0.9f + (float)rng.NextDouble() * 0.6f;
                var bush = Shapes.Pivot(root, "Bush" + i, pos);
                Shapes.Make(bush, "A", sphere, Palette.GrassDark, new Vector3(0f, s * 0.35f, 0f), new Vector3(s, s * 0.8f, s));
                Shapes.Make(bush, "B", sphere, Palette.GrassDark, new Vector3(s * 0.35f, s * 0.25f, -s * 0.1f), new Vector3(s * 0.7f, s * 0.6f, s * 0.7f));
            }

            var tree = Shapes.Pivot(root, "Tree", new Vector3(-4.0f, 0f, 4.0f));
            Shapes.Make(tree, "Trunk", MeshFactory.Capsule(0.2f), Palette.Chestnut, new Vector3(0f, 0.9f, 0f), new Vector3(0.42f, 2.0f, 0.42f));
            Shapes.Make(tree, "Leaf1", sphere, Palette.GrassDark, new Vector3(0f, 2.4f, 0f), new Vector3(2.3f, 1.9f, 2.3f));
            Shapes.Make(tree, "Leaf2", sphere, Palette.Grass, new Vector3(0.6f, 2.9f, -0.35f), new Vector3(1.4f, 1.2f, 1.4f));

            Color[] flowerColors = { Palette.StrawberryMilk, Palette.Butter, Palette.Lavender, Palette.Peach };
            for (int i = 0; i < 22; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 4.3f + (float)rng.NextDouble() * 1.8f;
                var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (pos.z < -2.5f && Mathf.Abs(pos.x) < 2.8f) continue; // 카메라 앞은 비워 둠
                var f = Shapes.Pivot(root, "Flower" + i, pos);
                Shapes.Make(f, "Stem", MeshFactory.Capsule(0.3f), Palette.GrassDark, new Vector3(0f, 0.1f, 0f), new Vector3(0.04f, 0.22f, 0.04f), default, false);
                Shapes.Make(f, "Petal", sphere, flowerColors[i % flowerColors.Length], new Vector3(0f, 0.24f, 0f), new Vector3(0.16f, 0.1f, 0.16f), default, false);
                Shapes.Make(f, "Center", sphere, Palette.Cream, new Vector3(0f, 0.285f, 0f), new Vector3(0.06f, 0.04f, 0.06f), default, false);
            }

            var ground = Shapes.Pivot(root, "GroundCollider", new Vector3(0f, -0.05f, 0f));
            var gc = ground.gameObject.AddComponent<BoxCollider>();
            gc.size = new Vector3(D, 0.1f, D);
            return root;
        }
    }
}
