using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public abstract class ChainTestActionBase : MonoBehaviour, IActionToolV2, IActionCostPreviewV2, IActionEnergyReportV2
    {
        [Header("Owner")]
        public HexBoardTestDriver driver;

        [Header("Config")]
        public string actionId = "ChainTest";
        [Min(0)] public int timeCostSeconds = 0;
        [Min(0)] public int energyCost = 0;

        int _usedSeconds;
        int _refundedSeconds;
        int _energyUsed;

        public string Id => actionId;
        public abstract ActionKind Kind { get; }

        public virtual void OnEnterAim() { }
        public virtual void OnExitAim() { }
        public virtual void OnHover(Hex hex) { }

        public virtual IEnumerator OnConfirm(Hex hex)
        {
            _usedSeconds = Mathf.Max(0, timeCostSeconds);
            _refundedSeconds = 0;
            _energyUsed = Mathf.Max(0, energyCost);
            yield return null;
        }

        public bool TryPeekCost(out int seconds, out int energy)
        {
            seconds = Mathf.Max(0, timeCostSeconds);
            energy = Mathf.Max(0, energyCost);
            return true;
        }

        public int UsedSeconds => _usedSeconds;
        public int RefundedSeconds => _refundedSeconds;
        public int EnergyUsed => _energyUsed;

        public void Consume()
        {
            _usedSeconds = 0;
            _refundedSeconds = 0;
            _energyUsed = 0;
        }

        public Unit ResolveUnit()
        {
            return driver != null ? driver.UnitRef : null;
        }
    }
}
