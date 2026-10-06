namespace CatIsland.Tests
{
    using UnityEngine;

    /// <summary>테스트 중 매 프레임: 위치·크기·경계가 NaN 이거나 터무니없는 물체를 처음 한 번 이름과 함께 남긴다 (엔진 경고는 어떤 물체인지 말해 주지 않는다).</summary>
    public class BoundsWatch : MonoBehaviour
    {
        bool reported;
        void LateUpdate()
        {
            if (reported) return;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var p = r.transform.position; var s = r.transform.lossyScale;
                bool bad = float.IsNaN(p.x + p.y + p.z + s.x + s.y + s.z) || p.magnitude > 1e4f || s.magnitude > 1e4f;
                if (r is SkinnedMeshRenderer smr) foreach (var b in smr.bones) if (b && float.IsNaN(b.position.x + b.rotation.x + b.lossyScale.x)) { bad = true; Debug.Log($"[BoundsWatch] bone {b.name} NaN pos {b.position} rot {b.rotation}"); break; }
                if (bad) { reported = true; Debug.Log($"[BoundsWatch] {r.name} parent {(r.transform.parent ? r.transform.parent.name : "-")} root {r.transform.root.name} pos {p} scale {s} t={Time.time:F2}"); return; }
            }
        }
    }
}
