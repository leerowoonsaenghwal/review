using System;
using UnityEngine;

namespace CatIsland
{
    public enum CatState { Idle, Wander, GoToBowl, WaitAtBowl, Eat, GoToCushion, Knead, Sleep, Petted, BellyUp, Nip, Invite, Called }

    /// <summary>
    /// 고양이의 행동. 상태, 성격(좋아하는 곳), 쓰다듬기 입력에 따라 스스로 움직인다.
    /// 세 가지 약속: 벌주지 않는다 / 고양이가 주인공 / 매일 조금씩 다르다.
    /// </summary>
    [RequireComponent(typeof(CatRig))]
    public class CatBrain : MonoBehaviour
    {
        public CatRig Rig { get; private set; }
        public CatNeeds Needs { get; private set; }
        public PetLogic Pet { get; private set; }
        public Affection Affection { get; private set; }
        public CatState State { get; private set; } = CatState.Idle;
        public float StateTime { get; private set; }
        public StatusBubble Bubble { get; private set; }
        public float Speed { get; private set; }

        public FoodBowl bowl;
        public Cushion cushion;
        public CatAudio audioOut;
        public Transform cam;

        public event Action<int> LevelUp;
        public event Action FirstPetToday;
        public event Action Nipped;
        public event Action BelliedUp;

        // 쓰다듬기 입력 (TouchRouter가 매 프레임 넣음)
        PetZone petZone;
        float strokeSpeed;
        Vector3 pointerWorld;
        bool touchingCat;
        float lastPetTime = -99f, lastUserActivity, heartAccum, inviteCooldown = 4f, idleDecide = 1.5f, crunchTimer, sleepBubbleTimer;
        Vector3 moveTarget;
        Vector3? faceTarget;
        bool firstPetChecked;
        float eyeSlitTimer;
        float savedAffection;
        float heightY;

        const string PrefAffection = "cat.affection";
        const string PrefLastPetDay = "cat.lastPetDay";

        void Awake()
        {
            Rig = GetComponent<CatRig>();
            Rig.Build();
            Needs = new CatNeeds();
            // 이 고양이는 턱과 볼을 가장 좋아하고, 이마와 등도 좋아한다. 엉덩이는 그럭저럭.
            Pet = new PetLogic(ZonePref.Liked, ZonePref.Loved, ZonePref.Loved, ZonePref.Liked, ZonePref.Neutral);
            Affection = new Affection(PlayerPrefs.GetFloat(PrefAffection, 0f));
            savedAffection = Affection.Points;
            Bubble = StatusBubble.Create(Rig.BubbleAnchor);
            moveTarget = transform.position;
        }

        // ------------------------------------------------------------ 입력

        public void SetPetInput(PetZone zone, float speed, Vector3 world, bool touching)
        {
            petZone = zone;
            strokeSpeed = speed;
            pointerWorld = world;
            touchingCat = touching;
            if (touching) lastUserActivity = Time.time;
        }

        public void NoteUserActivity() { lastUserActivity = Time.time; }

        public void OnTapCat()
        {
            NoteUserActivity();
            if (State == CatState.Sleep)
            {
                eyeSlitTimer = 0.6f;
                Rig.earsBack = 0f;
                return;
            }
            audioOut?.Meow();
            Rig.mouthOpen = 1f;
            Invoke(nameof(CloseMouth), 0.35f);
            Rig.Squash(0.5f);
            Haptics.Impact(ImpactStyle.Soft, 0.6f);
            if (State == CatState.Wander || State == CatState.Idle || State == CatState.Invite) Enter(CatState.Idle);
            faceTarget = cam ? cam.position : (Vector3?)null;
        }

        void CloseMouth() { Rig.mouthOpen = 0f; }

        public void OnTapGround(Vector3 world)
        {
            NoteUserActivity();
            if (!IsAwakeAndFree()) return;
            moveTarget = ClampToIsland(world);
            audioOut?.Chirp();
            Enter(CatState.Called);
        }

        public void OnBowlFilled()
        {
            NoteUserActivity();
            if (State == CatState.Sleep) { eyeSlitTimer = 0.8f; return; }
            if (Needs.Hunger < 0.9f && State != CatState.Eat && State != CatState.BellyUp && State != CatState.Nip)
                Enter(CatState.GoToBowl);
        }

        public void OnCushionTapped()
        {
            NoteUserActivity();
            if (State == CatState.Sleep) return;
            if (IsAwakeAndFree() && Needs.Energy < 0.7f) Enter(CatState.GoToCushion);
        }

        bool IsAwakeAndFree() =>
            State == CatState.Idle || State == CatState.Wander || State == CatState.Invite || State == CatState.Called || State == CatState.WaitAtBowl;

        // ------------------------------------------------------------ 갱신

        void Update() { Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            StateTime += dt;

            bool sleeping = State == CatState.Sleep;
            bool eating = State == CatState.Eat;
            Needs.Tick(dt, eating, sleeping);

            // 쓰다듬기
            var f = Pet.Update(touchingCat ? petZone : PetZone.None, strokeSpeed, dt);
            if (f.Enjoying) lastPetTime = Time.time;
            HandlePetFrame(f);

            switch (State)
            {
                case CatState.Idle: TickIdle(dt); break;
                case CatState.Wander: TickMoveTo(dt, GameConfig.WalkSpeed, CatState.Idle); break;
                case CatState.Called: TickMoveTo(dt, GameConfig.TrotSpeed, CatState.Idle); break;
                case CatState.Invite: TickInvite(dt); break;
                case CatState.GoToBowl: TickGoToBowl(dt); break;
                case CatState.WaitAtBowl: TickWaitAtBowl(dt); break;
                case CatState.Eat: TickEat(dt); break;
                case CatState.GoToCushion: TickGoToCushion(dt); break;
                case CatState.Knead: TickKnead(dt); break;
                case CatState.Sleep: TickSleep(dt); break;
                case CatState.Petted: TickPetted(dt); break;
                case CatState.BellyUp: TickBellyUp(dt); break;
                case CatState.Nip: TickNip(dt); break;
            }

            ApplyFace(dt);
            ApplyHeight(dt);
            UpdateExpression(dt);
            UpdateBubble();
            SaveIfNeeded();
        }

        void HandlePetFrame(PetFrame f)
        {
            if (f.AffectionGained > 0f)
            {
                if (!firstPetChecked)
                {
                    firstPetChecked = true;
                    string today = DateTime.Now.ToString("yyyy-MM-dd");
                    if (PlayerPrefs.GetString(PrefLastPetDay, "") != today)
                    {
                        PlayerPrefs.SetString(PrefLastPetDay, today);
                        AddAffection(GameConfig.FirstPetOfDayBonus);
                        FxPool.Instance?.Burst(Icon.Heart, Rig.Head.position + Vector3.up * 0.2f, 6, 0.2f, 0.16f);
                        audioOut?.Chirp();
                        FirstPetToday?.Invoke();
                    }
                }
                AddAffection(f.AffectionGained);
                heartAccum += f.AffectionGained;
                if (heartAccum >= 2.2f)
                {
                    heartAccum = 0f;
                    FxPool.Instance?.Burst(Icon.Heart, Rig.Head.position + Vector3.up * 0.18f, 1, 0.12f);
                    Haptics.Impact(ImpactStyle.Soft, 0.35f);
                }
            }

            bool canReact = State != CatState.Sleep && State != CatState.Eat && State != CatState.Knead;
            if (f.Enjoying && canReact && State != CatState.Petted && State != CatState.BellyUp && State != CatState.Nip)
                Enter(CatState.Petted);

            if (f.Nipped && State != CatState.Sleep)
            {
                Enter(CatState.Nip);
                Nipped?.Invoke();
            }
            if (f.BellyUp && canReact && State != CatState.Nip)
            {
                Enter(CatState.BellyUp);
                BelliedUp?.Invoke();
            }

            float purr = Pet.Purring ? Mathf.Clamp01(Pet.Pleasure * 1.2f) : 0f;
            audioOut?.SetPurr(purr);
            Haptics.SetPurr(touchingCat ? purr : 0f);
        }

        void AddAffection(float pts)
        {
            if (Affection.Add(pts))
            {
                FxPool.Instance?.Burst(Icon.Sparkle, Rig.Head.position + Vector3.up * 0.25f, 5, 0.25f, 0.12f);
                LevelUp?.Invoke(Affection.Level);
            }
        }

        void SaveIfNeeded()
        {
            if (Affection.Points - savedAffection > 5f)
            {
                savedAffection = Affection.Points;
                PlayerPrefs.SetFloat(PrefAffection, Affection.Points);
            }
        }

        void OnApplicationPause(bool pause) { if (pause) PlayerPrefs.SetFloat(PrefAffection, Affection.Points); }
        void OnDisable() { if (Affection != null) PlayerPrefs.SetFloat(PrefAffection, Affection.Points); }

        void Enter(CatState s)
        {
            // 나가기 정리
            if (State == CatState.BellyUp) Rig.Squash(0.8f);
            State = s;
            StateTime = 0f;
            faceTarget = null;
            switch (s)
            {
                case CatState.Idle: idleDecide = UnityEngine.Random.Range(1.5f, 3.5f); break;
                case CatState.Wander: moveTarget = RandomDeckPoint(); break;
                case CatState.Invite: moveTarget = InviteSpot(); audioOut?.Chirp(); break;
                case CatState.Nip:
                    audioOut?.Nip();
                    Haptics.Impact(ImpactStyle.Medium, 0.7f);
                    Rig.mouthOpen = 1f;
                    break;
                case CatState.BellyUp:
                    Rig.bellyRollSign = RollTowardCamera();
                    audioOut?.Chirp();
                    FxPool.Instance?.Burst(Icon.Heart, Rig.Head.position + Vector3.up * 0.15f, 4, 0.2f, 0.15f);
                    Haptics.Impact(ImpactStyle.Light, 0.6f);
                    break;
                case CatState.Sleep: sleepBubbleTimer = 0f; break;
            }
        }

        // ---------------- 상태별

        void TickIdle(float dt)
        {
            Speed = Mathf.MoveTowards(Speed, 0f, dt * 4f);
            idleDecide -= dt;
            if (idleDecide > 0f) return;

            if (Needs.IsHungry) { Enter(bowl && bowl.HasFood ? CatState.GoToBowl : CatState.WaitAtBowl); return; }
            if (Needs.IsSleepy && cushion) { Enter(CatState.GoToCushion); return; }
            if (Time.time - lastUserActivity > GameConfig.IdleInviteDelay && inviteCooldown <= 0f && cam)
            {
                inviteCooldown = 18f;
                Enter(CatState.Invite);
                return;
            }
            Enter(UnityEngine.Random.value < 0.65f ? CatState.Wander : CatState.Idle);
        }

        void LateUpdate() { inviteCooldown -= Time.deltaTime; }

        bool MoveTowards(Vector3 target, float maxSpeed, float dt, float arriveDist = 0.05f)
        {
            Vector3 pos = transform.position;
            Vector3 to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            if (dist <= arriveDist) { Speed = Mathf.MoveTowards(Speed, 0f, dt * 5f); return true; }

            Vector3 dir = to / dist;
            // 그릇은 피해서 걷는다 (목표가 아닐 때)
            if (bowl && State != CatState.GoToBowl && State != CatState.WaitAtBowl)
            {
                Vector3 away = pos - bowl.transform.position; away.y = 0f;
                float d = away.magnitude;
                if (d < 0.55f && d > 0.001f) dir = (dir + away / d * (0.55f - d) * 4f).normalized;
            }

            float angle = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
            float turn = Mathf.Clamp(angle, -GameConfig.TurnSpeedDeg * dt, GameConfig.TurnSpeedDeg * dt);
            transform.Rotate(0f, turn, 0f);
            float align = Mathf.Clamp01(1f - Mathf.Abs(angle) / 75f);
            float targetSpeed = maxSpeed * align * Mathf.Clamp01(dist / 0.35f + 0.25f);
            Speed = Mathf.MoveTowards(Speed, targetSpeed, dt * 4f);
            Vector3 next = pos + transform.forward * Speed * dt;
            transform.position = ClampToIsland(next);
            return false;
        }

        void TickMoveTo(float dt, float speed, CatState then)
        {
            if (MoveTowards(moveTarget, speed, dt) || StateTime > 8f) Enter(then);
        }

        void TickInvite(float dt)
        {
            if (MoveTowards(moveTarget, GameConfig.WalkSpeed, dt))
            {
                faceTarget = cam ? cam.position : (Vector3?)null;
                if (StateTime > 9f) Enter(CatState.Idle);
            }
            else if (StateTime > 6f) Enter(CatState.Idle);
        }

        void TickGoToBowl(float dt)
        {
            if (!bowl) { Enter(CatState.Idle); return; }
            if (MoveTowards(bowl.EatSpot, GameConfig.TrotSpeed, dt, 0.06f))
            {
                faceTarget = bowl.transform.position;
                if (FacingAngle(bowl.transform.position) < 12f)
                    Enter(bowl.HasFood ? CatState.Eat : CatState.WaitAtBowl);
            }
            else if (StateTime > 10f) Enter(CatState.Idle);
        }

        void TickWaitAtBowl(float dt)
        {
            // 그릇 앞에 앉아 기다린다. 울지 않는다.
            if (!bowl) { Enter(CatState.Idle); return; }
            if (MoveTowards(bowl.EatSpot, GameConfig.WalkSpeed, dt, 0.08f))
            {
                // 기다리는 동안 그릇과 카메라를 번갈아 본다
                faceTarget = bowl.transform.position;
                if (bowl.HasFood && FacingAngle(bowl.transform.position) < 15f) Enter(CatState.Eat);
            }
            if (!Needs.IsHungry && !bowl.HasFood && StateTime > 3f) Enter(CatState.Idle);
        }

        void TickEat(float dt)
        {
            Speed = 0f;
            faceTarget = bowl ? bowl.transform.position : (Vector3?)null;
            crunchTimer -= dt;
            if (crunchTimer <= 0f)
            {
                crunchTimer = UnityEngine.Random.Range(0.3f, 0.45f);
                audioOut?.Crunch();
            }
            if (bowl) bowl.Consume(GameConfig.EatGainPerSec * 0.8f * dt);
            if (Needs.Hunger >= 0.98f || (bowl && !bowl.HasFood))
            {
                AddAffection(GameConfig.AffectionPerMeal);
                FxPool.Instance?.Burst(Icon.Heart, Rig.Head.position + Vector3.up * 0.2f, 2, 0.1f);
                Rig.mouthOpen = 1f;
                Invoke(nameof(CloseMouth), 0.25f);
                // 배부르면 졸리다 (밥 → 방석에서 잠)
                Needs.SetForTest(Needs.Hunger, Mathf.Min(Needs.Energy, GameConfig.SleepyThreshold - 0.05f));
                Enter(cushion ? CatState.GoToCushion : CatState.Idle);
            }
        }

        void TickGoToCushion(float dt)
        {
            if (!cushion) { Enter(CatState.Idle); return; }
            Vector3 c = cushion.transform.position;
            if (MoveTowards(c, GameConfig.WalkSpeed, dt, 0.06f))
            {
                faceTarget = cam ? cam.position : (Vector3?)null;
                if (cam == null || FacingAngle(cam.position) < 25f)
                {
                    Rig.Squash(0.6f);
                    Enter(CatState.Knead);
                }
            }
            else if (StateTime > 12f) Enter(CatState.Idle);
        }

        void TickKnead(float dt)
        {
            Speed = 0f;
            faceTarget = cam ? cam.position : (Vector3?)null;
            if (StateTime > 2.4f) Enter(CatState.Sleep);
        }

        void TickSleep(float dt)
        {
            Speed = 0f;
            sleepBubbleTimer -= dt;
            if (Needs.Energy >= GameConfig.WakeEnergy && !touchingCat)
            {
                Rig.Squash(1f);
                audioOut?.Chirp();
                Enter(CatState.Idle);
            }
        }

        void TickPetted(float dt)
        {
            Speed = Mathf.MoveTowards(Speed, 0f, dt * 6f);
            if (Time.time - lastPetTime > 1.4f && !touchingCat) Enter(CatState.Idle);
        }

        void TickBellyUp(float dt)
        {
            Speed = 0f;
            // 계속 쓰다듬는 동안은 누워 있다. 믿음 시간이 끝난 뒤에도 배를 계속 만지면
            // 살짝 깨문다 (고양이 배의 함정). 손을 떼고 잠시 지나면 다시 일어난다.
            bool petRecently = Time.time - lastPetTime < 2.5f || touchingCat;
            if (!petRecently && StateTime > 2.5f) Enter(CatState.Petted);
        }

        void TickNip(float dt)
        {
            Speed = 0f;
            if (StateTime > 0.25f) Rig.mouthOpen = 0f;
            if (StateTime > 0.9f)
            {
                // 살짝 물러난다. 벌이 아니라 "거기는 싫어"라는 표현
                Vector3 away = transform.position - pointerWorld; away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
                moveTarget = ClampToIsland(transform.position + away.normalized * 0.35f);
                Enter(CatState.Called);
            }
        }

        // ---------------- 표정과 자세

        void UpdateExpression(float dt)
        {
            float p = Pet.Pleasure;
            var r = Rig;
            r.moveSpeed = Speed;

            bool sleep = State == CatState.Sleep;
            bool loafing = sleep || State == CatState.WaitAtBowl && Speed < 0.05f;
            r.loaf = sleep ? 1f : State == CatState.Knead ? 0.25f : (loafing ? 0.55f : 0f);
            r.knead = State == CatState.Knead ? 1f : 0f;
            r.eat = State == CatState.Eat ? 1f : 0f;
            r.bellyUp = State == CatState.BellyUp ? 1f : 0f;
            r.pawsUp = State == CatState.Nip && StateTime < 0.6f ? 1f : 0f;
            r.earsBack = State == CatState.Nip ? 0.6f : 0f;
            r.tailCurl = sleep ? 1f : 0f;
            r.sleepBreath = sleep ? 1f : 0f;

            eyeSlitTimer -= dt;
            float happyClose = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, p));
            if (sleep) r.eyeOpen = eyeSlitTimer > 0f ? 0.3f : 0f;
            else if (State == CatState.Knead) r.eyeOpen = 0.35f;
            else r.eyeOpen = 1f - happyClose * 0.95f;

            r.tailUp = sleep ? 0.3f : Mathf.Clamp01(0.45f + p * 0.55f + (State == CatState.Called || State == CatState.Invite ? 0.3f : 0f));
            r.tailWag = State == CatState.Nip ? 1f : p * 0.4f;

            // 쓰다듬는 손 쪽으로 머리를 기대고 갸웃
            bool petting = touchingCat && (State == CatState.Petted || State == CatState.BellyUp);
            if (petting && (petZone == PetZone.Forehead || petZone == PetZone.Cheek))
            {
                Vector3 local = transform.InverseTransformPoint(pointerWorld);
                r.headTilt = Mathf.Clamp(-local.x * 120f, -22f, 22f);
                r.lean = 1f;
                r.leanDir = Vector3.ClampMagnitude(new Vector3(local.x, 0f, local.z), 1f);
            }
            else
            {
                r.headTilt = State == CatState.WaitAtBowl ? Mathf.Sin(Time.time * 0.7f) * 10f : 0f;
                r.lean = 0f;
            }

            // 바라보기
            if (State == CatState.Eat || sleep) r.lookTarget = null;
            else if (petting && petZone == PetZone.Chin && cam) r.lookTarget = cam.position + Vector3.up * 2.5f; // 턱 들기
            else if (touchingCat) r.lookTarget = pointerWorld;
            else if (State == CatState.WaitAtBowl && cam) r.lookTarget = (Mathf.Repeat(StateTime, 5f) < 2.5f && bowl) ? bowl.transform.position : cam.position;
            else if (State == CatState.GoToBowl && bowl) r.lookTarget = bowl.transform.position;
            else if (State == CatState.Idle || State == CatState.Invite || State == CatState.Petted) r.lookTarget = cam ? cam.position : (Vector3?)null;
            else r.lookTarget = null;
        }

        void UpdateBubble()
        {
            if (State == CatState.Nip) Bubble.Show(Icon.Exclaim);
            else if (Needs.IsHungry && State != CatState.Eat && State != CatState.Sleep && !(bowl && bowl.HasFood && State == CatState.GoToBowl)) Bubble.Show(Icon.Fish);
            else if (State == CatState.Sleep && StateTime > 1.5f) Bubble.Show(Icon.Sleep);
            else if (State == CatState.Invite && StateTime > 0.5f) Bubble.Show(Icon.Hand);
            else Bubble.Hide();
        }

        void ApplyFace(float dt)
        {
            if (Speed > 0.1f) return;
            Vector3? target = faceTarget;
            float turnSpeed = 200f;
            if (!target.HasValue && cam && (State == CatState.Idle || State == CatState.Petted))
            {
                // 가만히 있거나 쓰다듬어질 때는 카메라 쪽으로 3/4 정도 돌아 얼굴이 보이게 한다
                Vector3 toCam = cam.position - transform.position; toCam.y = 0f;
                float side = Vector3.SignedAngle(toCam, transform.forward, Vector3.up) >= 0f ? 1f : -1f;
                target = transform.position + Quaternion.Euler(0f, 32f * side, 0f) * toCam;
                turnSpeed = State == CatState.Petted ? 55f : 120f;
            }
            if (!target.HasValue) return;
            Vector3 to = target.Value - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            transform.Rotate(0f, Mathf.Clamp(angle, -turnSpeed * dt, turnSpeed * dt), 0f);
        }

        /// <summary>방석 위에서는 방석 높이만큼 올라앉는다.</summary>
        void ApplyHeight(float dt)
        {
            bool onCushion = false;
            if (cushion)
            {
                Vector3 d = transform.position - cushion.transform.position; d.y = 0f;
                onCushion = d.magnitude < 0.3f;
            }
            heightY = Mathf.Lerp(heightY, onCushion ? 0.085f : 0f, 1f - Mathf.Exp(-8f * dt));
            var pos = transform.position;
            pos.y = heightY;
            transform.position = pos;
        }

        float FacingAngle(Vector3 target)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            return to.sqrMagnitude < 0.0001f ? 0f : Mathf.Abs(Vector3.SignedAngle(transform.forward, to, Vector3.up));
        }

        float RollTowardCamera()
        {
            if (!cam) return 1f;
            Vector3 toCam = (cam.position - transform.position).normalized;
            Vector3 fwd = transform.forward;
            Vector3 down = -transform.up;
            Vector3 a = Quaternion.AngleAxis(105f, fwd) * down;
            Vector3 b = Quaternion.AngleAxis(-105f, fwd) * down;
            // Pose.localRotation 의 z 회전 부호와 같은 방향
            return Vector3.Dot(a, toCam) > Vector3.Dot(b, toCam) ? 1f : -1f;
        }

        Vector3 RandomDeckPoint()
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * 1.6f;
                var p = new Vector3(r.x, 0f, r.y * 0.9f - 0.1f);
                if ((p - transform.position).magnitude > 0.6f) return p;
            }
            return Vector3.zero;
        }

        Vector3 InviteSpot()
        {
            if (!cam) return Vector3.zero;
            Vector3 toCam = cam.position; toCam.y = 0f;
            return ClampToIsland(toCam.normalized * 1.0f);
        }

        public static Vector3 ClampToIsland(Vector3 p)
        {
            p.y = 0f;
            Vector3 flat = Vector3.ClampMagnitude(p, GameConfig.IslandWalkRadius);
            return flat;
        }

        // ---------------- 테스트와 스크린샷용

        public void ForceState(CatState s) => Enter(s);
    }
}
