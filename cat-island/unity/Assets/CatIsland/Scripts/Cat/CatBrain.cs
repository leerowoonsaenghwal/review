using System;
using UnityEngine;

namespace CatIsland
{
    public enum CatState
    {
        Idle, Wander, Called, Zoomies, Invite, SitIdle, Groom, Stretch,
        GoToBowl, WaitAtBowl, Eat,
        GoToCushion, LieDown, Sleep,
        GoToTower, JumpUp, OnTower, JumpDown,
        Petted, BellyUp, Nip
    }

    /// <summary>
    /// 고양이의 행동. 상태, 성격(좋아하는 곳), 쓰다듬기 입력에 따라 스스로 움직인다.
    /// 세 가지 약속: 벌주지 않는다 / 고양이가 주인공 / 매일 조금씩 다르다.
    /// 동작은 파이프라인이 만든 클립을 쓴다: 걷기·종종걸음·달리기, 앉기, 식빵, 잠, 그루밍, 기지개, 마시기, 발라당, 앞발 장난, 점프.
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
        public bool OnTower { get; private set; }

        public FoodBowl bowl;
        public Cushion cushion;
        public CatTower tower;
        public CatAudio audioOut;
        public Transform cam;

        public event Action<int> LevelUp;
        public event Action FirstPetToday;
        public event Action Nipped;
        public event Action BelliedUp;

        PetZone petZone;
        float strokeSpeed;
        Vector3 pointerWorld;
        bool touchingCat;
        float lastPetTime = -99f, lastUserActivity, heartAccum, inviteCooldown = 4f, idleDecide = 1.5f, crunchTimer, stateTimer;
        Vector3 moveTarget;
        Vector3? faceDir;
        bool firstPetChecked, actionStarted;
        int zoomiesLeft;
        float savedAffection, heightY;
        Vector3 jumpStart, jumpDir;
        Quaternion flopFacing;
        float jumpFrom, jumpTo;

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
            if (State == CatState.Sleep) return; // 자는 고양이는 깨우지 않는다
            audioOut?.Meow();
            Rig.mouthOpen = 1f;
            Invoke(nameof(CloseMouth), 0.4f);
            Haptics.Impact(ImpactStyle.Soft, 0.6f);
            if (State == CatState.Wander || State == CatState.Invite) Enter(CatState.Idle);
            if (cam) faceDir = Flat(cam.position - transform.position);
        }

        void CloseMouth() { Rig.mouthOpen = 0f; }

        public void OnTapGround(Vector3 world)
        {
            NoteUserActivity();
            if (OnTower && IsFree()) { BeginJumpDown(ClampToIsland(world)); return; }
            if (!IsFree() || OnTower) return;
            moveTarget = ClampToIsland(world);
            audioOut?.Chirp();
            Enter(CatState.Called);
        }

        public void OnBowlFilled()
        {
            NoteUserActivity();
            if (State == CatState.Sleep || OnTower) return;
            if (Needs.Hunger < 0.9f && State != CatState.Eat && State != CatState.BellyUp && State != CatState.Nip && !IsJumping())
                Enter(CatState.GoToBowl);
        }

        public void OnCushionTapped()
        {
            NoteUserActivity();
            if (State == CatState.Sleep || OnTower) return;
            if (IsFree() && Needs.Energy < 0.7f) Enter(CatState.GoToCushion);
        }

        public void OnTowerTapped()
        {
            NoteUserActivity();
            if (tower && IsFree() && !OnTower) Enter(CatState.GoToTower);
        }

        bool IsFree() =>
            State == CatState.Idle || State == CatState.Wander || State == CatState.Invite || State == CatState.Called ||
            State == CatState.WaitAtBowl || State == CatState.SitIdle || State == CatState.OnTower || State == CatState.Groom;

        bool IsJumping() => State == CatState.JumpUp || State == CatState.JumpDown;

        // ------------------------------------------------------------ 갱신

        void Update() { Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            StateTime += dt;
            inviteCooldown -= dt;

            Needs.Tick(dt, State == CatState.Eat && Rig.ActionClip == "Drink", State == CatState.Sleep);

            var f = Pet.Update(touchingCat ? petZone : PetZone.None, strokeSpeed, dt);
            if (f.Enjoying) lastPetTime = Time.time;
            HandlePetFrame(f);

            switch (State)
            {
                case CatState.Idle: TickIdle(dt); break;
                case CatState.Wander: TickMoveTo(dt, GameConfig.WalkSpeed, CatState.Idle); break;
                case CatState.Called: TickMoveTo(dt, GameConfig.TrotSpeed, CatState.Idle); break;
                case CatState.Zoomies: TickZoomies(dt); break;
                case CatState.Invite: TickInvite(dt); break;
                case CatState.SitIdle: TickSitIdle(dt); break;
                case CatState.Groom: TickGroom(dt); break;
                case CatState.Stretch: TickStretch(dt); break;
                case CatState.GoToBowl: TickGoToBowl(dt); break;
                case CatState.WaitAtBowl: TickWaitAtBowl(dt); break;
                case CatState.Eat: TickEat(dt); break;
                case CatState.GoToCushion: TickGoToCushion(dt); break;
                case CatState.LieDown: TickLieDown(dt); break;
                case CatState.Sleep: TickSleep(dt); break;
                case CatState.GoToTower: TickGoToTower(dt); break;
                case CatState.JumpUp:
                case CatState.JumpDown: TickJump(dt); break;
                case CatState.OnTower: TickOnTower(dt); break;
                case CatState.Petted: TickPetted(dt); break;
                case CatState.BellyUp: TickBellyUp(dt); break;
                case CatState.Nip: TickNip(dt); break;
            }

            if (!IsJumping()) { ApplyFace(dt); ApplyHeight(dt); }
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
                        FxPool.Instance?.Burst(Icon.Heart, Rig.BubbleAnchor.position, 6, 0.3f, 0.24f);
                        audioOut?.Chirp();
                        FirstPetToday?.Invoke();
                    }
                }
                AddAffection(f.AffectionGained);
                heartAccum += f.AffectionGained;
                if (heartAccum >= 2.2f)
                {
                    heartAccum = 0f;
                    FxPool.Instance?.Burst(Icon.Heart, Rig.BubbleAnchor.position - Vector3.up * 0.15f, 1, 0.15f, 0.22f);
                    Haptics.Impact(ImpactStyle.Soft, 0.35f);
                }
            }

            bool canReact = State != CatState.Sleep && State != CatState.Eat && State != CatState.LieDown && !IsJumping() && State != CatState.Stretch;
            if (f.Enjoying && canReact && State != CatState.Petted && State != CatState.BellyUp && State != CatState.Nip)
                Enter(CatState.Petted);
            if (f.Nipped && State != CatState.Sleep && !IsJumping())
            {
                Enter(CatState.Nip);
                Nipped?.Invoke();
            }
            if (f.BellyUp && canReact && State != CatState.Nip && !OnTower)
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
                FxPool.Instance?.Burst(Icon.Sparkle, Rig.BubbleAnchor.position, 5, 0.35f, 0.2f);
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
            if (Rig.ActionClip == "Drink" || Rig.ActionClip == "GroomFace") Rig.StopAction();
            State = s;
            StateTime = 0f;
            faceDir = null;
            actionStarted = false;
            switch (s)
            {
                case CatState.Idle: idleDecide = UnityEngine.Random.Range(1.5f, 3.5f); Rig.Request(Posture.Stand); break;
                case CatState.Wander: moveTarget = RandomWalkPoint(1.2f); break;
                case CatState.Invite: moveTarget = InviteSpot(); audioOut?.Chirp(); break;
                case CatState.SitIdle: stateTimer = UnityEngine.Random.Range(4f, 8f); Rig.Request(Posture.Sit); break;
                case CatState.Zoomies: zoomiesLeft = UnityEngine.Random.Range(3, 5); moveTarget = RandomWalkPoint(2.5f); audioOut?.Chirp(); break;
                case CatState.Nip:
                    audioOut?.Nip();
                    Haptics.Impact(ImpactStyle.Medium, 0.7f);
                    Rig.mouthOpen = 1f;
                    Rig.Request(Posture.Stand);
                    break;
                case CatState.BellyUp:
                    Rig.Request(Posture.Flop);
                    flopFacing = FlopFacing();
                    audioOut?.Chirp();
                    FxPool.Instance?.Burst(Icon.Heart, Rig.BubbleAnchor.position, 4, 0.3f, 0.24f);
                    Haptics.Impact(ImpactStyle.Light, 0.6f);
                    break;
                case CatState.Petted:
                    if (Rig.Current == Posture.Stand) Rig.Request(Posture.Sit);
                    break;
            }
        }

        // ---------------- 상태별

        void TickIdle(float dt)
        {
            Speed = Mathf.MoveTowards(Speed, 0f, dt * 3f);
            if (OnTower) { Enter(CatState.OnTower); return; }
            if (!Rig.CanMove) return;
            idleDecide -= dt;
            if (idleDecide > 0f) return;

            if (Needs.IsHungry) { Enter(bowl && bowl.HasFood ? CatState.GoToBowl : CatState.WaitAtBowl); return; }
            if (Needs.IsSleepy && cushion) { Enter(CatState.GoToCushion); return; }
            if (Time.time - lastUserActivity > GameConfig.IdleInviteDelay && inviteCooldown <= 0f && cam)
            {
                inviteCooldown = 20f;
                Enter(CatState.Invite);
                return;
            }
            float r = UnityEngine.Random.value;
            if (r < 0.12f && tower) Enter(CatState.GoToTower);
            else if (r < 0.2f && Needs.Energy > 0.6f) Enter(CatState.Zoomies);
            else if (r < 0.35f) Enter(CatState.Groom);
            else if (r < 0.5f) Enter(CatState.SitIdle);
            else Enter(CatState.Wander);
        }

        bool MoveTowards(Vector3 target, float maxSpeed, float dt, float arriveDist = 0.08f)
        {
            if (!Rig.CanMove)
            {
                Rig.Request(Posture.Stand);
                Speed = 0f;
                return false;
            }
            Vector3 pos = transform.position;
            Vector3 to = Flat(target - pos);
            float dist = to.magnitude;
            if (dist <= arriveDist) { Speed = Mathf.MoveTowards(Speed, 0f, dt * 4f); return Speed < 0.05f; }

            Vector3 dir = to / dist;
            dir = AvoidProps(pos, dir);
            float angle = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
            float turn = Mathf.Clamp(angle, -GameConfig.TurnSpeedDeg * dt, GameConfig.TurnSpeedDeg * dt);
            transform.Rotate(0f, turn, 0f);
            // 크게 꺾을 때만 멈춰서 돌고, 웬만한 방향 바꾸기는 걸으면서 돈다 (제자리에서 미끄러지듯 도는 것 방지)
            float a = Mathf.Abs(angle);
            float align = a > 120f ? 0f : Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(1f - a / 90f));
            float targetSpeed = maxSpeed * align * Mathf.Clamp01(dist / (0.25f + maxSpeed * 0.35f) + 0.15f);
            Speed = Mathf.MoveTowards(Speed, targetSpeed, dt * (maxSpeed > 1.5f ? 4f : 2.5f));
            Vector3 next = pos + transform.forward * Speed * dt;
            next = ClampToIsland(next);
            next.y = transform.position.y;
            transform.position = next;
            return false;
        }

        Vector3 AvoidProps(Vector3 pos, Vector3 dir)
        {
            void Push(Vector3 c, float r)
            {
                Vector3 away = Flat(pos - c);
                float d = away.magnitude;
                if (d < r && d > 0.001f) dir = (dir + away / d * (r - d) * 3f).normalized;
            }
            if (bowl && State != CatState.GoToBowl && State != CatState.WaitAtBowl) Push(bowl.transform.position, 0.6f);
            if (tower && State != CatState.GoToTower) Push(tower.transform.position, 0.9f);
            return dir;
        }

        void TickMoveTo(float dt, float speed, CatState then)
        {
            if (MoveTowards(moveTarget, speed, dt) || StateTime > 12f) Enter(then);
        }

        void TickZoomies(float dt)
        {
            float speed = Rig.HasClip("Gallop") && Rig.Info.clips != null && Array.IndexOf(Rig.Info.clips, "Gallop") >= 0 ? GameConfig.RunSpeed : GameConfig.TrotSpeed;
            if (MoveTowards(moveTarget, speed, dt, 0.25f) || StateTime > 6f)
            {
                zoomiesLeft--;
                StateTime = 0f;
                if (zoomiesLeft <= 0) { Enter(CatState.SitIdle); return; }
                moveTarget = RandomWalkPoint(2.5f);
            }
        }

        void TickInvite(float dt)
        {
            if (Rig.Current == Posture.Sit || MoveTowards(moveTarget, GameConfig.WalkSpeed, dt))
            {
                if (cam) faceDir = Flat(cam.position - transform.position);
                if (Rig.Current == Posture.Stand && FacingAngle(faceDir) < 20f) Rig.Request(Posture.Sit);
                if (StateTime > 14f) Enter(CatState.Idle);
            }
            else if (StateTime > 10f) Enter(CatState.Idle);
        }

        void TickSitIdle(float dt)
        {
            Speed = 0f;
            stateTimer -= dt;
            if (stateTimer <= 0f) Enter(CatState.Idle);
        }

        void TickGroom(float dt)
        {
            Speed = 0f;
            if (!actionStarted)
            {
                Rig.Request(Posture.Sit);
                if (Rig.Current == Posture.Sit && !Rig.Busy)
                {
                    actionStarted = true;
                    if (Rig.HasClip("GroomFace")) Rig.PlayAction("GroomFace", true);
                    stateTimer = Rig.ClipLength("GroomFace");
                }
                else if (StateTime > 4f) Enter(CatState.Idle);
                return;
            }
            stateTimer -= dt;
            if (stateTimer <= 0f) { Rig.StopAction(); Enter(CatState.SitIdle); }
        }

        void TickStretch(float dt)
        {
            Speed = 0f;
            if (!actionStarted)
            {
                if (Rig.CanMove) { actionStarted = true; Rig.PlayAction("Stretch"); }
                else if (StateTime > 4f) Enter(CatState.Idle);
                return;
            }
            if (Rig.ActionClip == null) Enter(CatState.Idle);
        }

        // ---- 밥

        Vector3 EatSpot(out Vector3 face)
        {
            // 그릇 중심이 고양이 기준 (bowl.x, bowl.z) 에 오도록 선다 (clips.json Drink.drink.bowl)
            Vector3 toCenter = Flat(Vector3.zero - bowl.transform.position);
            face = toCenter.sqrMagnitude > 0.01f ? -toCenter.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, face);
            return bowl.transform.position - face * Rig.Info.bowlZ - right * Rig.Info.bowlX;
        }

        void TickGoToBowl(float dt)
        {
            if (!bowl) { Enter(CatState.Idle); return; }
            var spot = EatSpot(out var face);
            if (MoveTowards(spot, GameConfig.TrotSpeed, dt, 0.05f))
            {
                faceDir = face;
                transform.position = Vector3.Lerp(transform.position, new Vector3(spot.x, transform.position.y, spot.z), 1f - Mathf.Exp(-8f * dt));
                if (FacingAngle(face) < 4f) Enter(bowl.HasFood ? CatState.Eat : CatState.WaitAtBowl);
            }
            else if (StateTime > 14f) Enter(CatState.Idle);
        }

        void TickWaitAtBowl(float dt)
        {
            // 그릇 앞에 앉아 기다린다. 울지 않는다.
            if (!bowl) { Enter(CatState.Idle); return; }
            var spot = EatSpot(out var face);
            if (Rig.Current == Posture.Sit || MoveTowards(spot, GameConfig.WalkSpeed, dt, 0.08f))
            {
                faceDir = face;
                if (Rig.Current == Posture.Stand && FacingAngle(face) < 8f) Rig.Request(Posture.Sit);
                if (bowl.HasFood) Enter(CatState.GoToBowl);
            }
            if (!Needs.IsHungry && !bowl.HasFood && StateTime > 3f) Enter(CatState.Idle);
        }

        void TickEat(float dt)
        {
            Speed = 0f;
            if (!actionStarted)
            {
                if (Rig.CanMove) { actionStarted = true; Rig.PlayAction("Drink", true); }
                else { Rig.Request(Posture.Stand); return; }
            }
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
                FxPool.Instance?.Burst(Icon.Heart, Rig.BubbleAnchor.position, 2, 0.15f);
                Rig.StopAction();
                Rig.PlayAction("LickLips");
                // 배부르면 졸리다 (밥 → 방석에서 잠)
                Needs.SetForTest(Needs.Hunger, Mathf.Min(Needs.Energy, GameConfig.SleepyThreshold - 0.05f));
                Enter(cushion ? CatState.GoToCushion : CatState.Idle);
            }
        }

        // ---- 방석과 잠

        Vector3 CushionSpot(out Vector3 face)
        {
            face = cam ? Flat(cam.position - cushion.transform.position).normalized : Vector3.back;
            // 방석 중심이 고양이 몸통 가운데 아래에 오도록 (clips.json itemSpots.cushion)
            return cushion.transform.position - face * Rig.Info.cushionZ;
        }

        void TickGoToCushion(float dt)
        {
            if (!cushion) { Enter(CatState.Idle); return; }
            if (!actionStarted && Rig.ActionClip == "LickLips") return; // 입술 핥기를 마치고 출발
            actionStarted = true;
            var spot = CushionSpot(out var face);
            if (MoveTowards(spot, GameConfig.WalkSpeed, dt, 0.05f))
            {
                faceDir = face;
                transform.position = Vector3.Lerp(transform.position, new Vector3(spot.x, transform.position.y, spot.z), 1f - Mathf.Exp(-8f * dt));
                if (FacingAngle(face) < 6f) Enter(CatState.LieDown);
            }
            else if (StateTime > 18f) Enter(CatState.Idle);
        }

        void TickLieDown(float dt)
        {
            Speed = 0f;
            Rig.Request(StateTime > 2.5f ? Posture.Sleep : Posture.Loaf);
            if (Rig.Current == Posture.Sleep) Enter(CatState.Sleep);
        }

        void TickSleep(float dt)
        {
            Speed = 0f;
            Rig.Request(Posture.Sleep);
            if (Needs.Energy >= GameConfig.WakeEnergy && !touchingCat)
            {
                audioOut?.Chirp();
                Rig.Request(Posture.Stand);
                Enter(CatState.Stretch);
            }
        }

        // ---- 캣타워

        Vector3 TowerStart()
        {
            float d = Rig.Info.jump != null && Rig.Info.jump.forward.Length > 0 ? Rig.Info.jump.forward[Rig.Info.jump.forward.Length - 1] : 0.8f;
            return tower.transform.position - tower.transform.forward * d;
        }

        void TickGoToTower(float dt)
        {
            if (!tower || Rig.Info.jump == null) { Enter(CatState.Idle); return; }
            Vector3 start = TowerStart();
            if (MoveTowards(start, GameConfig.WalkSpeed, dt, 0.05f))
            {
                faceDir = tower.transform.forward;
                transform.position = Vector3.Lerp(transform.position, new Vector3(start.x, 0f, start.z), 1f - Mathf.Exp(-8f * dt));
                if (FacingAngle(faceDir) < 3f)
                {
                    transform.rotation = Quaternion.LookRotation(tower.transform.forward);
                    BeginJump(transform.position, tower.transform.forward, 0f, tower.DeckHeight, CatState.JumpUp);
                }
            }
            else if (StateTime > 16f) Enter(CatState.Idle);
        }

        void BeginJumpDown(Vector3 toward)
        {
            Vector3 dir = Flat(toward - transform.position);
            if (dir.sqrMagnitude < 0.01f) dir = -tower.transform.forward;
            dir.Normalize();
            transform.rotation = Quaternion.LookRotation(dir);
            BeginJump(transform.position, dir, tower ? tower.DeckHeight : 0f, 0f, CatState.JumpDown);
        }

        void BeginJump(Vector3 start, Vector3 dir, float fromY, float toY, CatState s)
        {
            Rig.Request(Posture.Stand);
            Enter(s);
            jumpStart = new Vector3(start.x, 0f, start.z);
            jumpDir = dir;
            jumpFrom = fromY;
            jumpTo = toY;
            Rig.PlayAction("JumpUp", false, 0.2f);
        }

        /// <summary>
        /// 점프 이동: 클립의 루트 곡선(앞으로 간 거리, 높이)을 그대로 따른다.
        /// 내려갈 때는 같은 곡선에서 "솟는 부분(포물선)"만 쓰고 기준 높이를 거꾸로 바꾼다.
        /// </summary>
        void TickJump(float dt)
        {
            Speed = 0f;
            var c = Rig.Info.jump;
            float t = Rig.ActionTime;
            float fi = Mathf.Clamp(t * c.fps, 0f, c.forward.Length - 1);
            int i0 = Mathf.FloorToInt(fi), i1 = Mathf.Min(i0 + 1, c.forward.Length - 1);
            float k = fi - i0;
            float fwd = Mathf.Lerp(c.forward[i0], c.forward[i1], k);
            float up = Mathf.Lerp(c.up[i0], c.up[i1], k);
            float totalFwd = c.forward[c.forward.Length - 1];
            float clipRise = c.up[c.up.Length - 1];
            float s = totalFwd > 0.001f ? Mathf.Clamp01(fwd / totalFwd) : 0f;
            float bump = up - clipRise * s;                 // 출발과 착지 높이를 뺀 포물선
            float y = Mathf.Lerp(jumpFrom, jumpTo, s) + bump;
            transform.position = jumpStart + jumpDir * fwd + Vector3.up * y;

            if (Rig.ActionClip == null)
            {
                transform.position = jumpStart + jumpDir * totalFwd + Vector3.up * jumpTo;
                heightY = jumpTo;
                bool up2 = State == CatState.JumpUp;
                OnTower = up2;
                Enter(up2 ? CatState.OnTower : CatState.Idle);
            }
        }

        void TickOnTower(float dt)
        {
            Speed = 0f;
            if (StateTime < 0.1f) stateTimer = UnityEngine.Random.Range(8f, 14f);
            if (StateTime > 1.2f && Rig.Current == Posture.Stand && !Rig.Busy) Rig.Request(UnityEngine.Random.value < 0.5f ? Posture.Sit : Posture.Loaf);
            if (cam && Rig.Current != Posture.Loaf) faceDir = Flat(cam.position - transform.position);
            stateTimer -= dt;
            if (stateTimer <= 0f || Needs.IsHungry || Needs.IsSleepy)
            {
                if (Rig.Current != Posture.Stand) { Rig.Request(Posture.Stand); return; }
                if (!Rig.Busy) BeginJumpDown(Vector3.zero);
            }
        }

        // ---- 쓰다듬기 반응

        void TickPetted(float dt)
        {
            Speed = Mathf.MoveTowards(Speed, 0f, dt * 6f);
            if (Time.time - lastPetTime > 1.6f && !touchingCat) Enter(OnTower ? CatState.OnTower : CatState.SitIdle);
        }

        /// <summary>발라당 누웠을 때 배가 카메라를 보도록 하는 몸 방향 (배 방향은 가져올 때 클립에서 잼).</summary>
        Quaternion FlopFacing()
        {
            Vector3 belly = Flat(Rig.Info.flopBelly);
            if (!cam || belly.sqrMagnitude < 0.01f) return transform.rotation;
            Vector3 toCam = Flat(cam.position - transform.position);
            return Quaternion.FromToRotation(belly.normalized, toCam.normalized);
        }

        void TickBellyUp(float dt)
        {
            Speed = 0f;
            // 구르는 동안 배가 카메라 쪽으로 오게 몸을 튼다
            if (StateTime < 1.4f) transform.rotation = Quaternion.RotateTowards(transform.rotation, flopFacing, 170f * dt);
            // 계속 쓰다듬는 동안은 누워 있다. 믿음 시간이 끝난 뒤에도 배를 계속 만지면 살짝 깨문다.
            bool petRecently = Time.time - lastPetTime < 2.5f || touchingCat;
            if (!petRecently && StateTime > 2.5f) Enter(CatState.Idle);
        }

        void TickNip(float dt)
        {
            Speed = 0f;
            if (StateTime > 0.3f) Rig.mouthOpen = 0f;
            if (!actionStarted)
            {
                if (Rig.CanMove) { actionStarted = true; Rig.PlayAction("PawBat"); }
                else if (StateTime < 2.5f) return;
                else actionStarted = true;
            }
            if (Rig.ActionClip == null || StateTime > 3f)
            {
                Rig.StopAction();
                // 살짝 물러난다. 벌이 아니라 "거기는 싫어"라는 표현
                if (OnTower) { Enter(CatState.OnTower); return; }
                Vector3 away = Flat(transform.position - pointerWorld);
                if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
                moveTarget = ClampToIsland(transform.position + away.normalized * 0.6f);
                Enter(CatState.Called);
            }
        }

        // ---------------- 표정과 자세

        void UpdateExpression(float dt)
        {
            float p = Pet.Pleasure;
            var r = Rig;
            r.moveSpeed = Speed;
            bool sleep = State == CatState.Sleep || r.Current == Posture.Sleep;
            float happyClose = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, p));
            r.eyeOpen = sleep ? 0f : 1f - happyClose;
            r.tailWag = State == CatState.Nip ? 1f : p * 0.5f;

            bool petting = touchingCat && (State == CatState.Petted || State == CatState.BellyUp);
            if (petting && (petZone == PetZone.Forehead || petZone == PetZone.Cheek))
            {
                Vector3 local = transform.InverseTransformPoint(pointerWorld);
                r.headTilt = Mathf.Clamp(-local.x * 60f, -18f, 18f);
            }
            else r.headTilt = State == CatState.WaitAtBowl ? Mathf.Sin(Time.time * 0.7f) * 8f : 0f;

            if (State == CatState.Eat || sleep) r.lookTarget = null;
            else if (petting && petZone == PetZone.Chin && cam) r.lookTarget = cam.position + Vector3.up * 6f;
            else if (touchingCat) r.lookTarget = pointerWorld;
            else if (State == CatState.WaitAtBowl && cam && bowl) r.lookTarget = Mathf.Repeat(StateTime, 5f) < 2.5f ? bowl.transform.position : cam.position;
            else if (State == CatState.GoToBowl && bowl) r.lookTarget = bowl.transform.position;
            else if (State == CatState.Idle || State == CatState.Invite || State == CatState.Petted || State == CatState.SitIdle || State == CatState.OnTower) r.lookTarget = cam ? cam.position : (Vector3?)null;
            else r.lookTarget = null;
        }

        void UpdateBubble()
        {
            if (State == CatState.Nip) Bubble.Show(Icon.Exclaim);
            else if (Needs.IsHungry && State != CatState.Eat && State != CatState.Sleep && !IsJumping() && !(bowl && bowl.HasFood && State == CatState.GoToBowl)) Bubble.Show(Icon.Fish);
            else if (State == CatState.Sleep && StateTime > 1.5f) Bubble.Show(Icon.Sleep);
            else if (State == CatState.Invite && Rig.Current == Posture.Sit) Bubble.Show(Icon.Hand);
            else Bubble.Hide();
        }

        void ApplyFace(float dt)
        {
            if (Speed > 0.1f) return;
            Vector3? dir = faceDir;
            float turnSpeed = 140f;
            bool canTurn = Rig.Current == Posture.Stand && !Rig.Busy;
            if (!dir.HasValue && cam && canTurn && (State == CatState.Idle || State == CatState.Petted))
            {
                // 가만히 있을 때는 카메라 쪽으로 3/4 정도 돌아 얼굴이 보이게 한다
                Vector3 toCam = Flat(cam.position - transform.position);
                float side = Vector3.SignedAngle(toCam, transform.forward, Vector3.up) >= 0f ? 1f : -1f;
                dir = Quaternion.Euler(0f, 32f * side, 0f) * toCam;
                turnSpeed = 90f;
            }
            if (!dir.HasValue || !canTurn || dir.Value.sqrMagnitude < 0.0001f) return;
            float angle = Vector3.SignedAngle(transform.forward, dir.Value, Vector3.up);
            transform.Rotate(0f, Mathf.Clamp(angle, -turnSpeed * dt, turnSpeed * dt), 0f);
        }

        /// <summary>바닥 높이: 캣타워 판 위, 방석 위(방석 중심 가까이 갈수록 올라감), 그 밖은 땅.</summary>
        void ApplyHeight(float dt)
        {
            float target = 0f;
            if (OnTower && tower) target = tower.DeckHeight;
            else if (cushion)
            {
                float d = Flat(transform.position - cushion.transform.position).magnitude;
                target = cushion.TopHeight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cushion.Radius * 0.55f, cushion.Radius * 1.05f, d)));
            }
            heightY = Mathf.Lerp(heightY, target, 1f - Mathf.Exp(-8f * dt));
            var pos = transform.position;
            pos.y = heightY;
            transform.position = pos;
        }

        float FacingAngle(Vector3? dir)
        {
            if (!dir.HasValue || dir.Value.sqrMagnitude < 0.0001f) return 0f;
            return Mathf.Abs(Vector3.SignedAngle(transform.forward, dir.Value, Vector3.up));
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        Vector3 RandomWalkPoint(float minDist)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * GameConfig.WanderRadius;
                var p = new Vector3(r.x, 0f, r.y * 0.8f - 0.3f);
                if (Flat(p - transform.position).magnitude < minDist) continue;
                if (bowl && Flat(p - bowl.transform.position).magnitude < 0.8f) continue;
                if (tower && Flat(p - tower.transform.position).magnitude < 1.1f) continue;
                return p;
            }
            return Vector3.zero;
        }

        Vector3 InviteSpot()
        {
            if (!cam) return Vector3.zero;
            Vector3 toCam = Flat(cam.position);
            return ClampToIsland(toCam.normalized * 1.6f);
        }

        public static Vector3 ClampToIsland(Vector3 p)
        {
            p.y = 0f;
            return Vector3.ClampMagnitude(p, GameConfig.IslandWalkRadius);
        }

        // ---------------- 테스트와 스크린샷용

        public void ForceState(CatState s) => Enter(s);
        public void ForceOnTower(bool on) { OnTower = on; heightY = on && tower ? tower.DeckHeight : 0f; }
    }
}
