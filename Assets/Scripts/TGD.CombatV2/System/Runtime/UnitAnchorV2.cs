using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.CombatV2
{
    public static class UnitAnchorV2
    {
        public static bool TryGetView(Unit unit, out Transform view)
        {
            view = null;
            if (unit == null)
                return false;

            if (!string.IsNullOrEmpty(unit.Id) &&
                UnitLocator.TryGetTransform(unit.Id, out var located) &&
                located != null)
            {
                view = located;
                return true;
            }

            return false;
        }
    }
}
