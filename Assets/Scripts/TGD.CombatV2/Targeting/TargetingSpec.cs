using System;
using UnityEngine;

namespace TGD.CombatV2.Targeting
{
    [Flags]
    public enum TargetOccupantMask
    {
        None = 0,
        Empty = 1 << 0,
        Self = 1 << 1,
        Ally = 1 << 2,
        Enemy = 1 << 3,
        AnyUnit = Self | Ally | Enemy,
        Any = Empty | AnyUnit
    }

    public enum TargetTerrainMask
    {
        NonObstacle = 0,
        Any = 1
    }

    public enum HitKind { None, Self, Ally, Enemy }
    public enum PlanKind { None, MoveOnly, MoveAndAttack, AttackOnly }

    [Serializable]
    public sealed class TargetingSpec
    {
        [Header("Who can I click?")]
        public TargetOccupantMask occupant = TargetOccupantMask.Empty;

        [Header("Terrain filter")]
        public TargetTerrainMask terrain = TargetTerrainMask.NonObstacle;

        [Header("Booleans")]
        public bool allowSelf = false;
        public bool requireOccupied = false;
        public bool requireEmpty = false;

        [Header("Optional")]
        public int maxRangeHexes = -1;

        public override string ToString()
        {
            return $"[Spec] occ={occupant} terr={terrain} allowSelf={allowSelf} reqOcc={requireOccupied} reqEmpty={requireEmpty} range={maxRangeHexes}";
        }
    }
}
