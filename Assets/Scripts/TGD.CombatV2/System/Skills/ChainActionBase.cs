using System.Collections;
using UnityEngine;
using UnityEngine.Pool;
using TGD.CoreV2;
using TGD.CombatV2.Targeting;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public abstract class ChainActionBase : ActionToolBase, IActionToolV2, IActionCostPreviewV2, IActionEnergyReportV2, IActionExecReportV2, IBindContext, ICursorUser
    {
        protected TurnManagerV2 turnManager;

        [Header("Targeting")]
        public TargetRule targetRule = TargetRule.AnyClick;
        public int maxRangeHexes = -1;
        public TargetSelectionProfile selection = TargetSelectionProfile.Default;
        public DefaultTargetValidator targetValidator;
        public HexBoardTiler tiler;
        public Color hoverValidColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color hoverInvalidColor = new(1f, 0.3f, 0.3f, 0.7f);
        public Color rangeColor = new(0.2f, 0.8f, 1f, 0.85f);

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
            if (!ctx)
                ctx = GetComponentInParent<UnitRuntimeContext>(true);

            if (!tiler)
                tiler = ResolveTiler();

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
            RenderSelectionArea(null);
            _lastTarget = null;
            AttackEventsV2.RaiseAimShown(OwnerUnit, System.Array.Empty<Hex>());
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

            var validator = ResolveValidator();
            var spec = _spec ?? GetTargetingSpec();
            var unit = OwnerUnit;
            var check = validator != null
                ? validator.Check(unit, hex, spec)
                : new TargetCheckResult { ok = true, hit = HitKind.None, plan = PlanKind.MoveOnly };

            RenderSelectionArea(hex, check);

            if (check.ok)
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

        public virtual TargetingSpec GetTargetingSpec()
        {
            var profile = selection.WithDefaults();
            int resolvedRange = profile.ResolveRange(ctx, maxRangeHexes);
            var spec = TargetingPresets.For(targetRule, resolvedRange);
            spec.selection = profile;
            spec.maxRangeHexes = resolvedRange;
            return spec;
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

        public override void BindContext(UnitRuntimeContext context, TurnManagerV2 tm)
        {
            base.BindContext(context, tm);
            turnManager = tm;

            if (ctx != null)
            {
                if (!_validator)
                    _validator = ctx.GetComponentInParent<DefaultTargetValidator>(true);
            }

            if (!_validator)
                _validator = ResolveValidator();
            if (_validator && targetValidator == null)
                targetValidator = _validator;
            if (!tiler)
                tiler = ResolveTiler();
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

            if (ctx != null)
            {
                _validator = ctx.GetComponent<DefaultTargetValidator>()
                    ?? ctx.GetComponentInChildren<DefaultTargetValidator>(true)
                    ?? ctx.GetComponentInParent<DefaultTargetValidator>(true);
                if (_validator)
                {
                    if (targetValidator == null)
                        targetValidator = _validator;
                    return _validator;
                }
            }

            _validator = GetComponent<DefaultTargetValidator>()
                ?? GetComponentInChildren<DefaultTargetValidator>(true)
                ?? GetComponentInParent<DefaultTargetValidator>(true);
            if (_validator && targetValidator == null)
                targetValidator = _validator;
            return _validator;
        }

        HexBoardTiler ResolveTiler()
        {
            if (tiler)
                return tiler;

            if (ctx != null)
            {
                var ctxTiler = ctx.GetComponent<HexBoardTiler>()
                    ?? ctx.GetComponentInChildren<HexBoardTiler>(true)
                    ?? ctx.GetComponentInParent<HexBoardTiler>(true);
                if (ctxTiler)
                {
                    tiler = ctxTiler;
                    return tiler;
                }
            }

            var resolved = GetComponentInParent<HexBoardTiler>(true) ?? GetComponentInChildren<HexBoardTiler>(true);
            if (resolved)
                tiler = resolved;
            return tiler;
        }

        void RenderSelectionArea(Hex? hover, TargetCheckResult? checkOverride = null)
        {
            var cursor = Cursor;
            if (cursor == null)
                return;

            var spec = _spec ?? GetTargetingSpec();
            var unit = OwnerUnit;
            if (spec == null || unit == null)
            {
                cursor.Clear();
                return;
            }

            var profile = spec.selection.WithDefaults();
            var validator = ResolveValidator();

            if (profile.shape == CastShape.SingleCell)
            {
                if (hover.HasValue)
                {
                    var check = checkOverride ?? (validator != null
                        ? validator.Check(unit, hover.Value, spec)
                        : new TargetCheckResult { ok = true, hit = HitKind.None, plan = PlanKind.MoveOnly });
                    bool allowed = check.ok;
                    cursor.ShowSingle(hover.Value, allowed ? hoverValidColor : hoverInvalidColor);
                }
                else
                {
                    cursor.Clear();
                }
                return;
            }

            var valid = ListPool<Hex>.Get();
            var invalid = ListPool<Hex>.Get();
            try
            {
                TargetSelectionAreaBuilder.Build(
                    ctx,
                    unit,
                    hover,
                    spec,
                    validator,
                    tiler != null ? tiler.authoring : null,
                    valid,
                    invalid);

                bool hoverAllowed = false;
                if (hover.HasValue)
                {
                    var check = checkOverride ?? (validator != null
                        ? validator.Check(unit, hover.Value, spec)
                        : new TargetCheckResult { ok = true, hit = HitKind.None, plan = PlanKind.MoveOnly });
                    hoverAllowed = check.ok;
                }

                cursor.ShowArea(
                    valid,
                    invalid,
                    hover,
                    hoverAllowed,
                    rangeColor,
                    hoverInvalidColor,
                    hoverValidColor,
                    hoverInvalidColor);
            }
            finally
            {
                ListPool<Hex>.Release(valid);
                ListPool<Hex>.Release(invalid);
            }
        }
    }
}
