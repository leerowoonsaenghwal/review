using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>밥그릇. 누르면 사료가 채워지고, 고양이가 먹으면 줄어든다.</summary>
    public class FoodBowl : MonoBehaviour
    {
        public float Food { get; private set; }        // 0..1
        public bool HasFood => Food > 0.02f;
        public Vector3 EatSpot => transform.position + transform.forward * -0.42f;
        readonly List<Transform> kibbles = new List<Transform>();
        float wobble, wobbleVel;
        Transform visual;

        public static FoodBowl Create(Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject("FoodBowl");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
            var b = go.AddComponent<FoodBowl>();
            b.Build();
            return b;
        }

        void Build()
        {
            visual = Shapes.Pivot(transform, "Visual", Vector3.zero);
            Shapes.Make(visual, "Bowl", MeshFactory.Bowl(), Palette.Peach, Vector3.zero, new Vector3(0.36f, 0.13f, 0.36f));
            // 그릇 안 사료 알갱이: 동그란 더미
            var rng = new System.Random(5);
            var sphere = MeshFactory.Sphere();
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * 0.1f;
                float h = 0.05f + (0.1f - r) * 0.35f + (float)rng.NextDouble() * 0.01f;
                var k = Shapes.Make(visual, "Kibble" + i, sphere, i % 3 == 0 ? Palette.Chestnut : Palette.Kibble,
                    new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r), Vector3.one * 0.032f, new Vector3(0f, i * 37f, 20f), false);
                kibbles.Add(k);
            }
            var col = gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.06f, 0f);
            col.radius = 0.24f; // 손가락으로 누르기 쉽게 실제보다 크게
            SetFood(0f);
        }

        public void Fill()
        {
            SetFood(1f);
            wobbleVel += 5f;
        }

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
            visual.localScale = new Vector3(1f + wobble * 0.08f, 1f - wobble * 0.12f, 1f + wobble * 0.08f);
        }
    }

    /// <summary>방석. 고양이가 꾹꾹이를 하고 식빵 자세로 잔다.</summary>
    public class Cushion : MonoBehaviour
    {
        public Vector3 Center => transform.position + Vector3.up * 0.09f;
        float squish, squishVel;
        Transform visual;

        public static Cushion Create(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Cushion");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var c = go.AddComponent<Cushion>();
            c.Build();
            return c;
        }

        void Build()
        {
            visual = Shapes.Pivot(transform, "Visual", Vector3.zero);
            Shapes.Make(visual, "Base", MeshFactory.RoundedCylinder(0.2f, 0.25f), Palette.StrawberryMilk, Vector3.zero, new Vector3(0.78f, 0.13f, 0.78f));
            Shapes.Make(visual, "Rim", MeshFactory.RoundedCylinder(0.24f, 0.4f), Palette.Peach, new Vector3(0f, 0.02f, 0f), new Vector3(0.62f, 0.085f, 0.62f));
            var col = gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.05f, 0f);
            col.radius = 0.4f;
        }

        public void Poke() { squishVel -= 4f; }

        void Update()
        {
            squishVel += (-squish * 200f - squishVel * 9f) * Time.deltaTime;
            squish += squishVel * Time.deltaTime;
            visual.localScale = new Vector3(1f - squish * 0.1f, 1f + squish * 0.4f, 1f - squish * 0.1f);
        }
    }

    /// <summary>장난감 같은 작은 섬. 잔디 원판 + 흙 + 나무 마루, 덤불, 나무, 꽃.</summary>
    public static class IslandBuilder
    {
        public static Transform Build(Transform parent)
        {
            var root = Shapes.Pivot(parent, "Island", Vector3.zero);
            var sphere = MeshFactory.Sphere();

            Shapes.Make(root, "Grass", MeshFactory.IslandTop(), Palette.Grass, Vector3.zero, new Vector3(8.4f, 1f, 8.4f));
            Shapes.Make(root, "Soil", MeshFactory.IslandSoil(), Palette.Soil, new Vector3(0f, -0.05f, 0f), new Vector3(8.3f, 1.6f, 8.3f), default, false);

            // 마루 (집 안 자리)
            Shapes.Make(root, "Deck", MeshFactory.RoundedCylinder(0.04f), Palette.Wood, new Vector3(0f, -0.046f, 0.2f), new Vector3(4.2f, 0.05f, 4.2f));
            Shapes.Make(root, "Rug", MeshFactory.RoundedCylinder(0.06f), Palette.Mint, new Vector3(0.1f, -0.028f, -0.4f), new Vector3(1.9f, 0.04f, 1.9f), default, false);

            // 덤불과 나무: 뒤쪽 가장자리에 둘러 섬 느낌
            var rng = new System.Random(21);
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(20f, 160f, i / 8f) * Mathf.Deg2Rad;
                float r = 3.45f + (float)rng.NextDouble() * 0.3f;
                var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float s = 0.5f + (float)rng.NextDouble() * 0.35f;
                var bush = Shapes.Pivot(root, "Bush" + i, pos);
                Shapes.Make(bush, "A", sphere, Palette.GrassDark, new Vector3(0f, s * 0.35f, 0f), new Vector3(s, s * 0.8f, s));
                Shapes.Make(bush, "B", sphere, Palette.GrassDark, new Vector3(s * 0.35f, s * 0.25f, -s * 0.1f), new Vector3(s * 0.7f, s * 0.6f, s * 0.7f));
            }

            var tree = Shapes.Pivot(root, "Tree", new Vector3(-2.3f, 0f, 2.4f));
            Shapes.Make(tree, "Trunk", MeshFactory.Capsule(0.2f), Palette.Chestnut, new Vector3(0f, 0.5f, 0f), new Vector3(0.24f, 1.1f, 0.24f));
            Shapes.Make(tree, "Leaf1", sphere, Palette.GrassDark, new Vector3(0f, 1.35f, 0f), new Vector3(1.3f, 1.1f, 1.3f));
            Shapes.Make(tree, "Leaf2", sphere, Palette.Grass, new Vector3(0.35f, 1.6f, -0.2f), new Vector3(0.8f, 0.7f, 0.8f));

            Color[] flowerColors = { Palette.StrawberryMilk, Palette.Butter, Palette.Lavender, Palette.Peach };
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 2.5f + (float)rng.NextDouble() * 1.2f;
                var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (pos.z < -1.5f && Mathf.Abs(pos.x) < 1.6f) continue; // 카메라 앞은 비워 둠
                var f = Shapes.Pivot(root, "Flower" + i, pos);
                Shapes.Make(f, "Stem", MeshFactory.Capsule(0.3f), Palette.GrassDark, new Vector3(0f, 0.06f, 0f), new Vector3(0.025f, 0.13f, 0.025f), default, false);
                Shapes.Make(f, "Petal", sphere, flowerColors[i % flowerColors.Length], new Vector3(0f, 0.14f, 0f), new Vector3(0.09f, 0.06f, 0.09f), default, false);
                Shapes.Make(f, "Center", sphere, Palette.Cream, new Vector3(0f, 0.165f, 0f), new Vector3(0.035f, 0.025f, 0.035f), default, false);
            }

            // 땅 클릭용 충돌체 (고양이 부르기)
            var ground = Shapes.Pivot(root, "GroundCollider", new Vector3(0f, -0.05f, 0f));
            var gc = ground.gameObject.AddComponent<BoxCollider>();
            gc.size = new Vector3(8.4f, 0.1f, 8.4f);
            return root;
        }
    }
}
