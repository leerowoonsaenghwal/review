using System.Collections.Generic;
using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 손가락이 무엇을 하는지 정한다.
    /// 고양이 위에서 문지르기 = 쓰다듬기 / 고양이 톡 = 야옹 / 그릇 톡 = 사료 / 방석 톡 = 꾹
    /// 땅 톡 = 부르기 / 빈 곳 끌기 = 시점 회전 / 두 손가락 = 확대
    /// </summary>
    public class TouchRouter : MonoBehaviour
    {
        enum Mode { None, Pet, Bowl, Cushion, Tower, Background, CameraDrag, Pinch }

        public CatBrain cat;
        public FoodBowl bowl;
        public Cushion cushion;
        public CatTower tower;
        public IslandCamera islandCamera;
        public CatAudio audioOut;
        public IPointerSource source = new InputSystemPointers();

        readonly List<PointerSample> samples = new List<PointerSample>();
        Mode mode;
        int activeId;
        Vector2 downPos, lastPos;
        float downTime, strokeSpeed, pinchStart, pinchLast;
        bool movedFar;
        Camera cam;

        public float StrokeSpeed => strokeSpeed;
        public PetZone LastZone { get; private set; }

        void Start() { cam = islandCamera ? islandCamera.GetComponent<Camera>() : Camera.main; }

        void Update() { Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null || dt <= 0f) return;
            source.Collect(samples);
            float sh = Mathf.Max(1f, Screen.height);

            float scroll = source.ScrollDelta;
            if (Mathf.Abs(scroll) > 0.01f && islandCamera) islandCamera.StepZoom(scroll > 0f ? 1 : -1);

            // 두 손가락: 확대
            if (samples.Count >= 2)
            {
                float d = (samples[0].position - samples[1].position).magnitude / sh;
                if (mode != Mode.Pinch) { EndCurrent(false); mode = Mode.Pinch; pinchStart = d; pinchLast = d; }
                pinchLast = d;
                return;
            }
            if (mode == Mode.Pinch)
            {
                if (samples.Count == 0)
                {
                    float ratio = pinchLast / Mathf.Max(0.001f, pinchStart);
                    if (islandCamera && Mathf.Abs(ratio - 1f) > 0.15f) islandCamera.StepZoom(ratio > 1f ? 1 : -1);
                    mode = Mode.None;
                }
                return;
            }

            if (samples.Count == 0)
            {
                if (mode != Mode.None) EndCurrent(true);
                cat?.SetPetInput(PetZone.None, 0f, Vector3.zero, false);
                return;
            }

            var p = samples[0];
            if (mode == Mode.None)
            {
                Begin(p);
                return;
            }

            // 계속 누르는 중
            Vector2 delta = p.position - lastPos;
            lastPos = p.position;
            if ((p.position - downPos).magnitude / sh > GameConfig.TapMaxMove) movedFar = true;

            switch (mode)
            {
                case Mode.Pet:
                {
                    float inst = delta.magnitude / sh / dt;
                    strokeSpeed = Mathf.Lerp(strokeSpeed, inst, 1f - Mathf.Exp(-14f * dt));
                    var ray = cam.ScreenPointToRay(p.position);
                    PetZone zone = PetZone.None;
                    Vector3 world;
                    if (Physics.Raycast(ray, out var hit, 100f) && cat.Rig.IsCatCollider(hit.collider))
                    {
                        zone = cat.Rig.ClassifyHit(hit.collider, hit.point);
                        world = hit.point;
                    }
                    else world = ray.GetPoint(Vector3.Distance(cam.transform.position, cat.transform.position));
                    LastZone = zone;
                    cat.SetPetInput(zone, strokeSpeed, world, true);
                    break;
                }
                case Mode.Background:
                    if (movedFar) mode = Mode.CameraDrag;
                    break;
                case Mode.CameraDrag:
                    if (islandCamera) islandCamera.Orbit(delta.x / sh * 160f);
                    break;
            }
        }

        void Begin(PointerSample p)
        {
            activeId = p.id;
            downPos = lastPos = p.position;
            downTime = Time.time;
            movedFar = false;
            strokeSpeed = 0f;
            cat?.NoteUserActivity();

            var ray = cam.ScreenPointToRay(p.position);
            mode = Mode.Background;
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                if (cat && cat.Rig.IsCatCollider(hit.collider)) mode = Mode.Pet;
                else if (bowl && hit.collider.gameObject == bowl.gameObject) mode = Mode.Bowl;
                else if (cushion && hit.collider.gameObject == cushion.gameObject) mode = Mode.Cushion;
                else if (tower && hit.collider.gameObject == tower.gameObject) mode = Mode.Tower;
            }
        }

        void EndCurrent(bool allowTap)
        {
            bool tap = allowTap && !movedFar && Time.time - downTime <= GameConfig.TapMaxDuration;
            switch (mode)
            {
                case Mode.Pet:
                    if (tap) cat.OnTapCat();
                    cat.SetPetInput(PetZone.None, 0f, Vector3.zero, false);
                    break;
                case Mode.Bowl:
                    if (tap || !movedFar)
                    {
                        bowl.Fill();
                        audioOut?.Kibble();
                        Haptics.Impact(ImpactStyle.Light, 0.5f);
                        cat?.OnBowlFilled();
                    }
                    break;
                case Mode.Cushion:
                    if (!movedFar)
                    {
                        cushion.Poke();
                        audioOut?.Pop();
                        Haptics.Impact(ImpactStyle.Soft, 0.5f);
                        cat?.OnCushionTapped();
                    }
                    break;
                case Mode.Tower:
                    if (!movedFar) cat?.OnTowerTapped();
                    break;
                case Mode.Background:
                    if (tap)
                    {
                        var ray = cam.ScreenPointToRay(lastPos);
                        if (Physics.Raycast(ray, out var hit, 100f)) cat?.OnTapGround(hit.point);
                    }
                    break;
            }
            mode = Mode.None;
            strokeSpeed = 0f;
            LastZone = PetZone.None;
        }
    }
}
