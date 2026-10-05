using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 둥근 도형으로 고양이를 만들고, 뼈대 없이 코드로 자세를 섞는다.
    /// 행동(CatBrain)은 아래 목표값만 바꾸고, 실제 움직임은 여기서 부드럽게 따라간다.
    /// 귀와 꼬리는 한 박자 늦게 따라온다 (기획서 3부 3장).
    /// </summary>
    public class CatRig : MonoBehaviour
    {
        // ---- 목표값 (CatBrain이 씀) ----
        [HideInInspector] public float moveSpeed;          // 현재 이동 속도 (다리 흔들기)
        [HideInInspector] public float eyeOpen = 1f;       // 0 = 감음
        [HideInInspector] public float tailUp = 0.5f;
        [HideInInspector] public float tailWag;            // 꼬리 끝 살랑임 세기
        [HideInInspector] public float tailCurl;           // 몸에 감기 (식빵, 잠)
        [HideInInspector] public float loaf;               // 식빵 자세
        [HideInInspector] public float bellyUp;            // 발라당
        [HideInInspector] public float bellyRollSign = 1f;
        [HideInInspector] public float knead;              // 꾹꾹이
        [HideInInspector] public float eat;                // 고개 숙이고 먹기
        [HideInInspector] public float pawsUp;             // 앞발로 손 잡기 (깨물기)
        [HideInInspector] public float earsBack;
        [HideInInspector] public float headTilt;           // 고개 갸웃 (도)
        [HideInInspector] public float mouthOpen;
        [HideInInspector] public float lean;               // 손 쪽으로 몸 기울이기
        [HideInInspector] public Vector3 leanDir;
        [HideInInspector] public float sleepBreath;        // 잠잘 때 깊은 숨
        public Vector3? lookTarget;                        // 바라볼 곳 (월드)

        // ---- 부품 ----
        public Transform Pose { get; private set; }
        public Transform Body { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Head { get; private set; }
        public Transform HeadZone { get; private set; }
        public Transform BodyZone { get; private set; }
        public Transform BubbleAnchor { get; private set; }
        Transform[] legs = new Transform[4];   // FL, FR, BL, BR
        Transform[] legMeshes = new Transform[4];
        Transform[] eyes = new Transform[2];
        Transform[] eyeOpenParts = new Transform[2];
        Transform[] eyeClosedParts = new Transform[2];
        Transform[] ears = new Transform[2];
        Transform[] tail;
        Transform mouth;

        // ---- 내부 상태 (부드럽게 따라가는 값) ----
        float sEye = 1f, sTailUp = 0.5f, sTailWag, sTailCurl, sLoaf, sBelly, sKnead, sEat, sPaws, sEars, sTilt, sMouth, sLean;
        float walkPhase, blinkTimer = 2f, blinkT = -1f, breathT, squash, squashVel;
        float[] tailLag;
        Vector2 headYawPitch;
        Vector3 bodyBase;

        public const float PoseHeight = 0.22f;
        const float ClosedStrokeAngle = -58f;

        void Awake() { Build(); }

        public void Build()
        {
            if (Pose != null) return;
            var sphere = MeshFactory.Sphere();
            var cone = MeshFactory.RoundCone();
            var capsule = MeshFactory.Capsule(0.32f);

            Pose = Shapes.Pivot(transform, "Pose", new Vector3(0f, PoseHeight, 0f));

            bodyBase = new Vector3(0f, 0.05f, 0f);
            Body = Shapes.Make(Pose, "Body", sphere, Palette.CatFur, bodyBase, new Vector3(0.42f, 0.36f, 0.56f));
            Shapes.Make(Body, "Chest", sphere, Palette.CatWhite, new Vector3(0f, -0.12f, 0.3f), new Vector3(0.66f, 0.62f, 0.42f));
            Shapes.Make(Body, "BellyWhite", sphere, Palette.CatWhite, new Vector3(0f, -0.2f, 0.02f), new Vector3(0.72f, 0.62f, 0.8f));
            // 등 줄무늬: 몸보다 살짝 큰 얇은 고리를 위로 올려 등과 옆구리에만 보이게
            float[] stripeZ = { 0.12f, -0.07f, -0.25f };
            for (int i = 0; i < stripeZ.Length; i++)
            {
                float z = stripeZ[i];
                float cs = Mathf.Sqrt(0.25f - z * z) * 2f;
                Shapes.Make(Body, "Stripe" + i, sphere, Palette.CatStripe,
                    new Vector3(0f, 0.035f, z), new Vector3(cs * 1.03f, cs, 0.09f));
            }

            // 다리: 엉덩이 높이에서 아래로 매달림. 길이 0.17
            Vector3[] hips =
            {
                new Vector3(-0.105f, -0.05f, 0.15f), new Vector3(0.105f, -0.05f, 0.15f),
                new Vector3(-0.115f, -0.05f, -0.15f), new Vector3(0.115f, -0.05f, -0.15f)
            };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = Shapes.Pivot(Pose, "Leg" + i, hips[i]);
                legMeshes[i] = Shapes.Make(legs[i], "LegMesh", capsule, Palette.CatFur, new Vector3(0f, -0.085f, 0f), new Vector3(0.11f, 0.19f, 0.11f));
                Shapes.Make(legs[i], "Paw", sphere, Palette.CatWhite, new Vector3(0f, -0.155f, 0.012f), new Vector3(0.12f, 0.08f, 0.135f));
            }

            // 머리: 몸 앞쪽 위. 크게
            Neck = Shapes.Pivot(Pose, "Neck", new Vector3(0f, 0.17f, 0.2f));
            Head = Shapes.Pivot(Neck, "Head", new Vector3(0f, 0.11f, 0.07f));
            Shapes.Make(Head, "Skull", sphere, Palette.CatFur, Vector3.zero, new Vector3(0.48f, 0.41f, 0.41f));
            for (int i = 0; i < 3; i++)
                Shapes.Make(Head, "MStripe" + i, sphere, Palette.CatStripe,
                    new Vector3((i - 1) * 0.052f, 0.145f, 0.135f), new Vector3(0.028f, 0.075f, 0.04f), new Vector3(-48f, 0f, (i - 1) * -12f));
            Shapes.Make(Head, "CheekL", sphere, Palette.CatWhite, new Vector3(-0.055f, -0.075f, 0.16f), new Vector3(0.13f, 0.1f, 0.1f));
            Shapes.Make(Head, "CheekR", sphere, Palette.CatWhite, new Vector3(0.055f, -0.075f, 0.16f), new Vector3(0.13f, 0.1f, 0.1f));
            Shapes.Make(Head, "Chin", sphere, Palette.CatWhite, new Vector3(0f, -0.12f, 0.13f), new Vector3(0.12f, 0.08f, 0.1f));
            Shapes.Make(Head, "Nose", sphere, Palette.CatNose, new Vector3(0f, -0.035f, 0.2f), new Vector3(0.05f, 0.033f, 0.03f));
            mouth = Shapes.Make(Head, "Mouth", sphere, Palette.Chestnut, new Vector3(0f, -0.105f, 0.2f), new Vector3(0.05f, 0.001f, 0.03f), default, false);

            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                eyes[i] = Shapes.Pivot(Head, i == 0 ? "EyeL" : "EyeR", new Vector3(sx * 0.098f, 0.018f, 0.174f), new Vector3(0f, sx * 18f, 0f));
                eyeOpenParts[i] = Shapes.Pivot(eyes[i], "Open", Vector3.zero);
                Shapes.Make(eyeOpenParts[i], "Iris", sphere, Palette.CatEye, Vector3.zero, new Vector3(0.078f, 0.092f, 0.045f), default, false);
                Shapes.Make(eyeOpenParts[i], "Shine", sphere, Palette.CatEyeShine, new Vector3(sx * -0.012f, 0.022f, 0.018f), new Vector3(0.026f, 0.026f, 0.012f), default, false);
                // 감은 눈: 두 획으로 만든 ^ 모양 (기분 좋게 감은 눈)
                eyeClosedParts[i] = Shapes.Pivot(eyes[i], "Closed", new Vector3(0f, -0.008f, 0.012f));
                var stroke = MeshFactory.Capsule(0.45f);
                Shapes.Make(eyeClosedParts[i], "StrokeA", stroke, Palette.CatEye, new Vector3(-0.017f, 0f, 0f), new Vector3(0.017f, 0.052f, 0.014f), new Vector3(0f, 0f, ClosedStrokeAngle), false);
                Shapes.Make(eyeClosedParts[i], "StrokeB", stroke, Palette.CatEye, new Vector3(0.017f, 0f, 0f), new Vector3(0.017f, 0.052f, 0.014f), new Vector3(0f, 0f, -ClosedStrokeAngle), false);
                eyeClosedParts[i].gameObject.SetActive(false);

                ears[i] = Shapes.Pivot(Head, i == 0 ? "EarL" : "EarR", new Vector3(sx * 0.115f, 0.13f, -0.01f), new Vector3(-8f, 0f, sx * -22f));
                Shapes.Make(ears[i], "Outer", cone, Palette.CatFur, Vector3.zero, new Vector3(0.16f, 0.15f, 0.085f));
                Shapes.Make(ears[i], "Inner", cone, Palette.CatInnerEar, new Vector3(0f, 0.012f, 0.022f), new Vector3(0.1f, 0.11f, 0.04f), default, false);
            }

            // 꼬리: 구슬을 이은 사슬
            int segs = 8;
            tail = new Transform[segs];
            tailLag = new float[segs];
            Transform parent = Shapes.Pivot(Pose, "Tail", new Vector3(0f, 0.08f, -0.25f));
            for (int i = 0; i < segs; i++)
            {
                var seg = Shapes.Pivot(parent, "T" + i, i == 0 ? Vector3.zero : new Vector3(0f, 0.058f, 0f));
                float s = Mathf.Lerp(0.085f, 0.068f, i / (float)(segs - 1));
                // 다음 마디까지 이어지는 캡슐이라 구슬처럼 끊겨 보이지 않는다
                Shapes.Make(seg, "Bead", MeshFactory.Capsule(0.45f), i == segs - 1 ? Palette.CatStripe : Palette.CatFur,
                    new Vector3(0f, 0.03f, 0f), new Vector3(s, 0.058f + s, s));
                tail[i] = seg;
                parent = seg;
            }

            // 쓰다듬기 판정용 충돌체. 자세(발라당)와 함께 돌아간다
            HeadZone = Shapes.Pivot(Head, "HeadZone", Vector3.zero);
            var hc = HeadZone.gameObject.AddComponent<SphereCollider>();
            hc.radius = 0.235f;
            BodyZone = Shapes.Pivot(Pose, "BodyZone", bodyBase);
            var bc = BodyZone.gameObject.AddComponent<CapsuleCollider>();
            bc.direction = 2;
            bc.radius = 0.2f;
            bc.height = 0.62f;

            BubbleAnchor = Shapes.Pivot(transform, "BubbleAnchor", new Vector3(0f, 0.95f, 0f));
        }

        /// <summary>충돌 지점이 어느 부위인지. 고양이 자세와 무관하게 몸 기준으로 판정.</summary>
        public PetZone ClassifyHit(Collider c, Vector3 worldPoint)
        {
            if (c.transform == HeadZone)
            {
                Vector3 d = HeadZone.InverseTransformPoint(worldPoint).normalized;
                if (d.y < -0.4f) return PetZone.Chin;
                if (d.y > 0.3f) return PetZone.Forehead;
                if (Mathf.Abs(d.x) > 0.55f) return PetZone.Cheek;
                return d.y < -0.1f ? PetZone.Chin : PetZone.Forehead;
            }
            if (c.transform == BodyZone)
            {
                Vector3 p = BodyZone.InverseTransformPoint(worldPoint);
                Vector3 d = new Vector3(p.x / 0.2f, p.y / 0.2f, p.z / 0.31f).normalized;
                if (d.y < -0.35f) return PetZone.Belly;
                if (p.z < -0.13f) return PetZone.Butt;
                return PetZone.Back;
            }
            return PetZone.None;
        }

        public bool IsCatCollider(Collider c) => c != null && (c.transform == HeadZone || c.transform == BodyZone);

        public void Squash(float amount) { squashVel -= amount * 6f; }

        void LateUpdate() { Animate(Time.deltaTime); }

        static float Approach(float cur, float target, float speed, float dt) => Mathf.Lerp(cur, target, 1f - Mathf.Exp(-speed * dt));

        public void Animate(float dt)
        {
            if (Pose == null || dt <= 0f) return;

            sEye = Approach(sEye, eyeOpen, 10f, dt);
            sTailUp = Approach(sTailUp, tailUp, 4f, dt);
            sTailWag = Approach(sTailWag, tailWag, 3f, dt);
            sTailCurl = Approach(sTailCurl, tailCurl, 3f, dt);
            sLoaf = Approach(sLoaf, loaf, 4f, dt);
            sBelly = Approach(sBelly, bellyUp, 4.5f, dt);
            sKnead = Approach(sKnead, knead, 5f, dt);
            sEat = Approach(sEat, eat, 6f, dt);
            sPaws = Approach(sPaws, pawsUp, 14f, dt);
            sEars = Approach(sEars, earsBack, 8f, dt);
            sTilt = Approach(sTilt, headTilt, 5f, dt);
            sMouth = Approach(sMouth, mouthOpen, 18f, dt);
            sLean = Approach(sLean, lean, 5f, dt);

            // 통통 튀는 눌림 (스프링)
            float k = 180f, damp = 14f;
            squashVel += (-k * squash - damp * squashVel) * dt;
            squash += squashVel * dt;

            // 숨쉬기
            breathT += dt * Mathf.Lerp(1.6f, 0.8f, sleepBreath);
            float breath = Mathf.Sin(breathT * Mathf.PI * 2f) * Mathf.Lerp(0.012f, 0.03f, sleepBreath);

            // 걷기
            float speedNorm = Mathf.Clamp01(moveSpeed / GameConfig.TrotSpeed);
            walkPhase += dt * Mathf.Lerp(0f, 13f, Mathf.Sqrt(speedNorm));
            float stride = Mathf.Lerp(0f, 32f, Mathf.Clamp01(speedNorm * 2.5f)) * (1f - sLoaf);
            float bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.025f * Mathf.Clamp01(speedNorm * 3f);

            // 자세: 식빵이면 낮게, 발라당이면 옆으로 구름
            float poseY = PoseHeight - sLoaf * 0.1f - sBelly * 0.03f + bob;
            Vector3 leanOffset = leanDir * (sLean * 0.035f);
            Pose.localPosition = new Vector3(leanOffset.x, poseY, leanOffset.z);
            float roll = sBelly * 105f * bellyRollSign;
            Pose.localRotation = Quaternion.Euler(-sEat * 6f + sPaws * -14f, 0f, roll);

            float sq = Mathf.Clamp(squash, -0.25f, 0.25f);
            Body.localScale = new Vector3(0.42f * (1f - sq * 0.5f + breath * 0.5f), 0.36f * (1f + sq + breath), 0.56f * (1f + sLoaf * 0.04f));
            Body.localPosition = bodyBase + new Vector3(0f, sq * 0.05f, 0f);

            // 다리
            float t = Time.time;
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float phase = walkPhase + ((i == 0 || i == 3) ? 0f : Mathf.PI);
                float swing = Mathf.Sin(phase) * stride;
                float kneadSwing = front ? Mathf.Max(0f, Mathf.Sin(t * 7f + (i == 0 ? 0f : Mathf.PI))) * -35f * sKnead : 0f;
                float paws = front ? -95f * sPaws : 0f;
                float bellyBend = (front ? -55f : 35f) * sBelly;
                legs[i].localRotation = Quaternion.Euler(swing + kneadSwing + paws + bellyBend, 0f, 0f);
                float tuck = Mathf.Lerp(1f, 0.3f, sLoaf * (1f - sBelly));
                legMeshes[i].localScale = new Vector3(0.11f, 0.19f * tuck, 0.11f);
                legMeshes[i].localPosition = new Vector3(0f, -0.085f * tuck, 0f);
                legs[i].GetChild(1).localPosition = new Vector3(0f, -0.155f * tuck, 0.012f);
            }

            // 머리: 바라보기 + 먹기 + 갸웃
            Vector2 yp = Vector2.zero;
            if (lookTarget.HasValue)
            {
                Vector3 local = Pose.InverseTransformPoint(lookTarget.Value) - Neck.localPosition;
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(local.y - 0.1f, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                yp = new Vector2(Mathf.Clamp(yaw, -70f, 70f), Mathf.Clamp(pitch, -30f, 35f));
            }
            yp *= (1f - sEat) * (1f - sLoaf * 0.4f);
            headYawPitch = Vector2.Lerp(headYawPitch, yp, 1f - Mathf.Exp(-6f * dt));
            float eatBob = sEat * (40f + Mathf.Max(0f, Mathf.Sin(t * 9f)) * 12f);
            float sleepDrop = sLoaf * (1f - sEye) * 18f;
            float counterRoll = -roll * 0.55f;
            Neck.localRotation = Quaternion.Euler(headYawPitch.y + eatBob + sleepDrop - sPaws * 10f, headYawPitch.x, sTilt + counterRoll);
            Neck.localPosition = new Vector3(0f, 0.17f - sEat * 0.06f - sLoaf * 0.03f, 0.2f + sEat * 0.05f);

            // 눈 깜빡임
            blinkTimer -= dt;
            if (blinkTimer <= 0f && blinkT < 0f) { blinkT = 0f; blinkTimer = Random.Range(2.2f, 5.5f); }
            float blink = 1f;
            if (blinkT >= 0f)
            {
                blinkT += dt;
                blink = Mathf.Abs(blinkT / 0.12f - 1f);
                if (blinkT >= 0.24f) blinkT = -1f;
            }
            float open = Mathf.Clamp01(sEye) * Mathf.Clamp01(blink);
            bool closed = open < 0.3f;
            for (int i = 0; i < 2; i++)
            {
                eyeOpenParts[i].gameObject.SetActive(!closed);
                eyeClosedParts[i].gameObject.SetActive(closed);
                eyeOpenParts[i].localScale = new Vector3(1f, Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(0.3f, 1f, open)), 1f);
            }

            // 귀: 한 박자 늦게 + 가끔 쫑긋
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                float twitch = Mathf.Max(0f, Mathf.Sin(t * 0.9f + i * 2.1f) - 0.96f) * 300f;
                ears[i].localRotation = Quaternion.Euler(-8f - sEars * 45f + twitch * 0.3f, sx * sEars * 30f, sx * (-22f - sEars * 25f));
            }

            // 꼬리: 각 마디가 앞 마디를 늦게 따라감
            float rootX = Mathf.Lerp(-115f, -12f, sTailUp);
            rootX = Mathf.Lerp(rootX, -92f, sTailCurl);
            tail[0].localRotation = Quaternion.Euler(rootX, Mathf.Sin(t * 1.3f) * 8f * (1f - sTailCurl), 0f);
            for (int i = 1; i < tail.Length; i++)
            {
                float f = i / (float)(tail.Length - 1);
                float wag = Mathf.Sin(t * 3.2f - i * 0.55f) * (6f + sTailWag * 16f) * f;
                float hook = sTailUp * f * f * 28f;
                float target = hook;
                tailLag[i] = Mathf.Lerp(tailLag[i], wag, 1f - Mathf.Exp(-(12f - i) * dt));
                float curlSide = sTailCurl * 22f;
                tail[i].localRotation = Quaternion.Euler(target + sTailCurl * 6f, 0f, tailLag[i] + curlSide);
            }

            mouth.localScale = new Vector3(0.05f, Mathf.Lerp(0.001f, 0.045f, sMouth), 0.03f);
        }
    }
}
