
namespace TGD.CombatV2
{
    public interface IAttackCostService
    {
        bool IsOnCooldown(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg);
        bool HasEnough(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg);
        void Pay(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg);
        void ResetForNewTurn(); // �Ժ� TurnManagerV2 ����
    }
}
