using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CatIsland.Tests
{
    /// <summary>
    /// 움직임 매끄러움 진단: 고양이가 스스로 돌아다니는 동안 매 프레임을 기록해
    /// 멈췄다 가기, 방향 떨림, 동작 전환 빈도, 발 미끄러짐(이동 속도 vs 동작 속도)을 잰다.
    /// </summary>
    [Category("Diagnostics")]
    public class MotionDiagnostics : SceneFixture
    {
        struct F { public float t, dt, speed, yaw, x, z, norm; public string clip; public float animSpeed; }

        [UnityTest]
        public IEnumerator Record_RootDrift()
        {
            // 걷는 동안 Root·Hips 뼈가 고양이 기준으로 앞으로 밀려 나갔다가 되돌아오는지 잰다
            Cat.Needs.SetForTest(1f, 1f);
            var bones = Cat.Rig.Model.GetComponentsInChildren<Transform>();
            var root = System.Array.Find(bones, b => b.name == "Root");
            var hips = System.Array.Find(bones, b => b.name == "Hips");
            Cat.OnTapGround(new Vector3(0f, 0f, 3.5f));
            var sb = new StringBuilder("[RootDrift]\n");
            float t = 0f, minZ = 9f, maxZ = -9f;
            while (t < 6f)
            {
                yield return null;
                t += Time.deltaTime;
                Vector3 r = Cat.transform.InverseTransformPoint(root.position);
                Vector3 h = Cat.transform.InverseTransformPoint(hips.position);
                if (Cat.Rig.Playing == "Move") { minZ = Mathf.Min(minZ, h.z); maxZ = Mathf.Max(maxZ, h.z); }
                if (Mathf.RoundToInt(t * 60) % 6 == 0)
                    sb.AppendLine($"  t={t:F2} clip={Cat.Rig.Playing} spd={Cat.Speed:F2} rootLocal={r:F3} hipsLocal={h:F3} norm={Cat.Rig.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime:F2}");
            }
            sb.AppendLine($"hips forward range while moving: {minZ:F3} .. {maxZ:F3} (in-place should be a few cm)");
            Debug.Log(sb.ToString());
            Assert.Pass();
        }

        [UnityTest]
        public IEnumerator Record_FreeRoam()
        {
            Cat.Needs.SetForTest(1f, 1f);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            var frames = new List<F>();
            float t = 0f;
            while (t < 40f)
            {
                Cat.NoteUserActivity(); // 다가오기(Invite)는 빼고 자유롭게
                yield return null;
                t += Time.deltaTime;
                var tr = Cat.transform;
                frames.Add(new F
                {
                    t = t, dt = Time.deltaTime, speed = Cat.Speed, yaw = tr.eulerAngles.y, x = tr.position.x, z = tr.position.z,
                    clip = Cat.Rig.Playing, norm = Cat.Rig.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime, animSpeed = Cat.Rig.Anim.GetFloat("Speed") * Cat.Rig.Anim.GetFloat("MoveRate") * (Cat.Rig.Anim.GetFloat("Speed") <= 0.4f ? 1f : 1f),
                });
            }

            var sb = new StringBuilder("[MotionDiag]\n");
            int stopGo = 0; bool moving = false;
            int clipSwitches = 0; string last = null;
            float maxYawRate = 0f; int yawSpikes = 0;
            float slideSum = 0f; int slideN = 0;
            var states = new Dictionary<string, int>();
            for (int i = 1; i < frames.Count; i++)
            {
                var a = frames[i - 1]; var b = frames[i];
                bool m = b.speed > 0.05f;
                if (m && !moving) stopGo++;
                moving = m;
                if (b.clip != last) { clipSwitches++; last = b.clip; }
                states[b.clip] = states.TryGetValue(b.clip, out var c) ? c + 1 : 1;
                float yawRate = Mathf.Abs(Mathf.DeltaAngle(a.yaw, b.yaw)) / Mathf.Max(1e-4f, b.dt);
                maxYawRate = Mathf.Max(maxYawRate, yawRate);
                if (yawRate > 200f) yawSpikes++;
                float realSpeed = new Vector2(b.x - a.x, b.z - a.z).magnitude / Mathf.Max(1e-4f, b.dt);
                if (b.clip == "Move" && realSpeed > 0.05f) { slideSum += Mathf.Abs(realSpeed - b.animSpeed) / realSpeed; slideN++; }
            }
            // 걸음 주기: 이동 중 한 주기에 걸리는 시간 (속도가 같으면 일정해야 한다)
            var cycles = new List<(float spd, float sec)>();
            for (int i = 1; i < frames.Count; i++)
            {
                var a = frames[i - 1]; var b = frames[i];
                if (b.clip != "Move" || a.clip != "Move" || b.speed < 0.3f || b.speed > 0.45f) continue;
                float dn = b.norm - a.norm;
                if (dn > 1e-5f) cycles.Add((b.speed, b.dt / dn));
            }
            if (cycles.Count > 0)
                sb.AppendLine($"walkCycle at 0.30-0.45 m/s: min={cycles.Min(c => c.sec):F2}s max={cycles.Max(c => c.sec):F2}s (Walk clip 1.50 s; MoveRate scales it below 0.4 m/s) n={cycles.Count}");
            // 방향 떨림: 이동 중 방향 변화량의 부호가 자주 바뀌는가
            int jitter = 0;
            for (int i = 2; i < frames.Count; i++)
            {
                if (frames[i].speed < 0.1f) continue;
                float d1 = Mathf.DeltaAngle(frames[i - 2].yaw, frames[i - 1].yaw), d2 = Mathf.DeltaAngle(frames[i - 1].yaw, frames[i].yaw);
                if (Mathf.Abs(d1) > 0.3f && Mathf.Abs(d2) > 0.3f && Mathf.Sign(d1) != Mathf.Sign(d2)) jitter++;
            }
            float dur = frames.Last().t;
            sb.AppendLine($"duration={dur:F1}s frames={frames.Count} meanDt={frames.Average(f => f.dt) * 1000:F1}ms");
            sb.AppendLine($"stopGo={stopGo} ({stopGo / dur * 60f:F1}/min) clipSwitches={clipSwitches} maxYawRate={maxYawRate:F0}deg/s yawSpikes>200={yawSpikes} headingJitter={jitter}");
            sb.AppendLine($"footSlide(mean |real-anim|/real)={(slideN > 0 ? slideSum / slideN : 0):P0} over {slideN} moving frames");
            sb.AppendLine("clips: " + string.Join(", ", states.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));
            // 이동 구간 하나를 자세히
            int start = frames.FindIndex(f => f.speed > 0.05f);
            for (int i = Mathf.Max(0, start - 5); i < Mathf.Min(frames.Count, start + 60); i += 3)
            {
                var f = frames[i];
                sb.AppendLine($"  t={f.t:F2} spd={f.speed:F2} anim={f.animSpeed:F2} yaw={f.yaw:F1} clip={f.clip}");
            }
            Debug.Log(sb.ToString());
            Assert.Pass();
        }
    }
}
