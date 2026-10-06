using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 용품 반응 (ART_DIRECTION 11장 게임 반응): 건드리면 말랑하게 눌렸다 튀어 오르고 살짝 흔들린다 (스프링). 장난감은 고양이가
    /// 치면 조금 굴러갔다가 제자리로 돌아온다 (놓인 자리·길찾기는 그대로: 보이는 모델만 움직인다).
    /// </summary>
    public class ItemJiggle : MonoBehaviour
    {
        Vector3 baseScale, basePos; Quaternion baseRot;
        float sq, sqVel, tilt, tiltVel; Vector3 tiltAxis = Vector3.right;
        Vector3 slide; float rollT = -1f, rollDur; Vector3 rollDir; float rollDist, spin;

        static Transform ModelOf(Transform item) => item ? (item.Find("Model") ?? item) : null;

        /// <summary>눌렀다 튀기 (strength 0~1: 손가락 톡 .5, 고양이가 올라탐 .8).</summary>
        public static void Poke(Transform item, float strength = .5f)
        {
            var m = ModelOf(item); if (!m) return;
            var j = m.GetComponent<ItemJiggle>() ?? m.gameObject.AddComponent<ItemJiggle>();
            j.enabled = true; j.sqVel -= 2.4f * strength; j.tiltVel += 30f * strength * (Random.value < .5f ? 1 : -1); j.tiltAxis = Random.value < .5f ? Vector3.right : Vector3.forward;
        }

        /// <summary>굴리기: dir 쪽으로 dist m 굴러갔다가 천천히 돌아온다 (공·쥐돌이).</summary>
        public static void Kick(Transform item, Vector3 dir, float dist = .22f)
        {
            var m = ModelOf(item); if (!m) return;
            var j = m.GetComponent<ItemJiggle>() ?? m.gameObject.AddComponent<ItemJiggle>();
            dir.y = 0; if (dir.sqrMagnitude < 1e-4f) return;
            j.enabled = true; j.rollDir = dir.normalized; j.rollDist = dist; j.rollT = 0f; j.rollDur = 2.2f;
            j.sqVel -= 1f;
        }

        void Awake() { baseScale = transform.localScale; basePos = transform.localPosition; baseRot = transform.localRotation; }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            // 스프링 (단단함 180, 감쇠 12: 두세 번 출렁이고 멎는다)
            sqVel += (-180f * sq - 12f * sqVel) * dt; sq += sqVel * dt;
            tiltVel += (-160f * tilt - 9f * tiltVel) * dt; tilt += tiltVel * dt;
            var off = Vector3.zero; var roll = Quaternion.identity;
            if (rollT >= 0f)
            {
                rollT += dt; float u = rollT / rollDur;
                // 빠르게 굴러가 멈추고 (0~.35), 쉬었다가 (~.55), 천천히 제자리로 (~1)
                float d = u < .35f ? 1f - Mathf.Pow(1f - u / .35f, 3f) : u < .55f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (u - .55f) / .45f);
                var p = transform.parent; var dirLocal = p ? p.InverseTransformDirection(rollDir) : rollDir;
                off = dirLocal * rollDist * d;
                spin = d * rollDist / .05f * Mathf.Rad2Deg;   // (반지름 5 cm 쯤 굴러간 만큼 돈다)
                roll = Quaternion.AngleAxis(spin, Vector3.Cross(Vector3.up, dirLocal));
                if (u >= 1f) rollT = -1f;
            }
            transform.localScale = new Vector3(baseScale.x * (1f - sq * .5f), baseScale.y * (1f + sq), baseScale.z * (1f - sq * .5f));
            transform.localPosition = basePos + off;
            transform.localRotation = roll * Quaternion.AngleAxis(tilt, tiltAxis) * baseRot;
            if (rollT < 0f && Mathf.Abs(sq) < 1e-4f && Mathf.Abs(sqVel) < 1e-3f && Mathf.Abs(tilt) < .01f && Mathf.Abs(tiltVel) < .05f)
            { transform.localScale = baseScale; transform.localPosition = basePos; transform.localRotation = baseRot; enabled = false; }
        }
    }
}
