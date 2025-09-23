using System.Collections.Generic;
using System.Linq;

namespace TGD.Combat
{
    public sealed class CooldownSystem : ICooldownSystem
    {
        readonly List<Unit> _allUnits;
        readonly IStatusSystem _statusSystem;
        readonly ICombatLogger _logger;

        public CooldownSystem(IEnumerable<Unit> allUnits, IStatusSystem statusSystem, ICombatLogger logger)
        {
            _allUnits = allUnits?.Where(u => u != null).Distinct().ToList() ?? new List<Unit>();
            _statusSystem = statusSystem;
            _logger = logger;
        }

        public void Execute(ModifyCooldownOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            var targets = ResolveTargets(op.Scope, ctx);
            foreach (var unit in targets)
                unit?.ModifyCooldown(op.SkillId, op.DeltaSeconds);
        }

        public void TickEndOfTurn()
        {
            foreach (var unit in _allUnits)
            {
                if (unit == null)
                    continue;

                unit.TickCooldownSeconds(CombatClock.BaseTurnSeconds);
                _statusSystem?.Tick(unit, CombatClock.BaseTurnSeconds);
            }

            _logger?.Log("COOLDOWN_TICK");
        }

        IEnumerable<Unit> ResolveTargets(CooldownTargetScope scope, RuntimeCtx ctx)
        {
            switch (scope)
            {
                case CooldownTargetScope.Self:
                    if (ctx?.Caster != null)
                        yield return ctx.Caster;
                    break;
                case CooldownTargetScope.All:
                    foreach (var unit in _allUnits)
                        yield return unit;
                    break;
                case CooldownTargetScope.ExceptRed:
                    if (ctx?.Caster != null)
                    {
                        foreach (var unit in _allUnits)
                        {
                            if (unit == null)
                                continue;
                            if (unit.IsAllyOf(ctx.Caster))
                                yield return unit;
                        }
                    }
                    break;
            }
        }
    }
}
