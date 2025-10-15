using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    /// 简化版能量服务，封装 TurnManagerV2 的资源接口。
    /// </summary>
    public sealed class EnergyService : MonoBehaviour
    {
        public TurnManagerV2 turnManager;
        [Tooltip("默认移动每秒能量消耗（用于 FreeMove 退款）")]
        public int defaultMoveEnergyPerSecond = 0;

        public int Current(Unit unit)
        {
            var pool = GetPool(unit);
            return pool != null ? pool.Get("Energy") : 0;
        }

        public bool CanAfford(Unit unit, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            var pool = GetPool(unit);
            return pool != null && pool.Has("Energy", amount);
        }

        public void Apply(Unit unit, int delta, string tag)
        {
            if (delta == 0)
            {
                return;
            }

            var pool = GetPool(unit);
            if (pool == null)
            {
                return;
            }

            if (delta < 0)
            {
                pool.Spend("Energy", -delta, tag);
            }
            else
            {
                pool.Refund("Energy", delta, tag);
            }
        }

        public int MoveEnergyPerSecond(Unit unit)
        {
            return defaultMoveEnergyPerSecond;
        }

        IResourcePool GetPool(Unit unit)
        {
            return turnManager != null ? turnManager.GetResources(unit) : null;
        }
    }
}
