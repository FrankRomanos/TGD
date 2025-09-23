using System.Collections.Generic;
using TGD.Combat;
namespace TGD.Combat
{
    public interface ICooldownSystem
    {
        void Execute(ModifyCooldownOp op, RuntimeCtx ctx);
        void TickEndOfTurn(); // ��һ��λ TurnEnd ʱ��ս��ѭ������
    }

    public sealed class CooldownSystem : ICooldownSystem
    {
        readonly IEnumerable<Unit> _allUnits;
        public CooldownSystem(IEnumerable<Unit> allUnits) { _allUnits = allUnits; }

        public void Execute(ModifyCooldownOp op, RuntimeCtx ctx) { /* �������� */ }

        public void TickEndOfTurn()
        {
            foreach (var u in _allUnits) u.TickCooldownSeconds(CombatClock.BaseTurnSeconds); // -6
        }
    }
}
