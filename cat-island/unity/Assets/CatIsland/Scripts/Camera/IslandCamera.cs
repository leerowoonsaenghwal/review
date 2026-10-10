using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 장난감 상자를 들여다보는 시점: 약 40도로 내려다보고 화각이 좁다 (기획서 3부 6장).
    /// 360도 회전, 확대 2단계. 고양이를 부드럽게 따라간다.
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
        public float[] distances = { 24f, 8.5f };   // 0 = 멀리(넓어진 섬 전체), 1 = 가까이
        public bool Petting;
        public int zoomLevel = 1;

        float yaw, yawVel, dist;
        Vector3 focus;
        Camera cam;

        public float Yaw => yaw;
        /// <summary>바닥 깔기 모드: 정한 곳(ManualFocus)을 정한 거리에서 본다. 끄면 다시 고양이를 따라간다.</summary>
        public bool Manual; public Vector3 ManualFocus; public float ManualDist = 11f;
        /// <summary>화면의 한 점 아래 땅 (높이 0) 의 세상 좌표.</summary>
        public bool GroundAt(Vector2 screen, out Vector3 p)
        {
            var ray = GetComponent<Camera>().ScreenPointToRay(screen); p = default;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float t)) return false;
            p = ray.GetPoint(t); return true;
        }

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = fov;
            dist = distances[zoomLevel];
            focus = new Vector3(0f, 0.5f, 0.4f);
            Apply();
        }

        /// <summary>손가락으로 돌리기: 360도 자유 (2026-10-10 사용자 요청, 예전 좌우 45도). 손을 떼면 남은 힘만큼 부드럽게 더 돈다.</summary>
        public void Orbit(float deg)
        {
            yaw = Mathf.Repeat(yaw + deg + 180f, 360f) - 180f;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-3f);
            spin = Mathf.Lerp(spin, deg / dt, .5f); orbitAt = Time.unscaledTime;
            yawVel = 0f;
        }
        float spin, orbitAt;
        /// <summary>점검·스크린샷: 방향을 바로 정한다.</summary>
        public void SetYaw(float deg) { yaw = Mathf.Repeat(deg + 180f, 360f) - 180f; spin = 0f; yawVel = 0f; }   // (돌리는 빠르기 도/초, 마지막으로 돌린 때)

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
            w = Mathf.Lerp(w, 1f, frameK);   // (고양이 만들기: 고른 고양이를 한가운데로)
            Vector3 f = Vector3.Lerp(home, new Vector3(p.x, 0.5f + p.y, p.z), w);
            return f;
        }

        // ---- 사진 모드: 자유 시점 (360도 돌리기, 연속 확대, 고양이 눈높이). 끝나면 원래 시점으로
        public bool PhotoMode { get; private set; }
        float photoYaw, photoPitch = 25f, photoDist = 4f; bool eyeLevel;
        public void BeginPhoto() { PhotoMode = true; photoYaw = yaw; photoPitch = 25f; photoDist = Mathf.Clamp(dist * .6f, 2.2f, 9f); eyeLevel = false; }
        public void EndPhoto() { PhotoMode = false; yaw = Mathf.Repeat(photoYaw + 180f, 360f) - 180f; spin = 0f; }
        public void PhotoOrbit(float dYaw, float dPitch) { photoYaw += dYaw; if (!eyeLevel) photoPitch = Mathf.Clamp(photoPitch + dPitch, 5f, 70f); }
        public void PhotoZoom(float k) { photoDist = Mathf.Clamp(photoDist * k, 1.6f, 14f); }
        public void SetEyeLevel(bool on) { eyeLevel = on; }
        /// <summary>사진 시점을 정해 두기 (앱스토어 스크린샷 StoreShots).</summary>
        public void FramePhoto(float yawDeg, float pitchDeg, float distance) { photoYaw = yawDeg; photoPitch = Mathf.Clamp(pitchDeg, 5f, 70f); photoDist = Mathf.Clamp(distance, 1.6f, 14f); }
        public bool EyeLevel => eyeLevel;

        void LateUpdate()
        {
            if (PhotoMode)
            {
                Vector3 target = follow ? follow.position + Vector3.up * (eyeLevel ? .45f : .4f) : focus;
                float pitchNow = eyeLevel ? 4f : photoPitch;
                focus = Vector3.Lerp(focus, target, 1f - Mathf.Exp(-5f * Time.deltaTime));
                var rot = Quaternion.Euler(pitchNow, photoYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-10f * Time.deltaTime));
                transform.position = focus - transform.rotation * Vector3.forward * photoDist;
                if (transform.position.y < .25f) transform.position = new Vector3(transform.position.x, .25f, transform.position.z);
                WorldStyle.Apply(focus, transform.forward, photoDist * 1.6f);
                return;
            }
            float dt = Time.deltaTime;
            if (Manual)
            {
                // (바닥 깔기: 고양이를 따라가지 않고 이용자가 옮기는 곳을 본다)
                focus = Vector3.Lerp(focus, ManualFocus, 1f - Mathf.Exp(-10f * dt)); dist = Mathf.Lerp(dist, ManualDist, 1f - Mathf.Exp(-6f * dt));
                spin = 0f; yaw = Mathf.SmoothDampAngle(yaw, 0f, ref yawVel, .2f); frameK = Mathf.Lerp(frameK, 0f, 1f - Mathf.Exp(-5f * dt)); Apply(); return;
            }
            // 낮게 열린 창(고양이 만들기) 위로 고양이를 가까이 잡는다
            float top = CatIsland.UI.GameUI.Instance ? CatIsland.UI.GameUI.Instance.SheetTop : 1f;
            frameK = Mathf.Lerp(frameK, top < .7f ? 1f : 0f, 1f - Mathf.Exp(-5f * dt)); if (top < .7f) frameTop = top;
            // 손을 뗀 뒤 남은 회전 (빠르게 밀면 더 멀리, 0.4초쯤 걸려 멈춘다)
            if (Time.unscaledTime - orbitAt > .06f && Mathf.Abs(spin) > 1f)
            { yaw = Mathf.Repeat(yaw + spin * Time.unscaledDeltaTime + 180f, 360f) - 180f; spin *= Mathf.Exp(-7f * Time.unscaledDeltaTime); }
            else if (Time.unscaledTime - orbitAt > .06f) spin = 0f;
            dist = Mathf.Lerp(dist, distances[zoomLevel] * (Petting ? .82f : 1f) * Mathf.Lerp(1f, .72f, frameK), 1f - Mathf.Exp((Petting ? -1.2f : -6f) * dt));   // (쓰다듬을 때 천천히 가까이)
            if (follow) focus = Vector3.Lerp(focus, FocusFor(follow.position), 1f - Mathf.Exp(-2.5f * dt));
            Apply();
        }

        float frameK, frameTop = .57f;
        void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            // (창 위 띠의 가운데(위 숫자판 아래)에 고양이가 오게: 시점을 화면 위쪽 방향의 반대로 옮긴다)
            float above = ((frameTop + .9f) * .5f - .5f) * 2f * dist * Mathf.Tan(fov * .5f * Mathf.Deg2Rad) * frameK;
            transform.position = focus - rot * Vector3.up * above - rot * Vector3.forward * dist;
            WorldStyle.Apply(focus, rot * Vector3.forward, dist);   // 둥근 세상 휨과 안개를 이 시점에 맞춘다
        }
    }
}
