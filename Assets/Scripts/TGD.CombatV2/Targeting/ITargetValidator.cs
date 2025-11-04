using TGD.CoreV2;

namespace TGD.CombatV2.Targeting
{
    public interface ITargetValidator
    {
        TargetCheckResult Check(Unit actor, Hex hex, TargetingSpec spec);
    }

    public enum TargetInvalidReason
    {
        None,
        Self,
        Friendly,
        EnemyNotAllowed,
        EmptyNotAllowed,
        Blocked,
        OutOfRange,
        Unknown
    }

    public struct TargetCheckResult
    {
        public bool ok;
        public TargetInvalidReason reason;
        public Unit hitUnit;
        public HitKind hit;
        public PlanKind plan;

        public override string ToString()
        {
            if (!ok) return $"[Probe] Reject(reason={reason})";
            return $"[Probe] Pass(hit={hit}, plan={plan})";
        }
    }
}
