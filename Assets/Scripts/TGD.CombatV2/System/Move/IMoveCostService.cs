using TGD.CombatV2;
using TGD.HexBoard;

public interface IMoveCostService
{
    bool IsOnCooldown(Unit unit, MoveActionConfig cfg);
    bool HasEnough(Unit unit, MoveActionConfig cfg);
    void Pay(Unit unit, MoveActionConfig cfg);
    // ★ 新增占位：移动过程中“节省≥阈值”时退回 N 秒的成本（能量等）
    void RefundSeconds(Unit unit, MoveActionConfig cfg, int seconds);
}