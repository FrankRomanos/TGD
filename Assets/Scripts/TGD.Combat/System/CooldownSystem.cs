using System.Collections.Generic;
using TGD.Combat;
namespace TGD.Combat
{
    public interface ICooldownSystem
    {
        void Execute(ModifyCooldownOp op, RuntimeCtx ctx);
        void TickEndOfTurn(); // 任一单位 TurnEnd 时由战斗循环调用
    }

    public sealed class CooldownSystem : ICooldownSystem
    {
        readonly IEnumerable<Unit> _allUnits;
        public CooldownSystem(IEnumerable<Unit> allUnits) { _allUnits = allUnits; }

        public void Execute(ModifyCooldownOp op, RuntimeCtx ctx) { /* 按秒增减 */ }

        public void TickEndOfTurn()
        {
            foreach (var u in _allUnits) u.TickCooldownSeconds(CombatClock.BaseTurnSeconds); // -6
        }
    }
}
