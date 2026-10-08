using System.Runtime.InteropServices;
using UnityEngine;

namespace CatIsland
{
    public enum ImpactStyle { Soft = 0, Light = 1, Medium = 2 }

    /// <summary>
    /// 진동. 아이폰에서는 Core Haptics로 골골송에 맞춘 연속 진동을 낸다 (Plugins/iOS/CatHaptics.mm).
    /// 다른 환경에서는 아무것도 하지 않고 호출 횟수만 센다 (테스트용).
    /// </summary>
    public static class Haptics
    {
        public static int ImpactCount { get; private set; }
        /// <summary>설정의 '진동' (끄면 모든 진동이 멈춘다).</summary>
        public static bool Enabled = true;
        public static float PurrIntensity { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CatHaptics_Prepare();
        [DllImport("__Internal")] static extern void CatHaptics_Impact(int style, float intensity);
        [DllImport("__Internal")] static extern void CatHaptics_SetPurr(float intensity);
#else
        static void CatHaptics_Prepare() { }
        static void CatHaptics_Impact(int style, float intensity) { }
        static void CatHaptics_SetPurr(float intensity) { }
#endif

        public static void Prepare() => CatHaptics_Prepare();

        public static void Impact(ImpactStyle style, float intensity = 1f)
        {
            ImpactCount++;
            if (!Enabled) return;
            CatHaptics_Impact((int)style, Mathf.Clamp01(intensity));
        }

        /// <summary>0이면 멈춤. 매 프레임 불러도 된다 (변화가 작으면 무시).</summary>
        public static void SetPurr(float intensity)
        {
            intensity = Enabled ? Mathf.Clamp01(intensity) : 0f;
            if (Mathf.Abs(intensity - PurrIntensity) < 0.03f && !(intensity == 0f && PurrIntensity > 0f)) return;
            PurrIntensity = intensity;
            CatHaptics_SetPurr(intensity);
        }

        public static void ResetCounters() { ImpactCount = 0; PurrIntensity = 0f; }
    }
}
