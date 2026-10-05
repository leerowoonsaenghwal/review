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
        public float pitch = 40f;
        public float fov = 28f;
        public float[] distances = { 11.5f, 7.2f };   // 0 = 멀리, 1 = 가까이
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
            focus = new Vector3(0f, 0.3f, 0.1f);
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
            float w = zoomLevel == distances.Length - 1 ? 0.75f : 0.2f;
            Vector3 f = Vector3.Lerp(new Vector3(0f, 0.3f, 0.1f), new Vector3(p.x, 0.3f, p.z), w);
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
        }
    }
}
