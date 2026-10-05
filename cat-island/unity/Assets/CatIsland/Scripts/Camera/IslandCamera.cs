using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 장난감 상자를 들여다보는 시점: 약 40도로 내려다보고 화각이 좁다 (기획서 3부 6장).
    /// 좌우 90도 회전, 확대 2단계. 고양이를 부드럽게 따라간다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IslandCamera : MonoBehaviour
    {
        public Transform follow;
        Vector3 home = new Vector3(0f, 0.5f, 0.4f);    // (지금 보는 구역의 가운데)
        /// <summary>구역 이동: 집 안 ↔ 마당 (마당에서는 고양이를 따라가지 않고 마당 전체를 본다).</summary>
        public void ShowZone(CatIsland.Game.Zone z) { home = z == CatIsland.Game.Zone.Indoor ? new Vector3(0f, 0.5f, 0.4f) : IslandBuilder.YardCenter + new Vector3(0, .5f, .2f); followOn = z == CatIsland.Game.Zone.Indoor; }
        bool followOn = true;
        public float pitch = 40f;
        public float fov = 28f;
        public float[] distances = { 17f, 8.5f };   // 0 = 멀리, 1 = 가까이
        public int zoomLevel = 1;
        public float yawLimit = 45f;

        float yaw, yawVel, dist;
        Vector3 focus;
        Camera cam;

        public float Yaw => yaw;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = fov;
            dist = distances[zoomLevel];
            focus = new Vector3(0f, 0.5f, 0.4f);
            Apply();
        }

        public void Orbit(float deg)
        {
            yaw = Mathf.Clamp(yaw + deg, -yawLimit - 8f, yawLimit + 8f);
            yawVel = 0f;
        }

        public void StepZoom(int dir)
        {
            zoomLevel = Mathf.Clamp(zoomLevel + dir, 0, distances.Length - 1);
        }

        public void SnapNow()
        {
            dist = distances[zoomLevel];
            if (follow) focus = FocusFor(follow.position);
            Apply();
        }

        Vector3 FocusFor(Vector3 p)
        {
            // 가까이 볼 때만 고양이를 따라간다. 멀리 보면 섬 전체
            float w = !followOn ? 0f : zoomLevel == distances.Length - 1 ? 0.75f : 0.2f;
            Vector3 f = Vector3.Lerp(home, new Vector3(p.x, 0.5f + p.y, p.z), w);
            return f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            // 회전 한계 밖이면 살짝 되돌아옴
            float clamped = Mathf.Clamp(yaw, -yawLimit, yawLimit);
            yaw = Mathf.SmoothDamp(yaw, clamped, ref yawVel, 0.15f);
            dist = Mathf.Lerp(dist, distances[zoomLevel], 1f - Mathf.Exp(-6f * dt));
            if (follow) focus = Vector3.Lerp(focus, FocusFor(follow.position), 1f - Mathf.Exp(-2.5f * dt));
            Apply();
        }

        void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = focus - rot * Vector3.forward * dist;
            WorldStyle.Apply(focus, rot * Vector3.forward, dist);   // 둥근 세상 휨과 안개를 이 시점에 맞춘다
        }
    }
}
