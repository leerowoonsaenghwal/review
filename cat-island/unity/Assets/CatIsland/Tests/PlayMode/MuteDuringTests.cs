namespace CatIsland.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>테스트 중에는 컴퓨터 스피커로 소리를 내지 않는다 (소리 재생 여부는 AudioSource·횟수로 따로 검사한다).</summary>
    [SetUpFixture]
    public class MuteDuringTests
    {
        [OneTimeSetUp] public void Mute() => AudioListener.volume = 0f;
        [OneTimeTearDown] public void Unmute() => AudioListener.volume = 1f;
    }
}
