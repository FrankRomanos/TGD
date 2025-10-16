using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public struct AttackPlanV2
    {
        public int moveSecs;
        public int attackSecs;
        public int energyMove;
        public int energyAtk;
        public bool isEnemyTarget;
        public bool isFreeMoveCandidate;
    }

    public interface IAttackPlannerV2
    {
        bool TryGetPlan(Unit self, Hex target, out AttackPlanV2 plan, out string reasonIfFail);
    }
}
