using System.Collections;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public abstract class ChainTestActionBase : MonoBehaviour, IActionToolV2, IActionCostPreviewV2, IActionEnergyReportV2
    {
        [Header("Owner")]
        public HexBoardTestDriver driver;

        [Header("Targeting")]
        public TargetMode targetMode = TargetMode.AnyClick;
        public int maxRangeHexes = -1;
        public DefaultTargetValidator targetValidator;
        public HexBoardTiler tiler;
        public Color hoverValidColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color hoverInvalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Config")]
        public string actionId = "ChainTest";
        [Min(0)] public int timeCostSeconds = 0;
        [Min(0)] public int energyCost = 0;

        [Header("Cooldown")]
        [Min(0)] public int cooldownSeconds = 0;

        int _usedSeconds;
        int _refundedSeconds;
        int _energyUsed;
        Hex? _lastTarget;
        TargetSelectionCursor _cursor;

        public string Id => actionId;
        public abstract ActionKind Kind { get; }

        public virtual string CooldownId => actionId;
        public virtual int CooldownSeconds => Mathf.Max(0, cooldownSeconds);

        protected virtual void Awake()
        {
            if (!driver)
                driver = GetComponentInParent<HexBoardTestDriver>(true);

            if (!tiler)
            {
                tiler = GetComponentInParent<HexBoardTiler>(true);
                if (!tiler && driver != null)
                {
                    tiler = driver.authoring != null
                        ? driver.authoring.GetComponent<HexBoardTiler>() ?? driver.authoring.GetComponentInParent<HexBoardTiler>(true)
                        : driver.GetComponentInParent<HexBoardTiler>(true);
                }
            }

            if (!targetValidator)
            {
                targetValidator = GetComponent<DefaultTargetValidator>() ?? GetComponentInParent<DefaultTargetValidator>(true);
                if (!targetValidator && driver != null)
                    targetValidator = driver.GetComponentInParent<DefaultTargetValidator>(true);
            }
        }

        TargetSelectionCursor Cursor => _cursor;

        public void SetCursorHighlighter(IHexHighlighter highlighter)
        {
            _cursor = highlighter != null ? new TargetSelectionCursor(highlighter) : null;
        }

        public virtual void OnEnterAim()
        {
            Cursor?.Clear();
            _lastTarget = null;
        }

        public virtual void OnExitAim()
        {
            Cursor?.Clear();
        }

        public virtual void OnHover(Hex hex)
        {
            var cursor = Cursor;
            if (cursor == null)
                return;

            var unit = ResolveUnit();
            var check = ValidateTarget(unit, hex);
            cursor.ShowSingle(hex, check.ok ? hoverValidColor : hoverInvalidColor);
        }

        public virtual IEnumerator OnConfirm(Hex hex)
        {
            _lastTarget = hex;
            Cursor?.Clear();
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
        public Hex? LastTarget => _lastTarget;

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

        public virtual TargetingSpec GetTargetingSpec()
        {
            return TargetingPresets.For(targetMode, maxRangeHexes);
        }

        public virtual TargetCheckResult ValidateTarget(Unit unit, Hex hex)
        {
            var validator = targetValidator;
            if (validator == null)
            {
                return new TargetCheckResult
                {
                    ok = true,
                    reason = TargetInvalidReason.None,
                    hit = HitKind.None,
                    plan = PlanKind.MoveOnly
                };
            }

            return validator.Check(unit, hex, GetTargetingSpec());
        }
    }
}
