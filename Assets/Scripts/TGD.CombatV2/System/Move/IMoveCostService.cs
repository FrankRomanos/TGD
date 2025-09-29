using TGD.CombatV2;
using TGD.HexBoard;

public interface IMoveCostService
{
    bool IsOnCooldown(Unit unit, MoveActionConfig cfg);
    bool HasEnough(Unit unit, MoveActionConfig cfg);
    void Pay(Unit unit, MoveActionConfig cfg);
    // �� ����ռλ���ƶ������С���ʡ����ֵ��ʱ�˻� N ��ĳɱ��������ȣ�
    void RefundSeconds(Unit unit, MoveActionConfig cfg, int seconds);
}