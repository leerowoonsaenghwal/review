using UnityEngine;

namespace CatIsland
{
    /// <summary>
    /// 배고픔과 기운. 줄기만 하고 벌은 없다: 바닥이어도 아프거나 떠나지 않는다.
    /// </summary>
    public class CatNeeds
    {
        public float Hunger { get; private set; }
        public float Energy { get; private set; }

        public bool IsHungry => Hunger < GameConfig.HungryThreshold;
        public bool IsSleepy => Energy < GameConfig.SleepyThreshold;

        public CatNeeds(float hunger = GameConfig.StartHunger, float energy = GameConfig.StartEnergy)
        {
            Hunger = Mathf.Clamp01(hunger);
            Energy = Mathf.Clamp01(energy);
        }

        public void Tick(float dt, bool eating, bool sleeping)
        {
            if (eating) Hunger += GameConfig.EatGainPerSec * dt;
            else if (!sleeping) Hunger -= GameConfig.HungerDrainPerSec * dt;

            if (sleeping) Energy += GameConfig.SleepGainPerSec * dt;
            else Energy -= GameConfig.EnergyDrainPerSec * dt;

            Hunger = Mathf.Clamp01(Hunger);
            Energy = Mathf.Clamp01(Energy);
        }

        public void SetForTest(float hunger, float energy)
        {
            Hunger = Mathf.Clamp01(hunger);
            Energy = Mathf.Clamp01(energy);
        }
    }
}
