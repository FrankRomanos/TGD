using System.Collections;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    public abstract class ChainActionBase : ActionToolBase, IActionToolV2, IActionCostPreviewV2, IActionEnergyReportV2, IActionExecReportV2, IBindContext, ICursorUser
    {
        [Header("Owner")]
        public HexBoardTestDriver driver;

        protected UnitRuntimeContext ctx;
        protected TurnManagerV2 turnManager;

        [Header("Targeting")]
        public TargetMode targetMode = TargetMode.AnyClick;
        public int maxRangeHexes = -1;
        public DefaultTargetValidator targetValidator;
        public HexBoardTiler tiler;
        public Color hoverValidColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color hoverInvalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Config")]
        public string skillId = "ChainTest";
        [Min(0)] public int timeCostSeconds = 0;
        [Min(0)] public int energyCost = 0;

        [Header("UI")]
        public Sprite icon;

        [Header("Cooldown")]
        [Min(0)] public int cooldownSeconds = 0;

        int _usedSeconds;
        int _refundedSeconds;
        int _energyUsed;
        Hex? _lastTarget;
        TargetSelectionCursor _cursor;
        protected DefaultTargetValidator _validator;
        protected TargetingSpec _spec;

        public string Id => skillId;
        public abstract ActionKind Kind { get; }

        public virtual Sprite Icon => icon;

        public virtual string CooldownId => skillId;
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

            if (!_validator)
                _validator = ResolveValidator();
        }

        protected override void HookEvents(bool bind)
        {
        }

        TargetSelectionCursor Cursor => _cursor;

        public void SetCursorHighlighter(IHexHighlighter highlighter)
        {
            _cursor = highlighter != null ? new TargetSelectionCursor(highlighter) : null;
        }

        public virtual void OnEnterAim()
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;

            _spec = GetTargetingSpec();
            Cursor?.Clear();
            _lastTarget = null;
            AttackEventsV2.RaiseAimShown(ResolveUnit(), System.Array.Empty<Hex>());
        }

        public virtual void OnExitAim()
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;
            Cursor?.Clear();
            AttackEventsV2.RaiseAimHidden();
        }

        public virtual void OnHover(Hex hex)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;

            var cursor = Cursor;
            if (cursor == null)
                return;

            var validator = ResolveValidator();
            var spec = _spec ?? GetTargetingSpec();
            var unit = ResolveUnit();
            var check = validator != null ? validator.Check(unit, hex, spec) : new TargetCheckResult { ok = true, hit = HitKind.None, plan = PlanKind.MoveOnly };

            var color = check.ok && check.hit != HitKind.Ally ? hoverValidColor : hoverInvalidColor;
            cursor.ShowSingle(hex, color);

            if (check.ok && check.hit != HitKind.Ally)
                AttackEventsV2.RaiseAimShown(unit, new[] { hex });
            else
                AttackEventsV2.RaiseAimShown(unit, System.Array.Empty<Hex>());
        }

        public virtual IEnumerator OnConfirm(Hex hex)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                yield break;
            _lastTarget = hex;
            Cursor?.Clear();
            SetExecReport(Mathf.Max(0, timeCostSeconds), 0, Mathf.Max(0, energyCost));
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

        protected void SetExecReport(int usedSeconds, int refundedSeconds, int energyUsed)
        {
            _usedSeconds = Mathf.Max(0, usedSeconds);
            _refundedSeconds = Mathf.Max(0, refundedSeconds);
            _energyUsed = Mathf.Max(0, energyUsed);
        }

        public Unit ResolveUnit()
        {
            if (ctx != null && ctx.boundUnit != null)
                return ctx.boundUnit;
            return driver != null ? driver.UnitRef : null;
        }

        public virtual TargetingSpec GetTargetingSpec()
        {
            return TargetingPresets.For(targetMode, maxRangeHexes);
        }

        public virtual TargetCheckResult ValidateTarget(Unit unit, Hex hex)
        {
            var validator = ResolveValidator();
            var spec = _spec ?? GetTargetingSpec();
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

            return validator.Check(unit, hex, spec);
        }

        public virtual void BindContext(UnitRuntimeContext context, TurnManagerV2 tm)
        {
            ctx = context;
            turnManager = tm;

            if (ctx != null)
            {
                if (!_validator)
                    _validator = ctx.GetComponentInParent<DefaultTargetValidator>(true);
                if (!tiler)
                    tiler = ctx.GetComponentInParent<HexBoardTiler>(true);
            }

            if (!_validator)
                _validator = ResolveValidator();
            if (_validator && targetValidator == null)
                targetValidator = _validator;
            if (!tiler)
                tiler = GetComponentInParent<HexBoardTiler>(true);
        }

        protected DefaultTargetValidator ResolveValidator()
        {
            if (_validator)
                return _validator;

            if (targetValidator)
            {
                _validator = targetValidator;
                return _validator;
            }

            _validator = GetComponent<DefaultTargetValidator>() ?? GetComponentInParent<DefaultTargetValidator>(true);
            if (!_validator && driver != null)
                _validator = driver.GetComponentInParent<DefaultTargetValidator>(true);
            if (_validator && targetValidator == null)
                targetValidator = _validator;
            return _validator;
        }
    }
}
