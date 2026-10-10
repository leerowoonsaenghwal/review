using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CatIsland
{
    public enum CatState
    {
        Idle, Wander, Called, Zoomies, Invite, SitIdle, Groom, Stretch,
        GoToBowl, WaitAtBowl, Eat,
        GoToCushion, LieDown, Sleep,
        GoToTower, JumpUp, OnTower, JumpDown,
        Petted, BellyUp, Nip,
        GoToItem, UseItem,
        Treat, LeaveForWalk, ReturnFromWalk, GoToSleepNear,
        Chase, Flee, Visit
    }

    /// <summary>섬에 놓인 용품 표시 (고양이가 골라 쓴다).</summary>
    public class ItemTag : MonoBehaviour
    {
        public string id; public CatIsland.Game.Placement placement;
        /// <summary>섬에 있는 용품 (켜진 것만): 매번 찾지 않게 스스로 등록한다 (OccluderFade).</summary>
        public static readonly System.Collections.Generic.List<ItemTag> All = new System.Collections.Generic.List<ItemTag>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
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

        /// <summary>기분 좋게 한 번 쓰다듬어졌다 (하트가 나올 때마다): 게임 규칙 쪽 호감도·할 일에 센다.</summary>
        public Action<float> OnPetted;
        /// <summary>게임 저장의 고양이 (없으면 시제품 혼자 사는 고양이). 배고픔은 저장 값을 따른다.</summary>
        public CatIsland.Game.CatData Data;
        Obstacle selfObstacle;
        // 용품은 한 번에 한 마리만 (방석·캣타워·그릇에 두 마리가 겹치지 않게)
        static readonly Dictionary<Transform, CatBrain> claims = new Dictionary<Transform, CatBrain>();
        public static readonly List<CatBrain> All = new List<CatBrain>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        float blockedFor;
        /// <summary>앞을 막고 있는 다른 고양이 (몸 반 마리 거리 안, 가는 쪽).</summary>
        CatBrain CatAhead(Vector3 dir, float range)
        {
            foreach (var o in All)
            {
                if (!o || o == this || !o.isActiveAndEnabled || o.OnTower != OnTower) continue;
                var d = Flat(o.transform.position - transform.position); float dist = d.magnitude;
                if (dist < range && (dist < .3f || Vector3.Dot(d / dist, dir) > .35f)) return o;
            }
            return null;
        }
        bool OtherCatNear(Vector3 p, float r)
        {
            foreach (var o in All) if (o && o != this && o.isActiveAndEnabled && Flat(o.transform.position - p).magnitude < r) return true;
            return false;
        }
        /// <summary>방석이 비었나: 차지한 고양이가 없고, 실제로 그 위·곁에 다른 고양이가 없다 (깬 뒤 방석 위에서 기지개 켜는 고양이도 있다).</summary>
        bool CushionFree => Free(cushion) && cushion && !OtherCatNear(cushion.transform.position, cushion.Radius + .15f);
        bool Free(Component item) => item && item.gameObject.activeInHierarchy && (!claims.TryGetValue(item.transform, out var o) || !o || o == this);
        void ClaimFor(CatState s)
        {
            foreach (var k in new List<Transform>(claims.Keys)) if (claims[k] == this) claims.Remove(k);
            var t = s == CatState.GoToBowl || s == CatState.WaitAtBowl || s == CatState.Eat ? (bowl ? bowl.transform : null)
                  : s == CatState.GoToCushion || s == CatState.LieDown || s == CatState.Sleep ? (cushion ? cushion.transform : null)
                  : s == CatState.GoToTower || s == CatState.OnTower || s == CatState.JumpUp || s == CatState.JumpDown ? (curTower ? curTower.transform : tower ? tower.transform : null)
                  : s == CatState.GoToItem || s == CatState.UseItem ? (useTarget ? useTarget.transform : null) : null;
            if (t) claims[t] = this;
        }
        void OnDestroy() { All.Remove(this); foreach (var k in new List<Transform>(claims.Keys)) if (claims[k] == this) claims.Remove(k); if (selfObstacle != null) nav?.obstacles.Remove(selfObstacle); }
        public FoodBowl bowl;
        public Cushion cushion;
        public CatTower tower;
        public NavGrid nav;
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
        float savedAffection, heightY, stepPhase, turnStep, replanTimer;
        List<Vector3> path;
        Vector3 pathGoal = new Vector3(999f, 0f, 999f);
        Vector3 jumpStart, jumpDir;
        Quaternion flopFacing;
        bool leaveTower;
        Vector3 leaveToward;
        float turnedIn;
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
            if (OnTower && IsFree()) { leaveTower = true; leaveToward = ClampToIsland(world); if (State != CatState.OnTower) Enter(CatState.OnTower); return; }
            if (!IsFree() || OnTower) return;
            moveTarget = ClampToIsland(world);
            if (nav != null) moveTarget = nav.NearestFree(moveTarget);
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
            if (IsFree() && !OnTower && PickTower()) { curTower = PickTower(); Enter(CatState.GoToTower); }
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
            if (cushion) { bool on = (State == CatState.LieDown || State == CatState.Sleep) && Flat(transform.position - cushion.transform.position).magnitude < cushion.Radius; if (on) cushion.Pressed = 1f; else if (Flat(transform.position - cushion.transform.position).magnitude < cushion.Radius * 1.5f) cushion.Pressed = 0f; }
            inviteCooldown -= dt;

            Needs.Tick(dt, State == CatState.Eat && Rig.ActionClip == "Drink", State == CatState.Sleep);
            if (Data != null && State != CatState.Eat) Needs.SetForTest(Data.hunger, Needs.Energy);   // (배고픔은 저장의 실제 시간 값)
            if (nav != null)
            {
                if (selfObstacle == null) selfObstacle = nav.Add(new Obstacle { name = "cat", owner = this, radius = .1f });
                selfObstacle.center = new Vector3(transform.position.x, 0f, transform.position.z);
            }

            var f = Pet.Update(touchingCat ? petZone : PetZone.None, strokeSpeed, dt);
            if (f.Enjoying) lastPetTime = Time.time;
            HandlePetFrame(f);

            // 고양이 만들기 미리보기: 창이 열려 있는 동안 고른 고양이는 제자리에 앉아 집사를 본다 (돌아다니면 창 위 화면을 벗어난다)
            if (Data == null && cam && !IsJumping() && CatIsland.UI.GameUI.Instance && CatIsland.UI.GameUI.Instance.SheetTop < .7f)
            {
                if (State != CatState.Idle) Enter(CatState.Idle);
                Speed = 0f; idleDecide = 1f; faceDir = Flat(cam.position - transform.position);
                if (Rig.Current == Posture.Stand && FacingAngle(faceDir) < 20f) Rig.Request(Posture.Sit);
                ApplyFace(dt); ApplyHeight(dt); UpdateExpression(dt); Bubble.Hide();   // (말풍선은 위 숫자판에 가린다: 미리보기에서는 숨김)
                return;
            }

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
                case CatState.GoToItem: TickGoToItem(dt); break;
                case CatState.UseItem: TickUseItem(dt); break;
                case CatState.Treat: TickTreat(dt); break;
                case CatState.LeaveForWalk: TickLeave(dt); break;
                case CatState.ReturnFromWalk: TickReturn(dt); break;
                case CatState.GoToSleepNear: TickMoveTo(dt, GameConfig.WalkSpeed, CatState.Sleep); break;
                case CatState.Chase: TickChase(dt); break;
                case CatState.Flee: TickFlee(dt); break;
                case CatState.Visit: TickVisit(dt); break;
                case CatState.Petted: TickPetted(dt); break;
                case CatState.BellyUp: TickBellyUp(dt); break;
                case CatState.Nip: TickNip(dt); break;
            }

            Separate(dt);
            if (!IsJumping()) { ApplyFace(dt); ApplyHeight(dt); }
            UpdateFootsteps(dt);
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
                    OnPetted?.Invoke(Mathf.Clamp01(Pet.Pleasure + .3f));
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
        void OnDisable() { All.Remove(this); if (Affection != null) PlayerPrefs.SetFloat(PrefAffection, Affection.Points); }

        void Enter(CatState s)
        {
            // 방석에서 자고 일어나 내려오는 중: 방석을 '방금 쓰고 나온 물건'으로 (내려오는 동안 방석 안을 지나는 것이 맞다)
            if ((State == CatState.Sleep || State == CatState.LieDown) && s != CatState.Sleep && s != CatState.LieDown && cushion && Flat(transform.position - cushion.transform.position).magnitude < cushion.Radius + .2f)
            { LeftItem = cushion.transform; LeftAt = Time.time; leftLift = 0f; }
            if (Rig.ActionClip == "Drink" || Rig.ActionClip == "GroomFace") Rig.StopAction();
            State = s;
            ClaimFor(s);
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

            if (Needs.IsHungry && Free(bowl)) { Enter(bowl && bowl.HasFood ? CatState.GoToBowl : CatState.WaitAtBowl); return; }
            if (Needs.IsSleepy && cushion && CushionFree) { Enter(CatState.GoToCushion); return; }
            if (Time.time - lastUserActivity > GameConfig.IdleInviteDelay && inviteCooldown <= 0f && cam)
            {
                inviteCooldown = 20f;
                Enter(CatState.Invite);
                return;
            }
            // 밤에는 잔다: 빈 방석이 있으면 방석에서, 아니면 자고 있는 다른 고양이 옆에서 (고양이끼리 같이 자기)
            if (IsNight)
            {
                if (cushion && CushionFree && cushion.gameObject.activeInHierarchy) { Enter(CatState.GoToCushion); return; }
                var sleeper = All.Find(o => o && o != this && o.isActiveAndEnabled && o.State == CatState.Sleep && !o.OnTower);
                if (sleeper) { var side = sleeper.transform.right * (UnityEngine.Random.value < .5f ? .7f : -.7f); moveTarget = nav != null ? nav.NearestFree(sleeper.transform.position + side) : sleeper.transform.position + side; Enter(CatState.GoToSleepNear); return; }
                Enter(CatState.Sleep); return;
            }
            float r = UnityEngine.Random.value;
            if (r < 0.1f && TryFriend()) return;
            r = UnityEngine.Random.value;
            if (r < 0.22f) { var it = PickItemUse(); if (it != null) { BeginUse(it); return; } }
            r = UnityEngine.Random.value;
            if (r < 0.12f && PickTower()) { curTower = PickTower(); Enter(CatState.GoToTower); }
            else if (r < 0.2f && Needs.Energy > 0.6f) Enter(CatState.Zoomies);
            else if (r < 0.35f) Enter(CatState.Groom);
            else if (r < 0.5f) Enter(CatState.SitIdle);
            else Enter(CatState.Wander);
        }

        // ---------------- 고양이끼리 (기획서: 고양이끼리 쫓기·같이 있기). 벌·싸움 없음, 짧게 놀고 앉는다
        CatBrain friend;
        bool Available => isActiveAndEnabled && !OnTower && (State == CatState.Idle || State == CatState.SitIdle || State == CatState.Wander) && Rig.CanMove;

        /// <summary>근처의 한가한 고양이에게: 장난꾸러기는 쫓기 놀이, 아니면 옆에 가서 같이 앉기.</summary>
        public bool TryFriend()
        {
            var other = All.Where(o => o && o != this && o.Available && o.Data != null && Flat(o.transform.position - transform.position).magnitude < 4f)
                           .OrderBy(o => Flat(o.transform.position - transform.position).magnitude).FirstOrDefault();
            if (!other || Data == null) return false;
            friend = other;
            bool playful = Data.personality == CatIsland.Game.Personality.Playful;
            if ((Data.personality == CatIsland.Game.Personality.Aloof || Data.personality == CatIsland.Game.Personality.Shy) && UnityEngine.Random.value > .4f) { friend = null; return false; }   // (새침·수줍은 고양이는 가끔만)
            if (playful && Needs.Energy > .5f && other.Needs.Energy > .4f) { Enter(CatState.Chase); other.BeChased(this); audioOut?.Chirp(); }
            else { other.Enter(CatState.SitIdle); other.stateTimer = 14f; Enter(CatState.Visit); moveTarget = VisitSpot(other); }   // (친구는 앉아서 기다린다)
            return true;
        }

        public void BeChased(CatBrain by)
        {
            friend = by; Enter(CatState.Flee);
            var away = Flat(transform.position - by.transform.position).normalized; if (away.sqrMagnitude < .01f) away = transform.forward;
            moveTarget = nav != null ? nav.NearestFree(transform.position + Quaternion.Euler(0, UnityEngine.Random.Range(-50f, 50f), 0) * away * 2.2f) : transform.position + away * 2.2f;
            FxPool.Instance?.Burst(Icon.Exclaim, Rig.BubbleAnchor.position, 1, 0.1f, 0.3f);
        }

        Vector3 VisitSpot(CatBrain o)
        {
            var side = o.transform.right * (Vector3.Dot(transform.position - o.transform.position, o.transform.right) >= 0 ? .7f : -.7f);   // (몸이 닿지 않게: 옆에 나란히)
            return nav != null ? nav.NearestFree(o.transform.position + side) : o.transform.position + side;
        }

        void TickChase(float dt)
        {
            if (!friend || friend.State != CatState.Flee) { Enter(CatState.SitIdle); return; }
            moveTarget = friend.transform.position;
            bool caught = MoveTowards(moveTarget, GameConfig.TrotSpeed * 1.05f, dt, .7f) || Flat(friend.transform.position - transform.position).magnitude < .72f;
            if (caught || StateTime > 5f)
            {
                friend.Enter(CatState.SitIdle); friend.faceDir = Flat(transform.position - friend.transform.position).normalized;
                Enter(CatState.SitIdle); faceDir = Flat(friend.transform.position - transform.position).normalized;
                if (caught) FxPool.Instance?.Burst(Icon.Note, (Rig.BubbleAnchor.position + friend.Rig.BubbleAnchor.position) * .5f, 2, 0.2f, 0.25f);
                if (caught) OnFriendPlay?.Invoke(friend);
            }
        }

        void TickFlee(float dt)
        {
            if (MoveTowards(moveTarget, GameConfig.TrotSpeed, dt, .25f))
            {
                var away = Flat(transform.position - (friend ? friend.transform.position : transform.position - transform.forward)).normalized;
                moveTarget = nav != null ? nav.NearestFree(transform.position + Quaternion.Euler(0, UnityEngine.Random.Range(-70f, 70f), 0) * away * 1.8f) : transform.position + away * 1.8f;
            }
            if (StateTime > 7f) Enter(CatState.SitIdle);
        }

        void TickVisit(float dt)
        {
            if (!friend || !friend.isActiveAndEnabled || friend.OnTower) { Enter(CatState.Idle); return; }
            if (!actionStarted)
            {
                if (MoveTowards(moveTarget, GameConfig.WalkSpeed, dt, .12f) || StateTime > 10f)
                {
                    actionStarted = true; StateTime = 0f;
                    faceDir = Flat(friend.transform.position - transform.position).normalized;
                    Rig.Request(Posture.Sit);
                    if (friend.Available) { friend.Enter(CatState.SitIdle); friend.faceDir = -faceDir; }
                    FxPool.Instance?.Burst(Icon.Heart, (Rig.BubbleAnchor.position + friend.Rig.BubbleAnchor.position) * .5f, 2, 0.2f, 0.22f);
                }
                return;
            }
            Rig.lookTarget = friend.Rig.Head.position;
            if (StateTime > 3f && StateTime - dt <= 3f && Rig.HasClip("GroomFace")) Rig.PlayAction("GroomFace");   // (친구 옆에서 세수)
            if (StateTime > 9f) { Rig.lookTarget = null; Enter(CatState.SitIdle); }
        }

        /// <summary>새로 놓인 물건 안에 서 있으면 가장 가까운 빈자리로 폴짝 비킨다 (물건 속에 묻히지 않게).</summary>
        public void StepOutOf(Obstacle ob, NavGrid grid)
        {
            if (ob == null || grid == null || OnTower || State == CatState.UseItem || State == CatState.LeaveForWalk) return;
            if (ob.Distance(transform.position) > .05f) return;
            grid.Self = this; var to = grid.NearestFree(transform.position); grid.Self = null;
            transform.position = new Vector3(to.x, transform.position.y, to.z);
            path = null;
            FxPool.Instance?.Burst(Icon.Exclaim, Rig.BubbleAnchor.position, 1, .1f, .26f);
            if (State == CatState.GoToItem || State == CatState.Wander || State == CatState.Called) Enter(CatState.Idle);
        }

        /// <summary>지금 쓰러 가는 물건 (길찾기에서 그 물건에만 좁게 다가간다).</summary>
        Transform UsingItem() =>
            State == CatState.GoToBowl || State == CatState.WaitAtBowl || State == CatState.Eat ? (bowl ? bowl.transform : null)
            : State == CatState.GoToCushion || State == CatState.LieDown || State == CatState.Sleep ? (cushion ? cushion.transform : null)
            : State == CatState.GoToTower ? (curTower ? curTower.transform : null)
            : (State == CatState.GoToItem && !viaDoor) || State == CatState.UseItem ? (useTarget ? useTarget.transform : null) : null;   // (문 앞까지는 그 용품도 피해 간다)

        /// <summary>다음에 향할 곳: 길찾기 경로의 다음 지점 (장애물을 돌아간다).</summary>
        Vector3 Steer(Vector3 target, float dt)
        {
            if (nav == null) return target;
            var item = UsingItem();
            replanTimer -= dt;
            if (path == null || Flat(target - pathGoal).magnitude > 0.1f || replanTimer <= 0f)
            {
                pathGoal = target;
                replanTimer = 1.5f;
                nav.Self = this; path = nav.FindPath(transform.position, target, item);
                // 목적지가 막힘 거리 안이면 길이 없어 곧장(물건을 뚫고) 가게 된다: 가장 가까운 빈자리까지 길을 찾고, 마지막만 곧게
                if ((path == null || path.Count == 0) && (item == null || (cushion && item == cushion.transform)) && nav.Blocked(target, item))   // (그릇·통 같은 용품은 저마다 다가가는 방법이 있다) { var near = nav.NearestFree(target, item); path = nav.FindPath(transform.position, near, item); if (path != null && path.Count > 0) path.Add(target); }
                nav.Self = null;
            }
            if (path == null || path.Count == 0) return target;
            while (path.Count > 1 && Flat(path[0] - transform.position).magnitude < 0.3f) path.RemoveAt(0);
            // 꺾이는 곳 가까이에서는 다음 구간 쪽으로 미리 돌아 곡선으로 지나간다 (각지게 꺾지 않게)
            if (path.Count > 1)
            {
                float d0 = Flat(path[0] - transform.position).magnitude;
                float k = Mathf.Clamp01(1f - d0 / 0.7f);
                Vector3 carrot = Vector3.Lerp(path[0], path[1], k * 0.5f);
                nav.Self = this; bool clear = nav.Clear(transform.position, carrot, item); nav.Self = null;
                if (clear) return carrot;
            }
            return path[0];
        }

        /// <summary>MoveTowards 가 '도착'을 돌려줬지만 실제로는 막혀서·다른 고양이 때문에 멈춘 경우 (자리 맞추기로 끌어당기면 미끄러지듯 순간이동한다).</summary>
        bool TooFarToSettle(Vector3 spot) => Flat(spot - transform.position).magnitude > .15f;

        bool MoveTowards(Vector3 target, float maxSpeed, float dt, float arriveDist = 0.08f)
        {
            if (!Rig.CanMove)
            {
                Rig.Request(Posture.Stand);
                Speed = 0f;
                return false;
            }
            Vector3 pos = transform.position;
            float distGoal = Flat(target - pos).magnitude;
            if (distGoal <= arriveDist) { Speed = Mathf.MoveTowards(Speed, 0f, dt * 4f); return Speed < 0.05f; }
            // 다른 고양이: 목적지에 이미 있으면 그 옆에서 멈추고, 앞을 막고 있으면 잠깐 기다린다 (오래 막히면 여기서 멈춘다)
            if (distGoal < .75f && OtherCatNear(target, .55f)) { Speed = Mathf.MoveTowards(Speed, 0f, dt * 4f); return Speed < 0.05f; }
            var ahead = CatAhead(Flat(target - pos).normalized, .6f);
            if (ahead != null)
            {
                blockedFor += dt; Speed = Mathf.MoveTowards(Speed, 0f, dt * 5f);
                if (blockedFor > 1.2f) { blockedFor = 0f; return true; }
                return false;
            }
            blockedFor = 0f;

            Vector3 aim = Steer(target, dt);
            Vector3 to = Flat(aim - pos);
            if (to.sqrMagnitude < 1e-6f) to = Flat(target - pos);
            Vector3 dir = to.normalized;
            float angle = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
            // 천천히 갈수록 더 빨리 돌 수 있다 (작은 걸음으로 방향 바꾸기)
            float turnRate = GameConfig.TurnSpeedDeg * Mathf.Lerp(1.4f, 0.8f, Mathf.Clamp01(Speed / GameConfig.TrotSpeed));
            float turn = Mathf.Clamp(angle, -turnRate * dt, turnRate * dt);
            transform.Rotate(0f, turn, 0f);
            // 크게 꺾어도 멈춰서 미끄러지듯 돌지 않고, 작은 걸음으로 돌면서 간다
            float a = Mathf.Abs(angle);
            float align = a > 100f ? 0.18f : Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(1f - a / 90f));
            float targetSpeed = Mathf.Max(0.12f, maxSpeed * align * Mathf.Clamp01(distGoal / (0.25f + maxSpeed * 0.35f) + 0.15f));
            Speed = Mathf.MoveTowards(Speed, targetSpeed, dt * (maxSpeed > 1.5f ? 4f : 2.5f));
            Vector3 next = pos + transform.forward * Speed * dt;
            next = KeepApart(next);
            next = SlideOffItems(pos, next);   // (비켜 선 걸음도 용품 안으로는 들어가지 않게: 비키기 다음에)
            next = ClampToIsland(next);
            next.y = transform.position.y;
            transform.position = next;
            return false;
        }

        /// <summary>
        /// 마지막 안전장치: 길찾기가 실패해 곧장 가더라도 쓰고 있지 않은 용품 안으로는 들어가지 않는다 (그 가장자리를 따라 미끄러진다).
        /// 방석도 돌아간다(높이 13 cm: 바닥 높이로 지나가면 다리가 묻힌다). 이미 안에 있으면(쓰고 나오는 중, 방석 위에서 깸) 더 깊이 들어가는 쪽만 막는다.
        /// </summary>
        /// <summary>고양이끼리 너무 가까우면 살짝 비켜 선다 (어떤 걸음에서든 몸이 겹치지 않게).
        /// 뿌리 사이 거리만 보면 마주 선 큰 머리가 옆 고양이 몸에 파고든다: 머리(원)·몸통(선분+반지름)을 위에서 본 모양으로 잰다.</summary>
        Vector3 KeepApart(Vector3 next)
        {
            var start = next; var shift = Flat(next - transform.position);
            foreach (var o in All)
            {
                if (!o || o == this || !o.isActiveAndEnabled || o.OnTower || o.IsJumping()) continue;   // (점프 중인 고양이는 공중: 위에서 본 모양으로 재면 겹쳐 보인다)
                var away = Flat(next - o.transform.position); float d = away.magnitude;
                if (d < .34f && d > 1e-4f) next += away / d * (.34f - d) * .5f;
                float gap = Gap(this, o, out var dir, shift);
                if (gap < 0f)
                {
                    var pushed = next + dir * (-gap) * (o.Speed > .05f ? .5f : 1f);   // (상대가 서 있으면 내가 다 비킨다)
                    if (!IntoScenery(next, pushed)) { next = pushed; shift = Flat(next - transform.position); }
                }
            }
            // (한 걸음에 비키는 양은 걸음 크기 정도까지: 크게 겹친 채 만나도 미끄러지듯 튀지 않고 몇 걸음에 걸쳐 비킨다)
            var push = Flat(next - start); float cap = Mathf.Max(.04f, Speed * Time.deltaTime * 1.5f);
            if (push.magnitude > cap) next = start + push.normalized * cap;
            return next;
        }

        /// <summary>
        /// 멈춰 있어도 겹치지 않게: 걷는 중에는 KeepApart 가 막지만, 도착해 앉았거나 앞이 막혀 기다리는 고양이끼리 3 cm 넘게 겹치면
        /// 천천히(최대 0.5 m/s, 걷는 빠르기) 비켜 선다. 자리가 정해진 고양이(용품 쓰기·밥 먹기·쓰다듬기·캣타워·점프·산책)는 그대로 두고 상대가 비킨다.
        /// </summary>
        void Separate(float dt)
        {
            if (Pinned(this)) return;
            Vector3 push = Vector3.zero;
            foreach (var o in All)
            {
                if (!o || o == this || !o.isActiveAndEnabled || o.OnTower || o.IsJumping()) continue;
                float g = Gap(this, o, out var dir); if (g >= -.03f) continue;
                float share = Pinned(o) || (o.Speed < .05f && Speed >= .05f) ? 1f : .5f;
                push += dir * (-g) * share;   // (닿을 때까지 민다: 3 cm 문턱 근처에서 느려지지 않게)
            }
            if (push.sqrMagnitude < 1e-10f) return;
            float max = .5f * dt; if (push.magnitude > max) push = push.normalized * max;
            var pos = transform.position; var next = ClampToIsland(SlideOffItems(pos, pos + push)); next.y = pos.y;
            if (IntoScenery(pos, next)) return;
            transform.position = next;
        }
        /// <summary>비켜 서다가 꽃밭·나무 같은 경치(용품 아닌 장애물) 쪽으로 더 들어가는가 (SlideOffItems 는 용품만 본다).</summary>
        bool IntoScenery(Vector3 from, Vector3 to)
        {
            if (nav == null) return false;
            foreach (var o in nav.obstacles)
            {
                if (o.owner != null || o.item != null) continue;
                float d = o.Distance(to); if (d < .2f && d < o.Distance(from)) return true;
            }
            return false;
        }
        static bool Pinned(CatBrain c) => c.OnTower || c.IsJumping() || c.State == CatState.UseItem || c.State == CatState.Eat || c.State == CatState.Petted || c.State == CatState.BellyUp
            || c.State == CatState.Nip || c.State == CatState.Treat || c.State == CatState.LeaveForWalk || c.State == CatState.ReturnFromWalk;

        /// <summary>두 고양이 몸 사이 틈 (m, 위에서 본 모양: 머리 원 + 몸통 캡슐, 털은 조금 눌려도 된다). 음수면 겹침, dir 은 a 를 b 에서 떼는 방향.
        /// shiftA: a 를 이만큼 옮겼다고 치고 잰다 (다음 걸음).</summary>
        public static float Gap(CatBrain a, CatBrain b, out Vector3 dir, Vector3 shiftA = default)
        {
            Shape(a, shiftA, out var ha, out var hra, out var a0, out var a1, out var bra);
            Shape(b, Vector3.zero, out var hb, out var hrb, out var b0, out var b1, out var brb);
            float best = float.MaxValue; Vector2 pa = default, pb = default;
            void Try(Vector2 p, Vector2 q, float r) { float g = (p - q).magnitude - r; if (g < best) { best = g; pa = p; pb = q; } }
            Try(ha, hb, hra + hrb);
            Try(ha, Closest(b0, b1, ha), hra + brb);
            Try(Closest(a0, a1, hb), hb, bra + hrb);
            // (몸통끼리: 네 끝점에서 상대 선분까지 - 엇갈려 겹치는 경우는 머리·뿌리 검사가 먼저 막는다)
            Try(a0, Closest(b0, b1, a0), bra + brb); Try(a1, Closest(b0, b1, a1), bra + brb);
            Try(Closest(a0, a1, b0), b0, bra + brb); Try(Closest(a0, a1, b1), b1, bra + brb);
            var v = pa - pb;
            if (v.sqrMagnitude < 1e-8f) { var f = Flat(a.transform.position + shiftA - b.transform.position); v = f.sqrMagnitude > 1e-8f ? new Vector2(f.x, f.z) : Vector2.right; }
            v.Normalize(); dir = new Vector3(v.x, 0f, v.y);
            return best;
        }

        static void Shape(CatBrain c, Vector3 shift, out Vector2 head, out float headR, out Vector2 b0, out Vector2 b1, out float bodyR)
        {
            var rig = c.Rig; Vector2 P(Vector3 w) => new Vector2(w.x + shift.x, w.z + shift.z);
            if (rig && rig.HeadZone && rig.BodyZone)
            {
                head = P(rig.HeadZone.position); headR = rig.HeadRadius * .62f;   // (HeadRadius 는 귀까지 넣은 80 % 값: 머리 공 자체는 그 약 60 %)
                var fw = Flat(rig.BodyZone.forward); if (fw.sqrMagnitude < 1e-6f) fw = Flat(c.transform.forward);
                fw.Normalize(); var bc = rig.BodyZone.position; float half = rig.BodyHalf.z;
                b0 = P(bc - fw * half); b1 = P(bc + fw * half); bodyR = rig.BodyHalf.x * .85f;
            }
            else { var p = P(c.transform.position); head = p; headR = .12f; b0 = b1 = p; bodyR = .12f; }
        }

        static Vector2 Closest(Vector2 s0, Vector2 s1, Vector2 p)
        {
            var d = s1 - s0; float l = d.sqrMagnitude; if (l < 1e-8f) return s0;
            return s0 + d * Mathf.Clamp01(Vector2.Dot(p - s0, d) / l);
        }

        Vector3 SlideOffItems(Vector3 pos, Vector3 next)
        {
            if (nav == null || OnTower) return next;
            var item = UsingItem(); var step = Flat(next - pos);
            const float body = .2f;   // (몸통은 뿌리보다 앞에 있다: 넉넉히)
            foreach (var o in nav.obstacles)
            {
                if (o.owner != null || o.item == null || o.item == item || (LeftItem && o.item == LeftItem && Time.time - LeftAt < 12f)) continue;
                float dn = o.Distance(next);
                if (dn >= body || dn >= o.Distance(pos)) continue;
                const float e = .01f;   // (바깥쪽 방향: 거리의 기울기)
                var n = new Vector3(o.Distance(next + Vector3.right * e) - o.Distance(next - Vector3.right * e), 0f, o.Distance(next + Vector3.forward * e) - o.Distance(next - Vector3.forward * e));
                if (n.sqrMagnitude < 1e-8f) continue; n.Normalize();
                float into = Vector3.Dot(step, n);
                if (into < 0f) step -= n * into;
                next = pos + step;
            }
            return next;
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

        static readonly string[] GroomClips = { "GroomFace", "ScratchEar", "NibbleClaws", "LickLips" };
        void TickGroom(float dt)
        {
            Speed = 0f;
            if (!actionStarted)
            {
                Rig.Request(Posture.Sit);
                if (Rig.Current == Posture.Sit && !Rig.Busy)
                {
                    actionStarted = true;
                    // 이 품종에 있는 그루밍 동작 중 하나 (새 그림체에서 닿지 않아 빠진 동작은 없다: 대신 입맛 다시기)
                    var have = GroomClips.Where(Rig.HasClip).ToList();
                    string clip = have.Count > 0 ? (have.Contains("GroomFace") && UnityEngine.Random.value < .5f ? "GroomFace" : have[UnityEngine.Random.Range(0, have.Count)]) : null;
                    if (clip != null) { Rig.PlayAction(clip, clip == "GroomFace"); stateTimer = Rig.ClipLength(clip); }
                    else stateTimer = 2f;
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
                if (TooFarToSettle(spot)) { Enter(CatState.SitIdle); return; }   // (막혀서·다른 고양이가 있어서 멈춘 것: 그 자리에서 쉰다)
                faceDir = face;
                transform.position = Vector3.Lerp(transform.position, new Vector3(spot.x, transform.position.y, spot.z), 1f - Mathf.Exp(-8f * dt));
                if (FacingAngle(face) < 4f && Flat(spot - transform.position).magnitude < .015f) Enter(bowl.HasFood ? CatState.Eat : CatState.WaitAtBowl);   // (그릇 자리에 다 와서: 혀가 그릇에 닿는 자리)
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
                crunchTimer = UnityEngine.Random.Range(0.45f, 0.7f);
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
            if (OtherCatNear(cushion.transform.position, cushion.Radius + .15f)) { Enter(CatState.SitIdle); return; }   // (가는 사이 다른 고양이가 올라갔다)
            var spot = CushionSpot(out var face);
            if (MoveTowards(spot, GameConfig.WalkSpeed, dt, 0.05f))
            {
                if (TooFarToSettle(spot)) { Enter(CatState.SitIdle); return; }
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
            if (Needs.Energy >= GameConfig.WakeEnergy && !touchingCat && !IsNight)
            {
                audioOut?.Chirp();
                Rig.Request(Posture.Stand);
                Enter(CatState.Stretch);
            }
        }

        // ---- 캣타워

        /// <summary>
        /// 점프 출발점: 판의 뒤쪽 끝이 출발점에서 clips.json 의 deckBack 만큼 앞에 오게 (qa_items.mjs 가 검사한 배치와 같다).
        /// 착지하면 고양이는 판 가운데보다 조금 뒤에 선다.
        /// </summary>
        // ---- 캣타워: 한 층씩 뛰어 오르고(높이별 점프 중 가장 가까운 것, 앞 거리는 실제 판 사이에 맞춤) 쉬다가 한 층씩 내려온다
        CatTower curTower;           // 지금 쓰는 캣타워
        int deck = -1, targetDeck, hopToDeck;
        CatRig.JumpSet hopSet; float hopScale = 1f; bool hopWalking;
        Vector3 hopTakeoff;

        CatTower PickTower()
        {
            CatTower best = null; float bd = float.MaxValue;
            foreach (var t in CatTower.All) { if (!t || !Free(t)) continue; float d = Flat(t.transform.position - transform.position).magnitude; if (d < bd) { bd = d; best = t; } }
            return best;
        }
        float Level => curTower ? curTower.Height(deck) : 0f;
        int forcedTarget = -1;
        /// <summary>이 캣타워의 이 층까지 올라가게 한다 (테스트·연출).</summary>
        public void ClimbTo(CatTower t, int targetDeckIndex) { curTower = t; forcedTarget = targetDeckIndex; Enter(CatState.GoToTower); }

        /// <summary>다음 한 번의 뛰기를 정한다: 출발 자리(지금 서 있는 면 위), 방향, 점프, 거리 맞춤.</summary>
        bool PlanHop(int to)
        {
            var t = curTower; bool up = t.Height(to) > Level; float dh = t.Height(to) - Level;
            hopSet = Rig.NearestJump(up, dh);
            float D, back, edge;
            var legacy = up || !HasJumpDownClip ? Rig.Info.jump : Rig.Info.jumpDown;   // (예전 에셋: JumpDown 곡선이 비어 있으면 JumpUp)
            if (hopSet != null) { D = hopSet.D; back = hopSet.deckBack; edge = hopSet.edge; }
            else { if (legacy?.forward == null || legacy.forward.Length == 0) return false; D = legacy.forward[legacy.forward.Length - 1]; back = Rig.Info.deckBack; edge = D - Rig.Info.deckBack + Rig.Info.turnIn; }
            Vector3 from = deck >= 0 ? t.Center(deck) : transform.position;
            Vector3 dir;
            if (to >= 0) { dir = Flat(t.Center(to) - from); if (dir.magnitude < .25f) dir = t.transform.forward; }
            else dir = Flat(transform.position - t.transform.position).sqrMagnitude > .01f ? Flat(transform.position - t.transform.position) : -t.transform.forward;
            if (deck < 0 && to == 0 && t.Decks.Count == 1) dir = t.transform.forward;                       // (낮은 1단: qa_items 와 같은 방향)
            if (deck == 0 && to < 0 && t.Decks.Count == 1) dir = -t.transform.forward;
            dir = dir.normalized;
            if (up)
            {
                // 판 앞 가장자리에서 deckBack 만큼 앞에서 출발하면 뒷발까지 판 위에 내린다
                var nearEdge = t.Center(to) - dir * t.Extent(to, dir);
                hopTakeoff = nearEdge - dir * back; hopTakeoff.y = Level;
                if (deck >= 0 && !t.Inside(deck, hopTakeoff, .12f)) hopTakeoff = t.ClampInto(deck, hopTakeoff, .15f);
                var land = t.Center(to) + dir * 0f;
                float need = Flat(nearEdge + dir * Mathf.Min(.25f, t.Extent(to, dir)) - hopTakeoff).magnitude;   // (뒷발이 판 안쪽 25 cm 에 닿게)
                hopScale = deck >= 0 ? Mathf.Clamp(need / Mathf.Max(.3f, D - back + .25f) , .55f, 1.4f) : 1f;
            }
            else
            {
                // 판 끝에서 edge 만큼 안쪽에서 출발 (뒷발이 판에 걸리지 않게), 아래 판이면 그 판 위에 내리도록 거리 맞춤
                var farEdge = (deck >= 0 ? t.Center(deck) : from) + dir * (deck >= 0 ? t.Extent(deck, dir) : 0f);
                hopTakeoff = farEdge - dir * edge; hopTakeoff.y = Level;
                if (deck >= 0 && !t.Inside(deck, hopTakeoff, .1f)) hopTakeoff = t.ClampInto(deck, hopTakeoff, .12f);
                hopScale = 1f;
                if (to >= 0) { float need = Flat(t.Center(to) - hopTakeoff).magnitude; hopScale = Mathf.Clamp(need / Mathf.Max(.3f, D), .55f, 1.4f); }
                // (바닥에 내릴 자리는 섬 안이면 된다: 캣타워 바로 옆이라 길찾기 여유 반경 안이다)
            }
            jumpDir = dir; hopToDeck = to; hopWalking = true;
            return true;
        }

        void TickGoToTower(float dt)
        {
            if (curTower == null) curTower = PickTower();
            if (!curTower || (Rig.Info.jump == null && Rig.Info.jumps == null)) { Enter(CatState.Idle); return; }
            if (StateTime < dt * 1.5f) { deck = -1; targetDeck = forcedTarget >= 0 ? forcedTarget : UnityEngine.Random.value < .5f ? curTower.TopDeck : UnityEngine.Random.Range(0, curTower.Decks.Count); forcedTarget = -1; int first = curTower.NextUp(-1, transform.position); if (first < 0 || !PlanHop(first)) { Enter(CatState.Idle); return; } }
            if (MoveTowards(hopTakeoff, GameConfig.WalkSpeed, dt, 0.05f))
            {
                if (TooFarToSettle(hopTakeoff)) { Enter(CatState.Idle); return; }
                faceDir = jumpDir;
                transform.position = Vector3.Lerp(transform.position, new Vector3(hopTakeoff.x, 0f, hopTakeoff.z), 1f - Mathf.Exp(-8f * dt));
                if (FacingAngle(faceDir) < 3f) { transform.rotation = Quaternion.LookRotation(jumpDir); StartHop(); }
            }
            else if (StateTime > 16f) Enter(CatState.Idle);
        }

        bool HasJumpDownClip => Rig.Info.jumpDown != null && Rig.Info.jumpDown.forward != null && Rig.Info.jumpDown.forward.Length > 1 && Rig.HasClip("JumpDown");

        void StartHop()
        {
            bool up = curTower.Height(hopToDeck) > Level;
            var s = up ? CatState.JumpUp : CatState.JumpDown;
            Rig.Request(Posture.Stand);
            Enter(s);
            jumpStart = new Vector3(hopTakeoff.x, 0f, hopTakeoff.z);
            jumpFrom = Level; jumpTo = curTower.Height(hopToDeck);
            string clip = hopSet != null ? hopSet.clip : up ? "JumpUp" : HasJumpDownClip ? "JumpDown" : "JumpUp";
            Rig.PlayAction(clip, false, 0.2f);
        }

        /// <summary>
        /// 점프 이동: 클립의 루트 곡선(앞으로 간 거리, 높이)을 따른다. 앞 거리는 판 사이에 맞게 늘이거나 줄이고(hopScale),
        /// 높이는 출발·도착 높이 사이를 잇고 그 위에 클립의 포물선만 얹는다.
        /// </summary>
        void TickJump(float dt)
        {
            Speed = 0f;
            var c = hopSet != null ? hopSet.curve : State == CatState.JumpDown && HasJumpDownClip ? Rig.Info.jumpDown : Rig.Info.jump;
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
            transform.position = jumpStart + jumpDir * fwd * hopScale + Vector3.up * y;

            if (Rig.ActionClip == null)
            {
                transform.position = jumpStart + jumpDir * totalFwd * hopScale + Vector3.up * jumpTo;
                heightY = jumpTo; deck = hopToDeck; hopScale = 1f;
                OnTower = deck >= 0;
                audioOut?.Step(OnTower ? Surface.Rug : IslandBuilder.SurfaceAt(transform.position), 0.9f);
                if (!OnTower) { curTower = null; Enter(CatState.Idle); return; }
                hopWalking = false;
                Enter(CatState.OnTower);
            }
        }

        void TickOnTower(float dt)
        {
            Speed = 0f;
            if (!curTower) { curTower = tower; deck = 0; }
            if (StateTime < 0.1f) stateTimer = deck == targetDeck || leaveTower ? UnityEngine.Random.Range(8f, 14f) : .5f;
            bool goingUp = !leaveTower && deck != targetDeck && curTower.NextUp(deck, transform.position) >= 0 && curTower.Height(targetDeck) > curTower.Height(deck);
            if (!goingUp && !leaveTower && StateTime > 1.2f && Rig.Current == Posture.Stand && !Rig.Busy) Rig.Request(UnityEngine.Random.value < 0.5f ? Posture.Sit : Posture.Loaf);
            if (!goingUp && !leaveTower && cam && Rig.Current != Posture.Loaf) faceDir = Flat(cam.position - transform.position);
            stateTimer -= dt;
            if (!goingUp && (stateTimer <= 0f || Needs.IsHungry || Needs.IsSleepy)) leaveTower = true;
            if (!goingUp && !leaveTower) return;
            if (goingUp && stateTimer > 0f) return;
            if (Rig.Current != Posture.Stand) { Rig.Request(Posture.Stand); return; }
            if (Rig.Busy) return;
            // 다음 층 (오르기) 또는 한 층 아래 (내려가기) 자리로: 판 위에서 천천히 걸어가 돌아선다
            if (!hopWalking)
            {
                int to = goingUp ? curTower.NextUp(deck, transform.position) : curTower.NextDown(deck, transform.position);
                if (!PlanHop(to)) { if (!goingUp) { stateTimer = 1f; } return; }
            }
            var flat = Flat(hopTakeoff - transform.position);
            if (flat.magnitude > .02f)
            {
                float step = Mathf.Min(flat.magnitude, .35f * dt);
                transform.position += flat.normalized * step;
                faceDir = flat.magnitude > .08f ? flat : (Vector3?)null;
                Speed = .12f;
                return;
            }
            faceDir = null;
            float angle = Vector3.SignedAngle(transform.forward, jumpDir, Vector3.up);
            transform.Rotate(0f, Mathf.Clamp(angle, -150f * dt, 150f * dt), 0f);
            Speed = 0.12f;   // (발을 옮기며 도는 것처럼 걷기를 아주 느리게 재생)
            if (Mathf.Abs(angle) < 2f)
            {
                Speed = 0f; hopWalking = false;
                if (!goingUp && curTower.Height(hopToDeck) <= 0.01f) leaveTower = false;
                StartHop();
            }
        }

        // ---- 용품 쓰기: 장난감 치기, 스크래처에서 기지개, 화장실에서 파기, 숨숨집에서 식빵, (모델이 아직 없는 용품은 옆에 앉기)
        ItemTag useTarget; string useKind; Vector3 useSpot; float useYaw, useLift; int useStep; float useTimer;
        /// <summary>방금 쓰고 나온 용품 (나오는 동안은 그 바닥 높이를 지킨다, 테스트도 이것으로 안다).</summary>
        public Transform LeftItem { get; private set; }
        bool viaDoor;
        /// <summary>문으로 들어가는 마지막 걸음 중 (그 용품 안에 있는 것이 맞다: 테스트).</summary>
        public Transform EnteringItem => State == CatState.GoToItem && !viaDoor && useTarget ? useTarget.transform : null;
        public float LeftAt { get; private set; } = -99f;
        float leftLift;
        /// <summary>새로 놓인 용품을 바로 써 본다 (느낌표 말풍선). 기획서 2-2: 고양이가 바로 그 용품을 써 보는 것이 보상.</summary>
        public bool TryNewItem(ItemTag t)
        {
            if (!t || !IsFree() || OnTower || State == CatState.Sleep || !Free(t)) return false;
            if (!BeginUse(t)) return false;
            Bubble?.Show(Icon.Exclaim); audioOut?.Chirp(); return true;
        }
        ItemTag PickItemUse()
        {
            var all = ItemTag.All; if (all.Count == 0) return null;
            ItemTag best = null; float bs = 0f;
            foreach (var t in all)
            {
                if (!t || !Free(t) || Kind(t.id) == null) continue;
                var d = CatIsland.Game.Catalog.Item(t.id); float sc = UnityEngine.Random.value;
                if (Data != null)
                {
                    if (System.Array.IndexOf(CatIsland.Game.Catalog.FavoriteItems(Data.personality), t.id) >= 0) sc += .8f;
                    if (d.fills == CatIsland.Game.NeedKind.Play && Data.play < .6f) sc += .6f;
                    if ((t.id == "litter_box" || t.id == "sandbox") && Data.clean < .7f) sc += .5f;
                }
                if (sc > bs) { bs = sc; best = t; }
            }
            return best;
        }
        static string Kind(string id)
        {
            var d = CatIsland.Game.Catalog.Item(id); if (d == null || d.consumable) return null;
            if (id == "litter_box" || id == "sandbox") return "litter";
            if (id == "scratcher") return "stretch";
            if (id == "hideout" || id == "pumpkin_house" || id == "hanok_hideout") return "hide";
            if (BedTop(id) > 0f) return "bed";
            if (d.category == CatIsland.Game.ItemCategory.Toy && id != "paper_box" && id != "tunnel") return "bat";
            if (d.category == CatIsland.Game.ItemCategory.Food || d.category == CatIsland.Game.ItemCategory.Tower) return null;
            return "near";
        }
        [Serializable] class TopInfo { public TopAnchors anchors; } [Serializable] class TopAnchors { public float top; }
        static readonly Dictionary<string, float> bedTops = new Dictionary<string, float>();
        /// <summary>고양이가 올라가 눕는 면의 높이 (용품 JSON anchors.top, 방석 계열은 0.134). 없으면 0.</summary>
        static float BedTop(string id)
        {
            if (bedTops.TryGetValue(id, out var v)) return v;
            var d = CatIsland.Game.Catalog.Item(id); var txt = d != null ? ItemLoader.InfoText(d.model) : null;
            v = 0f; if (txt != null) { try { v = JsonUtility.FromJson<TopInfo>(txt)?.anchors?.top ?? 0f; } catch { } }
            if (id == "petal_cushion" || id == "melon_cushion") v = .134f;
            return bedTops[id] = v;
        }
        bool BeginUse(ItemTag t)
        {
            useKind = Kind(t.id); if (useKind == null) return false;
            useTarget = t; useStep = 0; useLift = 0f; viaDoor = false;
            var tp = t.transform.position; var f = Flat(t.transform.forward); if (f.sqrMagnitude < .01f) f = Vector3.forward; f.Normalize();
            var toItem = Flat(tp - transform.position); if (toItem.sqrMagnitude < .01f) toItem = -f; toItem.Normalize();
            float yawTo = Mathf.Atan2(toItem.x, toItem.z) * Mathf.Rad2Deg;
            switch (useKind)
            {
                case "bat":   // 쥐돌이가 고양이 앞 (toyX, toyZ) 에 오게 선다
                    useYaw = yawTo; useSpot = tp - Quaternion.Euler(0, useYaw, 0) * new Vector3(Rig.Info.toyX, 0, Rig.Info.toyZ); break;
                case "stretch":   // 앞발이 판 위에 닿게: 판 가운데에서 몸 반만큼 뒤
                    useYaw = yawTo; useSpot = tp - toItem * .38f; break;
                case "litter":    // 통 가운데, 긴 쪽을 따라
                    useYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; useSpot = tp; useLift = .15f; if (t.id == "sandbox") useLift = .05f; break;
                case "bed":       // 침대·벤치·해먹 위에서 식빵 (밤에는 잠)
                    useYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 90f; useSpot = tp; useLift = BedTop(t.id); break;
                case "hide":      // 몸은 안에, 머리는 문 밖 (qa_items 숨숨집 장면과 같은 자리)
                    viaDoor = true;
                    useYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; useSpot = tp - Quaternion.Euler(0, useYaw, 0) * new Vector3(0, 0, Rig.Info.hideZ); useLift = Rig.Info.hideLift; break;
                default:          // 옆에 앉아 바라보기
                    useYaw = yawTo; useSpot = tp - toItem * .7f; break;
            }
            useSpot.y = 0f;
            Enter(CatState.GoToItem); return true;
        }
        void TickGoToItem(float dt)
        {
            if (!useTarget || OtherCatNear(useSpot, .5f)) { Enter(CatState.Idle); return; }   // (다른 고양이가 그 자리에 있으면 다음에)
            bool inside = useKind == "litter" || useKind == "hide" || useKind == "bed";
            if (viaDoor)
            {
                // 숨숨집·침대는 먼저 문 앞으로 (뒤에서 곧장 가면 벽을 뚫는다), 그다음 문으로 곧게 들어간다
                var door = useSpot + Quaternion.Euler(0f, useYaw, 0f) * Vector3.forward * .75f;
                if (nav != null) door = nav.NearestFree(door);   // (문 앞 점이 막힘 거리 안이면 길찾기가 실패해 곧장 (벽을 뚫고) 간다)
                if (MoveTowards(door, GameConfig.WalkSpeed, dt, .1f) || Flat(door - transform.position).magnitude < .12f) viaDoor = false;
                else if (StateTime > 14f) Enter(CatState.Idle);
                return;
            }
            if (MoveTowards(useSpot, GameConfig.WalkSpeed, dt, inside ? .06f : .1f) || (inside && Flat(useSpot - transform.position).magnitude < .45f))
            {
                // (통·숨숨집 안으로는 마지막 몇 걸음을 곧게: 길찾기는 물건 안을 막아 둔다)
                if (OtherCatNear(transform.position, .3f)) { Enter(CatState.Idle); return; }   // (다른 고양이가 바로 곁이면 다음에)
                transform.position = Vector3.MoveTowards(transform.position, new Vector3(useSpot.x, transform.position.y, useSpot.z), .3f * dt);
                faceDir = Quaternion.Euler(0, useYaw, 0) * Vector3.forward;
                if (Flat(useSpot - transform.position).magnitude < .03f && FacingAngle(faceDir) < 4f) Enter(CatState.UseItem);
            }
            else if (StateTime > 14f) Enter(CatState.Idle);
        }
        void TickUseItem(float dt)
        {
            if (!useTarget) { Enter(CatState.Idle); return; }
            Speed = 0f; useTimer -= dt;
            if (useStep == 0)
            {
                useStep = 1;
                switch (useKind)
                {
                    case "bat": Rig.Request(Posture.Stand); Rig.PlayAction("PawBat", true, .2f); useTimer = 3.2f; break;
                    case "stretch": Rig.Request(Posture.Stand); Rig.PlayAction("Stretch", false, .2f); useTimer = 4.3f; ItemJiggle.Poke(useTarget.transform, .35f); break;
                    case "litter": Rig.Request(Posture.Stand); if (Rig.HasClip("Dig")) Rig.PlayAction("Dig", false, .2f); useTimer = 2.5f; break;
                    case "hide": Rig.Request(Posture.Loaf); useTimer = UnityEngine.Random.Range(8f, 16f); ItemJiggle.Poke(useTarget.transform, .7f); break;
                    case "bed": Rig.Request(IsNight ? Posture.Sleep : Posture.Loaf); useTimer = IsNight ? 60f : UnityEngine.Random.Range(10f, 20f); ItemJiggle.Poke(useTarget.transform, .6f); break;
                    default: Rig.Request(Posture.Sit); useTimer = UnityEngine.Random.Range(3f, 6f); break;
                }
                return;
            }
            // 장난감: 앞발로 칠 때마다 조금 굴러갔다 돌아온다 (3.2 초 동안 두 번)
            if (useKind == "bat" && BatTapNow(dt))
            {
                var tid = useTarget.id;   // (서 있는 장난감은 흔들리고, 굴러가는 장난감은 굴러간다)
                if (tid == "wand_toy" || tid == "feather_stand" || tid == "yarn_basket") ItemJiggle.Poke(useTarget.transform, .6f);
                else ItemJiggle.Kick(useTarget.transform, Flat(useTarget.transform.position - transform.position), .18f);
            }
            if (useKind == "litter" && useStep == 1 && useTimer <= 0f) { useStep = 2; Rig.Request(Posture.Sit); useTimer = 3f; return; }
            if (useKind == "litter" && useStep == 2 && useTimer <= 0f) { useStep = 3; Rig.Request(Posture.Stand); if (Rig.HasClip("Dig")) Rig.PlayAction("Dig", false, .2f); useTimer = 2.5f; return; }
            if (useTimer > 0f) return;
            if (useKind == "bat" || useKind == "stretch") Rig.StopAction();
            var id = useTarget.id; LeftItem = useTarget.transform; LeftAt = Time.time; leftLift = useLift; var kind = useKind; useTarget = null;
            if (Data != null) OnUsedItem?.Invoke(id);
            if (Rig.Current != Posture.Stand) Rig.Request(Posture.Stand);
            if (kind == "hide" || kind == "bed")
            {
                // 숨숨집·침대에서 나올 때는 문(앞) 쪽으로 걸어 나온다: 바로 다른 곳으로 가면 벽을 뚫고 지나간다
                Enter(CatState.Wander);
                moveTarget = useSpot + Quaternion.Euler(0f, useYaw, 0f) * Vector3.forward * .75f;
                if (nav != null) moveTarget = nav.NearestFree(moveTarget);
            }
            else Enter(CatState.Idle);
        }
        /// <summary>앞발이 장난감을 치는 순간 (PawBat 한 바퀴의 58 %: qa_items 와 같은 때). 이때 장난감이 굴러간다.</summary>
        bool BatTapNow(float dt)
        {
            float len = Rig.ClipLength("PawBat"), e = 3.2f - useTimer - .2f;   // (시작 뒤 .2 초는 동작으로 넘어가는 섞임)
            if (len <= 0f || e < 0f) return false;
            float ph = e % len / len, prev = (e - dt) % len / len;
            return prev < .58f && ph >= .58f || (dt > 0f && e - dt < 0f && ph >= .58f);
        }

        /// <summary>용품을 다 썼다 (게임 규칙 쪽: 놀이·할 일).</summary>
        public Action<string> OnUsedItem;
        /// <summary>다른 고양이와 쫓기 놀이를 마쳤을 때 (놀이 상태가 둘 다 오른다: Game.PlayTogether).</summary>
        public Action<CatBrain> OnFriendPlay;

        static bool IsNight => GameBootstrap.Instance && GameBootstrap.Instance.Day && GameBootstrap.Instance.Day.Night;

        // ---- 츄르: 앞으로 와서 앉아 손에 든 츄르를 핥는다 (손은 보이지 않는다: 츄르만 입 앞에 떠 있다)
        Action<Vector3, Quaternion> treatShow; Action treatDone;
        public bool GiveTreat(Action<Vector3, Quaternion> showProp, Action done)
        {
            if (OnTower || IsJumping()) return false;
            treatShow = showProp; treatDone = done; moveTarget = InviteSpot(); Enter(CatState.Treat); return true;
        }
        bool treatStarted;
        void TickTreat(float dt)
        {
            if (StateTime < dt * 1.5f) treatStarted = false;
            if (!treatStarted)
            {
                if (!MoveTowards(moveTarget, GameConfig.WalkSpeed, dt) && StateTime < 10f) return;
                if (cam) faceDir = Flat(cam.position - transform.position);
                if (FacingAngle(faceDir) > 8f && StateTime < 12f) return;
                Rig.Request(Posture.Sit);
                if (Rig.Current != Posture.Sit && StateTime < 14f) return;
                var tip = transform.TransformPoint(Rig.Info.lickTip);
                treatShow?.Invoke(tip, Quaternion.LookRotation(-transform.forward + Vector3.up * .3f)); treatShow = null;
                if (Rig.HasClip("LickUp")) Rig.PlayAction("LickUp", true, .2f); else Rig.mouthOpen = .6f;
                stateTimer = 4f; treatStarted = true; audioOut?.Chirp();
                return;
            }
            stateTimer -= dt; if (stateTimer <= 0f) FinishTreat();
        }
        void FinishTreat() { Rig.StopAction(); Rig.mouthOpen = 0f; var d = treatDone; treatDone = null; d?.Invoke(); FxPool.Instance?.Burst(Icon.Heart, Rig.BubbleAnchor.position, 4, .25f, .22f); Enter(CatState.SitIdle); }

        // ---- 산책: 바닷가 쪽으로 걸어 나가 섬에서 사라지고, 돌아올 때는 선물을 물고 걸어 들어온다
        Action leaveDone; Transform carried;
        public void LeaveForWalk(Action gone)
        {
            leaveDone = gone; moveTarget = ClampToIsland(new Vector3(4.4f, 0f, -2.6f)); if (nav != null) moveTarget = nav.NearestFree(moveTarget);
            if (OnTower) { OnTower = false; heightY = 0; }
            Enter(CatState.LeaveForWalk);
        }
        void TickLeave(float dt)
        {
            if (MoveTowards(moveTarget, GameConfig.WalkSpeed * 1.3f, dt) || StateTime > 12f) { var d = leaveDone; leaveDone = null; d?.Invoke(); }
        }
        public void ReturnFromWalk(GameObject gift)
        {
            transform.position = ClampToIsland(new Vector3(4.4f, 0f, -2.6f)); transform.rotation = Quaternion.LookRotation(-transform.position.normalized);
            if (gift) { carried = gift.transform; carried.SetParent(Rig.Head, false); carried.localPosition = Rig.Head.InverseTransformPoint(Rig.HeadZone.position + transform.forward * (Rig.HeadRadius * .9f) - Vector3.up * Rig.HeadRadius * .45f); carried.localScale = Vector3.one * .6f; }
            moveTarget = nav != null ? nav.NearestFree(new Vector3(.6f, 0f, -.8f)) : new Vector3(.6f, 0, -.8f);
            Rig.mouthOpen = .35f; Enter(CatState.ReturnFromWalk);
        }
        void TickReturn(float dt)
        {
            if (!MoveTowards(moveTarget, GameConfig.WalkSpeed, dt) && StateTime < 12f) return;
            if (carried) { FxPool.Instance?.Burst(Icon.Sparkle, carried.position, 6, .3f, .2f); Destroy(carried.gameObject); carried = null; }
            Rig.mouthOpen = 0f; audioOut?.Chirp(); Enter(CatState.SitIdle);
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
                if (nav != null) moveTarget = nav.NearestFree(moveTarget);
                Enter(CatState.Called);
            }
        }

        // ---------------- 표정과 자세

        void UpdateExpression(float dt)
        {
            float p = Pet.Pleasure;
            var r = Rig;
            r.moveSpeed = Mathf.Max(Speed, turnStep);
            turnStep = Mathf.MoveTowards(turnStep, 0f, dt * 0.6f);
            bool sleep = State == CatState.Sleep || r.Current == Posture.Sleep;
            float happyClose = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, p));
            r.eyeOpen = sleep ? 0f : 1f - happyClose;
            r.tailWag = State == CatState.Nip ? 1f : p * 0.5f;

            bool petting = touchingCat && (State == CatState.Petted || State == CatState.BellyUp);
            r.petLean = petting ? 1f : 0f;
            if (touchingCat) FingerGlow.Show(pointerWorld);
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
            else if (State == CatState.Visit && friend && actionStarted) r.lookTarget = friend.Rig.Head.position;
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
            float step = Mathf.Clamp(angle, -turnSpeed * dt, turnSpeed * dt);
            transform.Rotate(0f, step, 0f);
            // 제자리에서 돌 때도 발을 옮긴다 (미끄러지듯 도는 대신 아주 느린 걸음)
            if (Mathf.Abs(step) / dt > 20f) turnStep = 0.13f;
        }

        /// <summary>
        /// 발소리: 걸음 주기에 맞춰 (앞발 두 번 = 한 주기). 걷기 0.75 s, 종종걸음 0.4 s, 달리기 0.29 s 주기.
        /// 바닥 재질은 위치로 정한다 (마루, 러그, 풀밭, 모래밭, 캣타워·방석 위).
        /// </summary>
        void UpdateFootsteps(float dt)
        {
            if (Speed < 0.05f || Rig.Current != Posture.Stand || IsJumping()) { stepPhase = 0.6f; return; }
            float cycle = Speed < 0.4f ? 0.75f / Mathf.Max(0.3f, Speed / 0.4f)
                : Speed < 1.06f ? Mathf.Lerp(0.75f, 0.4f, (Speed - 0.4f) / 0.66f)
                : Mathf.Lerp(0.4f, 0.29f, Mathf.Clamp01((Speed - 1.06f) / 1.21f));
            stepPhase += dt / (cycle * 0.5f);
            if (stepPhase < 1f) return;
            stepPhase -= 1f;
            audioOut?.Step(SurfaceUnderfoot(), 0.45f + 0.55f * Mathf.Clamp01(Speed / GameConfig.RunSpeed));
        }

        Surface SurfaceUnderfoot()
        {
            if (OnTower) return Surface.Rug;   // (캣타워 판은 카펫)
            if (cushion && Flat(transform.position - cushion.transform.position).magnitude < cushion.Radius) return Surface.Rug;
            return IslandBuilder.SurfaceAt(transform.position);
        }

        /// <summary>바닥 높이: 캣타워 판 위, 방석 위(방석 중심 가까이 갈수록 올라감), 그 밖은 땅.</summary>
        void ApplyHeight(float dt)
        {
            float target = 0f;
            if (OnTower && curTower) target = curTower.Height(deck);
            else if (State == CatState.UseItem && useLift > 0f) target = useLift + (useTarget ? Mathf.Max(0f, useTarget.transform.position.y - .004f) : 0f);   // (데크 타일 위 용품: 그만큼 위)
            else if (LeftItem && leftLift > 0f && Time.time - LeftAt < 12f && Flat(transform.position - LeftItem.position).magnitude < .55f) target = leftLift;   // (나오는 동안 바닥에 묻히지 않게)
            else if (cushion)
            {
                float d = Flat(transform.position - cushion.transform.position).magnitude;
                target = (cushion.TopHeight + Mathf.Max(0f, cushion.transform.position.y - .004f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cushion.Radius * 0.55f, cushion.Radius * 1.05f, d)));
            }
            if (!(OnTower && curTower)) target = Mathf.Max(target, FloorTiles.HeightAt(transform.position));   // (데크 타일 위: 발이 타일에 묻히지 않게)
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
                if (nav != null && nav.Blocked(p)) continue;
                return p;
            }
            return Vector3.zero;
        }

        Vector3 InviteSpot()
        {
            if (!cam) return Vector3.zero;
            Vector3 toCam = Flat(cam.position);
            int k = Mathf.Max(0, All.IndexOf(this));
            var side = Vector3.Cross(Vector3.up, toCam.normalized) * ((k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * .8f);
            var p = ClampToIsland(toCam.normalized * 1.6f + side);
            return nav != null ? nav.NearestFree(p) : p;
        }

        public static Vector3 ClampToIsland(Vector3 p)
        {
            p.y = 0f;
            return Vector3.ClampMagnitude(p, GameConfig.IslandWalkRadius);
        }

        // ---------------- 테스트와 스크린샷용

        public void ForceState(CatState s) => Enter(s);
        public void ForceOnTower(bool on) { OnTower = on; curTower = on ? tower : null; deck = on ? 0 : -1; targetDeck = 0; heightY = on && tower ? tower.DeckHeight : 0f; }
    }
}
