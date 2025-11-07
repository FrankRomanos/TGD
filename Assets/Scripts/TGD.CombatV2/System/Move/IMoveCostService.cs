using TGD.CoreV2;

namespace TGD.CombatV2
{
    public struct MoveCostSpec
    {
        public string skillId;
        public int energyPerSecond;
        public float cooldownSeconds;
    }

    public interface IMoveCostService
    {
        bool IsOnCooldown(Unit unit, in MoveCostSpec spec);
        bool HasEnough(Unit unit, in MoveCostSpec spec);
        void Pay(Unit unit, in MoveCostSpec spec);
        void RefundSeconds(Unit unit, in MoveCostSpec spec, int seconds);
    }
}
