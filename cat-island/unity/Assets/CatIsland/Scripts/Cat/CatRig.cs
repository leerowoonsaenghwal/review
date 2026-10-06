using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CatIsland
{
    public enum Posture { Stand, Sit, Loaf, Sleep, Flop }

    /// <summary>
    /// 파이프라인에서 만든 고양이 모델(뼈 43개, 동작 20개, 표정 2개)을 움직인다.
    /// - 자세: 서기/앉기/식빵/잠/발라당 사이는 전환 동작(SitDown, StandUp, LieDown, FallAsleep, Flop)으로 잇는다
    /// - 이동: 걷기·종종걸음·달리기는 속도로 섞는다 (Locomotion 블렌드). 실제 이동은 CatBrain 이 한다
    /// - 표정과 시선: 동작 위에 깜빡임, 기분 좋게 감은 눈, 입 벌림, 고개 돌리기를 더한다
    /// - 쓰다듬기 판정: 머리·몸 영역을 메시에서 자동으로 맞춘다
    /// </summary>
    public class CatRig : MonoBehaviour
    {
        public string breed = "korean_shorthair";
        public string eyeStyle = "", whiskerStyle = "";   // (비면 품종 기본: CatFace)
        public string coatJson = "";                      // 사진 고양이 털 색 (CatCoat). 비면 품종 그대로

        // ---- 목표값 (CatBrain이 씀) ----
        [HideInInspector] public float moveSpeed;
        [HideInInspector] public float eyeOpen = 1f;     // 0 = 기분 좋게 감음
        [HideInInspector] public float mouthOpen;
        [HideInInspector] public float headTilt;         // 고개 갸웃 (도)
        [HideInInspector] public float tailWag;
        [HideInInspector] public float lookWeight = 1f;
        [HideInInspector] public float petLean;          // 쓰다듬는 중 (0~1): 골골 떨림, 귀 편하게 뒤로
        public Vector3? lookTarget;

        public Animator Anim { get; private set; }
        public Transform Model { get; private set; }
        public Transform Head { get; private set; }
        public Transform HeadZone { get; private set; }
        public Transform BodyZone { get; private set; }
        public Transform BubbleAnchor { get; private set; }
        public CatArtInfo Info { get; private set; }
        public float HeadRadius { get; private set; }

        public Posture Current { get; private set; } = Posture.Stand;
        public Posture Target { get; private set; } = Posture.Stand;
        public string Playing { get; private set; } = "Idle";
        public string ActionClip { get; private set; }
        public bool InTransition => transitionLeft > 0f;
        public bool Busy => InTransition || ActionClip != null;
        public bool CanMove => Current == Posture.Stand && !Busy;

        Transform neck, rootBone;
        Vector3 rootBindLocal;   // Root 뼈의 기본 위치 (고양이 기준)
        Transform[] tail;
        SkinnedMeshRenderer face;
        int blinkIdx = -1, mouthIdx = -1;
        readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
        float transitionLeft, actionLeft;
        Posture pendingPosture;
        bool actionLoop;
        float blinkTimer = 2f, blinkT = -1f, sEye = 1f, sMouth, sTilt, sWag;
        Vector2 look;

        [Serializable] public class RootCurve { public string clip; public float fps; public float[] forward; public float[] up; }
        /// <summary>높이별 점프 하나 (JumpUp / JumpUp40 / JumpUp80 / JumpDown …): 클립 이름, 오르기인지, 높이 H, 앞으로 D, 루트 곡선.</summary>
        [Serializable] public class JumpSet { public string clip; public bool up; public float H, D, deckBack, turnIn, edge; public RootCurve curve; }
        [Serializable] public class CatArtInfo
        {
            public string id; public string[] clips; public string[] loops; public RootCurve jump; public float headRadius;
            public float bowlX, bowlZ = 0.54f, cushionZ, cushionLift = 0.134f;
            public Vector3 flopBelly = Vector3.down;
            public float jumpD = 0.8f, jumpH = 0.2f, deckBack = 0.5f, turnIn;
            public RootCurve jumpDown;
            public JumpSet[] jumps;            // (높이별 점프: 새 에셋부터. 없으면 jump / jumpDown 하나)
            public float toyX, toyZ = .35f, toyYaw, hideZ = -.2f, hideLift = .08f; public Vector3 lickTip = new Vector3(0, .5f, .45f);
            public string faceEye = "", faceWhisker = "";   // 이 품종의 기본 눈 모양·수염
        }
        /// <summary>높이 차이 dh(m, 오르기 +)에 가장 가까운 점프. 높이별 점프가 없는 에셋은 null.</summary>
        public JumpSet NearestJump(bool up, float dh)
        {
            JumpSet best = null; float bd = float.MaxValue;
            if (Info.jumps != null) foreach (var j in Info.jumps) { if (j.up != up || !HasClip(j.clip)) continue; float d = Mathf.Abs(Mathf.Abs(j.H) - Mathf.Abs(dh)); if (d < bd) { bd = d; best = j; } }
            return best;
        }

        void Awake() { Build(); }

        public void Build()
        {
            if (Model != null) return;
            var prefab = Resources.Load<GameObject>("Art/Cats/" + breed);
            var infoText = Resources.Load<TextAsset>("Art/Cats/" + breed + "_info");
            if (prefab == null || infoText == null)
                throw new InvalidOperationException($"고양이 에셋이 없습니다: {breed}. tools/sync_art.py 와 CatIsland/Import Art 를 실행하세요.");
            Info = JsonUtility.FromJson<CatArtInfo>(infoText.text);

            Model = Instantiate(prefab, transform, false).transform;
            Model.name = "Model";
            Anim = Model.GetComponent<Animator>();
            Anim.applyRootMotion = false;
            foreach (var c in Anim.runtimeAnimatorController.animationClips) clipLen[c.name] = c.length;

            var bones = Model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
            Head = bones["Head"];
            rootBone = bones["Root"];
            rootBindLocal = transform.InverseTransformPoint(rootBone.position);
            neck = bones["Neck"];
            tail = Enumerable.Range(1, 10).Select(i => bones.TryGetValue("Tail" + i, out var t) ? t : null).Where(t => t != null).ToArray();

            face = Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh.blendShapeCount > 0);
            if (face)
            {
                blinkIdx = face.sharedMesh.GetBlendShapeIndex("Blink");
                mouthIdx = face.sharedMesh.GetBlendShapeIndex("MouthOpen");
                SetFace(eyeStyle, whiskerStyle);
            }

            FitZones(bones);
            // 움직임 층 (ART_DIRECTION 10-1, motionfeel.js): 꼬리·귀 스프링
            for (int i = 0; i < tail.Length; i++) springs.Add(new Spring { b = tail[i], k = 90f * (1f - .5f * i / Mathf.Max(1, tail.Length - 1)), c = 11f });
            foreach (var e in new[] { "Ear_L", "Ear_R" }) if (bones.TryGetValue(e, out var eb)) { springs.Add(new Spring { b = eb, k = 150f, c = 13f }); ears.Add(eb); }
            bones.TryGetValue("Chest", out chest);
            modelScale = Model.localScale;
            if (!string.IsNullOrEmpty(coatJson)) CatCoat.Apply(Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh.blendShapeCount == 0), breed, coatJson);
            BubbleAnchor = new GameObject("BubbleAnchor").transform;
            BubbleAnchor.SetParent(transform, false);
        }

        /// <summary>눈 모양·수염을 바꾼다 (만들기 화면 미리보기, 저장된 고양이).</summary>
        public void SetFace(string eye, string whisker)
        {
            eyeStyle = eye ?? ""; whiskerStyle = whisker ?? "";
            if (face) CatFace.Apply(face, breed, eyeStyle, whiskerStyle, Info?.faceEye, Info?.faceWhisker);
        }

        /// <summary>쓰다듬기 판정 영역: 머리(구)와 몸(캡슐)을 실제 메시 정점에서 맞춘다.</summary>
        void FitZones(Dictionary<string, Transform> bones)
        {
            var body = Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.blendShapeCount == 0);
            var baked = new Mesh();
            body.BakeMesh(baked, true);
            var verts = baked.vertices;
            var weights = body.sharedMesh.boneWeights;
            var boneNames = body.bones.Select(b => b.name).ToArray();
            var headPts = new List<Vector3>();
            var bodyPts = new List<Vector3>();
            var headSet = new HashSet<string> { "Head", "Ear_L", "Ear_R" };
            var bodySet = new HashSet<string> { "Spine1", "Spine2", "Chest", "Belly", "Hips" };
            for (int i = 0; i < verts.Length; i++)
            {
                string b = boneNames[weights[i].boneIndex0];
                Vector3 w = body.transform.TransformPoint(verts[i]);
                if (headSet.Contains(b)) headPts.Add(w);
                else if (bodySet.Contains(b)) bodyPts.Add(w);
            }
            Destroy(baked);

            Vector3 hc = Centroid(headPts);
            HeadRadius = Percentile(headPts.Select(p => (p - hc).magnitude), 0.8f);
            HeadZone = new GameObject("HeadZone").transform;
            HeadZone.SetParent(Head, true);
            HeadZone.SetPositionAndRotation(hc, transform.rotation);
            HeadZone.gameObject.AddComponent<SphereCollider>().radius = HeadRadius / Mathf.Max(0.0001f, HeadZone.lossyScale.x);

            Vector3 bc = Centroid(bodyPts);
            var local = bodyPts.Select(p => transform.InverseTransformDirection(p - bc)).ToList();
            float halfLen = Percentile(local.Select(v => Mathf.Abs(v.z)), 0.9f);
            float radius = Percentile(local.Select(v => new Vector2(v.x, v.y).magnitude), 0.75f);
            BodyZone = new GameObject("BodyZone").transform;
            BodyZone.SetParent(bones.ContainsKey("Spine2") ? bones["Spine2"] : bones["Hips"], true);
            BodyZone.SetPositionAndRotation(bc, transform.rotation);
            var cap = BodyZone.gameObject.AddComponent<CapsuleCollider>();
            float s = Mathf.Max(0.0001f, BodyZone.lossyScale.x);
            cap.direction = 2;
            cap.radius = radius / s;
            cap.height = (halfLen + radius) * 2f / s;
            bodyHalf = new Vector3(radius, radius, halfLen);
        }

        Vector3 bodyHalf;
        public Vector3 BodyHalf => bodyHalf;

        static Vector3 Centroid(List<Vector3> pts)
        {
            Vector3 c = Vector3.zero;
            foreach (var p in pts) c += p;
            return pts.Count > 0 ? c / pts.Count : Vector3.zero;
        }

        static float Percentile(IEnumerable<float> values, float q)
        {
            var arr = values.OrderBy(v => v).ToArray();
            return arr.Length == 0 ? 0f : arr[Mathf.Clamp(Mathf.RoundToInt(q * (arr.Length - 1)), 0, arr.Length - 1)];
        }

        /// <summary>충돌 지점이 어느 부위인지. 몸 기준 좌표라 앉거나 발라당 누워도 맞다.</summary>
        public PetZone ClassifyHit(Collider c, Vector3 worldPoint)
        {
            if (c.transform == HeadZone)
            {
                Vector3 d = HeadZone.InverseTransformPoint(worldPoint).normalized;
                if (d.y < -0.35f) return PetZone.Chin;
                if (d.y > 0.3f) return PetZone.Forehead;
                if (Mathf.Abs(d.x) > 0.55f) return PetZone.Cheek;
                return d.y < -0.1f ? PetZone.Chin : PetZone.Forehead;
            }
            if (c.transform == BodyZone)
            {
                Vector3 p = BodyZone.InverseTransformPoint(worldPoint);
                Vector3 d = new Vector3(p.x / bodyHalf.x, p.y / bodyHalf.y, p.z / bodyHalf.z).normalized;
                if (d.y < -0.35f) return PetZone.Belly;
                if (p.z < -bodyHalf.z * 0.4f) return PetZone.Butt;
                return PetZone.Back;
            }
            return PetZone.None;
        }

        public bool IsCatCollider(Collider c) => c != null && (c.transform == HeadZone || c.transform == BodyZone);

        // ------------------------------------------------------------------ 동작

        public float ClipLength(string clip) => clipLen.TryGetValue(clip, out var l) ? l : 1f;
        public bool HasClip(string clip) => clipLen.ContainsKey(clip);

        void Play(string state, float fade)
        {
            if (Playing == state) return;
            Playing = state;
            Anim.CrossFadeInFixedTime(state, fade);
        }

        bool walking;

        string BaseClip(Posture p)
        {
            switch (p)
            {
                case Posture.Sit: return "Sit";
                case Posture.Loaf: return "Loaf";
                case Posture.Sleep: return "Sleep";
                case Posture.Flop: return "FlopIdle";
                default: return walking ? "Move" : "Idle";
            }
        }

        /// <summary>서 있을 때: 멈춤은 Idle, 움직이면 Move(걷기·종종걸음·달리기 블렌드). 경계에서 깜빡이지 않게 여유를 둔다.</summary>
        void UpdateStand(float dt)
        {
            if (!walking && moveSpeed > 0.06f) walking = true;
            else if (walking && moveSpeed < 0.025f) walking = false;
            Play(walking ? "Move" : "Idle", walking ? 0.2f : 0.3f);
            Anim.SetFloat("Speed", Mathf.Max(moveSpeed, 0.4f));
            Anim.SetFloat("MoveRate", moveSpeed < 0.4f ? Mathf.Clamp(moveSpeed / 0.4f, 0.3f, 1f) : 1f);
        }

        /// <summary>자세 바꾸기. 필요하면 전환 동작을 거친다. 지금 하던 단발 동작은 끝낸다.</summary>
        public void Request(Posture p)
        {
            Target = p;
            if (ActionClip != null && !actionLoop) return; // 단발 동작이 끝나면 이어서
            StopAction();
            Step();
        }

        void Step()
        {
            if (InTransition || ActionClip != null || Current == Target) return;
            string clip = null; Posture next = Target; float fade = 0.25f;
            switch (Current)
            {
                case Posture.Stand:
                    if (Target == Posture.Sit) clip = "SitDown";
                    else if (Target == Posture.Loaf || Target == Posture.Sleep) { clip = "LieDown"; next = Posture.Loaf; }
                    else if (Target == Posture.Flop) clip = "Flop";
                    break;
                case Posture.Sit:
                    if (Target == Posture.Stand) clip = "StandUp";
                    else if (Target == Posture.Loaf || Target == Posture.Sleep) { clip = "LieDown"; next = Posture.Loaf; fade = 0.4f; }
                    else if (Target == Posture.Flop) { clip = "Flop"; fade = 0.4f; }
                    break;
                case Posture.Loaf:
                    if (Target == Posture.Sleep) clip = "FallAsleep";
                    else if (Target == Posture.Stand) { clip = "StandUp"; fade = 0.5f; }
                    else if (Target == Posture.Sit) { fade = 0.6f; }
                    else if (Target == Posture.Flop) { clip = "Flop"; fade = 0.5f; }
                    break;
                case Posture.Sleep:
                    next = Posture.Loaf; fade = 0.8f;
                    break;
                case Posture.Flop:
                    if (Target == Posture.Stand) { clip = "StandUp"; fade = 0.6f; }
                    else { next = Target == Posture.Sleep ? Posture.Loaf : Target; fade = 0.6f; if (next == Posture.Loaf) clip = null; }
                    break;
            }
            pendingPosture = next;
            if (clip != null && HasClip(clip))
            {
                Play(clip, fade);
                transitionLeft = ClipLength(clip) - 0.15f;
            }
            else
            {
                Current = next;
                Play(BaseClip(Current), fade);
                transitionLeft = fade;
                pendingPosture = Current;
            }
        }

        /// <summary>자세 위에서 하는 동작 (마시기, 그루밍, 기지개, 앞발 장난, 점프…). loop 면 StopAction 까지 반복.</summary>
        public void PlayAction(string clip, bool loop = false, float fade = 0.25f)
        {
            if (!HasClip(clip)) return;
            ActionClip = clip;
            actionLoop = loop;
            actionLeft = ClipLength(clip) - 0.1f;
            transitionLeft = 0f;
            Play(clip, fade);
        }

        public void StopAction()
        {
            if (ActionClip == null) return;
            ActionClip = null;
            Play(BaseClip(Current), 0.3f);
        }

        /// <summary>단발 동작이 시작된 뒤 흐른 시간 비율 0..1.</summary>
        public float ActionProgress => ActionClip == null ? 1f : Mathf.Clamp01(1f - actionLeft / Mathf.Max(0.01f, ClipLength(ActionClip) - 0.1f));
        public float ActionTime => ActionClip == null ? 0f : (ClipLength(ActionClip) - 0.1f) - actionLeft;

        void Update()
        {
            float dt = Time.deltaTime;
            if (transitionLeft > 0f)
            {
                transitionLeft -= dt;
                if (transitionLeft <= 0f)
                {
                    Current = pendingPosture;
                    Play(BaseClip(Current), 0.2f);
                }
            }
            if (ActionClip != null && !actionLoop)
            {
                actionLeft -= dt;
                if (actionLeft <= 0f) StopAction();
            }
            Step();

            if (Current == Posture.Stand && ActionClip == null && !InTransition) UpdateStand(dt);
        }

        // ------------------------------------------------------------------ 표정과 시선 (동작 위에 더함)

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || Model == null) return;

            PinRoot();
            HeadSteady(dt);

            // 눈: 무작위 깜빡임 + 기분 좋게 감기
            blinkTimer -= dt;
            if (blinkTimer <= 0f && blinkT < 0f) { blinkT = 0f; blinkTimer = UnityEngine.Random.Range(2.2f, 5.5f); }
            float blink = 0f;
            if (blinkT >= 0f)
            {
                blinkT += dt;
                blink = 1f - Mathf.Abs(blinkT / 0.09f - 1f);
                if (blinkT >= 0.18f) blinkT = -1f;
            }
            sEye = Mathf.Lerp(sEye, eyeOpen, 1f - Mathf.Exp(-10f * dt));
            sMouth = Mathf.Lerp(sMouth, mouthOpen, 1f - Mathf.Exp(-18f * dt));
            if (face)
            {
                float closed = Mathf.Clamp01(Mathf.Max(1f - sEye, blink));
                if (blinkIdx >= 0) face.SetBlendShapeWeight(blinkIdx, Mathf.Max(face.GetBlendShapeWeight(blinkIdx), closed * 100f));
                if (mouthIdx >= 0 && sMouth > 0.01f) face.SetBlendShapeWeight(mouthIdx, Mathf.Max(face.GetBlendShapeWeight(mouthIdx), sMouth * 100f));
            }

            // 고개: 바라볼 곳으로 (몸 기준 좌우·상하), 앉거나 서 있을 때만
            Vector2 target = Vector2.zero;
            bool canLook = (Current == Posture.Stand || Current == Posture.Sit) && !InTransition && (ActionClip == null || ActionClip == "Sit");
            if (lookTarget.HasValue && canLook)
            {
                Vector3 to = transform.InverseTransformDirection(lookTarget.Value - Head.position);
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                target = new Vector2(Mathf.Clamp(yaw, -60f, 60f), Mathf.Clamp(pitch, -25f, 25f)) * lookWeight;
            }
            look = Vector2.Lerp(look, target, 1f - Mathf.Exp(-5f * dt));
            sTilt = Mathf.Lerp(sTilt, canLook ? headTilt : 0f, 1f - Mathf.Exp(-5f * dt));
            Vector3 up = transform.up, right = transform.right, fwd = transform.forward;
            Quaternion add = Quaternion.AngleAxis(look.x * 0.6f, up) * Quaternion.AngleAxis(look.y * 0.5f, right);
            neck.rotation = add * neck.rotation;
            Quaternion addHead = Quaternion.AngleAxis(look.x * 0.4f, up) * Quaternion.AngleAxis(look.y * 0.5f, right) * Quaternion.AngleAxis(sTilt, fwd);
            Head.rotation = addHead * Head.rotation;

            // 꼬리 끝 살랑임 더하기
            sWag = Mathf.Lerp(sWag, tailWag, 1f - Mathf.Exp(-3f * dt));
            if (sWag > 0.01f && tail.Length > 2)
            {
                float t = Time.time;
                for (int i = tail.Length / 2; i < tail.Length; i++)
                {
                    float f = (i - tail.Length / 2 + 1) / (float)(tail.Length - tail.Length / 2);
                    tail[i].rotation = Quaternion.AngleAxis(Mathf.Sin(t * 5f - i * 0.6f) * 12f * sWag * f, up) * tail[i].rotation;
                }
            }

            // 쓰다듬기: 골골 떨림(24 Hz, 아주 작게), 귀를 편하게 뒤로
            sPet = Mathf.Lerp(sPet, petLean, 1f - Mathf.Exp(-4f * dt));
            if (sPet > .01f)
            {
                float pur = Mathf.Sin(Time.time * 2f * Mathf.PI * 24f) * .25f * sPet;
                if (chest) chest.rotation = Quaternion.AngleAxis(pur, right) * chest.rotation;
                Head.rotation = Quaternion.AngleAxis(pur, right) * Head.rotation;
                foreach (var e in ears) e.rotation = Quaternion.AngleAxis(-22f * sPet, right) * e.rotation;
            }
            UpdateSprings(dt);
            UpdateSquash(dt);

            BubbleAnchor.position = Head.position + up * (HeadRadius + 0.32f);
        }

        // ---------------- 움직임 층 (motionfeel.js 를 옮김)
        class Spring { public Transform b; public float k, c; public Vector3 w; public Quaternion q; public bool init; }
        readonly List<Spring> springs = new List<Spring>();
        readonly List<Transform> ears = new List<Transform>();
        Transform chest; float sPet; Vector3 modelScale = Vector3.one;

        /// <summary>꼬리·귀가 몸을 한 박자 늦게 따라오고 살짝 넘쳤다 돌아온다 (세계 공간 각 스프링). 부모부터 차례로.</summary>
        void UpdateSprings(float dt)
        {
            dt = Mathf.Min(dt, 1f / 20f);
            foreach (var s in springs)
            {
                var target = s.b.rotation;   // (부모는 이미 스프링이 적용된 회전)
                if (!s.init) { s.q = target; s.w = Vector3.zero; s.init = true; }
                var err = target * Quaternion.Inverse(s.q);
                err.ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) ang -= 360f;
                if (float.IsNaN(ang) || float.IsNaN(axis.x) || Mathf.Abs(ang) > 70f) { s.q = target; s.w = Vector3.zero; continue; }   // (순간이동·큰 전환: 따라잡기)
                s.w += axis * (ang * Mathf.Deg2Rad * s.k * dt); s.w *= Mathf.Max(0f, 1f - s.c * dt);
                float wl = s.w.magnitude;
                if (wl > 1e-6f) s.q = (Quaternion.AngleAxis(wl * dt * Mathf.Rad2Deg, s.w / wl) * s.q).normalized;   // (곱이 쌓이면 길이가 1에서 벗어나 각도가 NaN 이 된다)
                if (float.IsNaN(s.q.x)) { s.q = target; s.w = Vector3.zero; }
                s.b.rotation = s.q;
            }
        }

        /// <summary>점프·착지에만 눌림과 늘어남 (발 기준, 부피 유지). 걷기에는 없다.</summary>
        float sqS, sqV, prevY = float.NaN, prevVy;
        void UpdateSquash(float dt)
        {
            dt = Mathf.Min(dt, 1f / 20f);
            float y = transform.position.y, vy = float.IsNaN(prevY) ? 0f : (y - prevY) / dt;
            if (prevVy < -.8f && vy > -.2f) { sqV -= 1.3f * Mathf.Min(2f, -prevVy / 2.5f); if (prevVy < -1.2f) FxPool.Instance?.Dust(transform.position); }   // 착지: 눌림 + 작은 먼지
            if (prevVy > -.05f && vy > 1f) sqV += .6f;                                      // 뛰어오름: 늘어남
            float target = Mathf.Clamp(.05f * Mathf.Abs(vy), 0f, .1f);
            sqV += (260f * (target - sqS) - 16f * sqV) * dt; sqS += sqV * dt;
            prevY = y; prevVy = vy;
            if (float.IsNaN(sqS) || float.IsNaN(sqV)) { sqS = sqV = 0f; }
            float sy = 1f + Mathf.Clamp(sqS, -.16f, .14f), sxz = 1f / Mathf.Sqrt(sy);
            Model.localScale = new Vector3(modelScale.x * sxz, modelScale.y * sy, modelScale.z * sxz);
        }

        /// <summary>걸을 때 머리는 수평으로 차분히: 머리의 세계 회전을 천천히 따라가는 평균 쪽으로 붙잡는다.</summary>
        Quaternion headAvg; bool headAvgInit;
        void HeadSteady(float dt)
        {
            float w = Mathf.Clamp01(moveSpeed / .3f) * (Current == Posture.Stand && ActionClip == null ? 1f : 0f);
            var qw = Head.rotation;
            if (!headAvgInit) { headAvg = qw; headAvgInit = true; }
            headAvg = Quaternion.Slerp(headAvg, qw, 1f - Mathf.Exp(-7.5f * dt));
            if (w > .01f) Head.rotation = Quaternion.Slerp(qw, headAvg, .75f * w);
        }

        /// <summary>
        /// 동작 클립에 들어 있는 루트 이동(걷기 한 주기에 0.3~0.8 m 전진)을 지운다.
        /// 지우지 않으면 몸이 앞으로 갔다가 주기가 바뀔 때 제자리로 튕겨 돌아온다. 실제 이동은 CatBrain 이 한다.
        /// 높이는 자세 보정(바닥 위로 올리기)에 쓰일 수 있어 남기고, 점프 중에만 함께 지운다 (점프 높이는 코드가 곡선대로).
        /// </summary>
        void PinRoot()
        {
            Vector3 r = transform.InverseTransformPoint(rootBone.position);
            r.x = rootBindLocal.x;
            r.z = rootBindLocal.z;
            if (ActionClip == "JumpUp" || ActionClip == "JumpDown") r.y = rootBindLocal.y;
            rootBone.position = transform.TransformPoint(r);
        }

        public Vector3 RootOffset => transform.InverseTransformPoint(rootBone.position) - rootBindLocal;

        // 예전 API 호환 (통통 튀는 연출은 동작 클립이 대신한다)
        public void Squash(float amount) { }
    }
}
