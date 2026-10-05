using UnityEngine;

namespace CatIsland
{
    public static class Shapes
    {
        public static Transform Pivot(Transform parent, string name, Vector3 localPos, Vector3 localEuler = default)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localEulerAngles = localEuler;
            return t;
        }

        public static Transform Make(Transform parent, string name, Mesh mesh, Color color,
            Vector3 localPos, Vector3 localScale, Vector3 localEuler = default, bool castShadow = true)
        {
            var t = Pivot(parent, name, localPos, localEuler);
            t.localScale = localScale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = Materials.Soft(color);
            r.shadowCastingMode = castShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        public static Transform Make(Transform parent, string name, Mesh mesh, Material mat,
            Vector3 localPos, Vector3 localScale, Vector3 localEuler = default, bool castShadow = true)
        {
            var t = Make(parent, name, mesh, Color.white, localPos, localScale, localEuler, castShadow);
            t.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return t;
        }
    }
}
