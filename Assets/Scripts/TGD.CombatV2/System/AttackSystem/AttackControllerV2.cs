// File: TGD.CombatV2/AttackControllerV2.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackControllerV2 : MonoBehaviour, IActionToolV2
    {
        enum TargetType
        {
            Invalid,
            MoveOnly,
            MoveAndAttack
        }

        struct AttackPlan
        {
            public TargetType type;
            public Hex target;
            public Hex landing;
            public List<Hex> path;
            public int steps;
            public float mrClick;
            public int moveSecsPred;
            public int moveSecsCharge;
            public int attackSecsCharge;
            public float baseMoveRateNoEnv;
            public string error;
        }

        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public HexBoardTiler tiler;
        public FootprintShape footprintForActor;
        public HexOccupancyService occService;
        public HexEnvironmentSystem env;

        [Header("Context (optional)")]
        public UnitRuntimeContext ctx;
        public MoveRateStatusRuntime status;
        public MonoBehaviour stickySource;

        [Header("Config")]
        public AttackActionConfigV2 config;
        public MoveActionConfig moveConfig;
        public MonoBehaviour moveCostProvider;
        public MonoBehaviour costProvider;

        [Header("Aim & Picking")]
        public Camera pickCamera;
        public LayerMask pickMask = ~0;
        public float rayMaxDistance = 2000f;
        public float pickPlaneY = 0.01f;

        [Header("Visuals")]
        public Color pathColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color invalidColor = new(1f, 0.3f, 0.3f, 0.7f);
        public float y = 0.01f;
        public float minStepSeconds = 0.06f;

        [Header("Turn Time (TEMP no-TM)")]
        public bool simulateTurnTime = true;
        public int baseTurnSeconds = 6;
        [SerializeField] int _turnSecondsLeft = -1;

        [Header("Debug")]
        public bool debugLog = true;

        public string Id => "Attack";

        IMoveCostService _moveCost;
        IAttackCostService _cost;
        IStickyMoveSource _sticky;
        HexAreaPainter _painter;
        HexOccupancy _occ;
        IGridActor _actor;
        bool _aiming;
        bool _moving;
        AttackPlan _plan;

        int MaxTurnSeconds => Mathf.Max(0, baseTurnSeconds + (ctx ? ctx.Speed : 0));

        void Awake()
        {
            _painter = new HexAreaPainter(tiler);
            _moveCost = moveCostProvider as IMoveCostService;
            _cost = costProvider as IAttackCostService;
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            if (!status) status = GetComponentInParent<MoveRateStatusRuntime>(true);
            _sticky = (stickySource as IStickyMoveSource) ?? (env as IStickyMoveSource);
        }

        void Start()
        {
            tiler?.EnsureBuilt();
            driver?.EnsureInit();

            if (authoring?.Layout == null || driver == null || !driver.IsReady)
            {
                enabled = false;
                return;
            }

            _occ = (occService != null ? occService.Get() : null) ?? new HexOccupancy(authoring.Layout);
            var fp = footprintForActor ? footprintForActor : CreateSingleFallback();
            _actor = new UnitGridAdapter(driver.UnitRef, fp);
            if (!_occ.TryPlace(_actor, driver.UnitRef.Position, driver.UnitRef.Facing))
                _occ.TryPlace(_actor, driver.UnitRef.Position, driver.UnitRef.Facing);
        }

        void OnDisable()
        {
            _painter?.Clear();
            _plan = default;
            _aiming = false;
        }

        void EnsureTurnTimeInited()
        {
            if (!simulateTurnTime) return;
            if (_turnSecondsLeft < 0)
            {
                _turnSecondsLeft = MaxTurnSeconds;
                if (debugLog)
                    Debug.Log($"[AttackV2] Init TurnTime = {_turnSecondsLeft}s", this);
            }
        }

        float EnvMult(Hex h) => env ? env.GetSpeedMult(h) : 1f;

        float GetBaseMoveRateNoEnv()
        {
            if (ctx != null)
            {
                int b = Mathf.Max(1, ctx.BaseMoveRate);
                float buffMult = 1f + Mathf.Max(-0.99f, ctx.MoveRatePctAdd);
                int flat = ctx.MoveRateFlatAdd;
                int mr = StatsMathV2.EffectiveMoveRateFromBase(b, new[] { buffMult }, flat);
                return Mathf.Max(0.01f, mr);
            }
            if (moveConfig != null)
            {
                int steps = Mathf.Max(1, moveConfig.fallbackSteps);
                float time = Mathf.Max(0.1f, moveConfig.timeCostSeconds);
                return Mathf.Max(0.01f, steps / time);
            }
            return 3f;
        }

        float GetStickyMultiplierNow()
        {
            float m = 1f;
            if (status != null)
            {
                foreach (var s in status.GetActiveMultipliers())
                    m *= Mathf.Clamp(s, 0.1f, 5f);
            }
            return Mathf.Clamp(m, 0.1f, 5f);
        }

        public void OnEnterAim()
        {
            EnsureTurnTimeInited();
            _aiming = true;
            _plan = default;
            _painter.Clear();
            AttackEventsV2.RaiseAimShown(driver.UnitRef, System.Array.Empty<Hex>());
        }

        public void OnExitAim()
        {
            _aiming = false;
            _plan = default;
            _painter.Clear();
            AttackEventsV2.RaiseAimHidden();
        }

        public void OnHover(Hex hex)
        {
            if (!_aiming || _moving) return;
            _plan = BuildPlan(hex);
            PaintPlan(_plan);
        }

        public IEnumerator OnConfirm(Hex hex)
        {
            EnsureTurnTimeInited();
            var plan = BuildPlan(hex);
            if (plan.type == TargetType.Invalid)
            {
                AttackEventsV2.RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NoPath, plan.error ?? "Path blocked");
                yield break;
            }

            yield return ExecutePlan(plan);
        }

        AttackPlan BuildPlan(Hex target)
        {
            var plan = new AttackPlan
            {
                type = TargetType.Invalid,
                target = target,
                landing = target,
                path = null,
                steps = 0,
                mrClick = 0f,
                moveSecsPred = 0,
                moveSecsCharge = 0,
                attackSecsCharge = 0,
                baseMoveRateNoEnv = GetBaseMoveRateNoEnv(),
                error = null
            };

            if (authoring?.Layout == null || driver == null || !driver.IsReady || _occ == null || _actor == null)
            {
                plan.error = "Not ready";
                return plan;
            }

            var layout = authoring.Layout;
            if (!layout.Contains(target))
            {
                plan.error = "Path blocked";
                return plan;
            }

            if (env != null && env.IsPit(target))
            {
                plan.error = "Path blocked";
                return plan;
            }

            var start = driver.UnitRef.Position;
            TGD.HexBoard.Unit occupant = null;
            driver.Map?.TryGetAt(target, out occupant);
            if (occupant == driver.UnitRef)
            {
                plan.error = "Path blocked";
                return plan;
            }

            if (occupant != null)
            {
                int range = config ? Mathf.Max(1, config.meleeRange) : 1;
                List<Hex> best = null;
                Hex bestLanding = default;
                int bestLen = int.MaxValue;

                foreach (var cand in Hex.Ring(target, range))
                {
                    if (!layout.Contains(cand)) continue;
                    if (env != null && env.IsPit(cand)) continue;
                    if (!_occ.CanPlace(_actor, cand, _actor.Facing, ignore: _actor)) continue;

                    var raw = FuzzyMoveRunner.BuildShortestPath(layout, _occ, _actor, start, cand, h => env != null && env.IsPit(h));
                    if (raw == null || raw.Count == 0) continue;

                    if (raw.Count < bestLen)
                    {
                        best = raw;
                        bestLanding = cand;
                        bestLen = raw.Count;
                    }
                }

                if (best == null)
                {
                    plan.error = "Path blocked";
                    return plan;
                }

                plan.path = best;
                plan.landing = bestLanding;
                plan.steps = Mathf.Max(0, best.Count - 1);
                plan.type = TargetType.MoveAndAttack;
            }
            else
            {
                var raw = FuzzyMoveRunner.BuildShortestPath(layout, _occ, _actor, start, target, h => env != null && env.IsPit(h));
                if (raw == null || raw.Count == 0)
                {
                    plan.error = "Path blocked";
                    return plan;
                }

                plan.path = raw;
                plan.steps = Mathf.Max(0, raw.Count - 1);
                plan.type = TargetType.MoveOnly;
            }

            if (ctx != null && ctx.Entangled && plan.steps > 0)
            {
                plan.type = TargetType.Invalid;
                plan.error = "Can't move while entangled.";
                return plan;
            }

            float startMult = Mathf.Clamp(EnvMult(start), 0.1f, 5f);
            float mrClick = Mathf.Max(0.01f, plan.baseMoveRateNoEnv * startMult);
            plan.mrClick = mrClick;

            if (plan.steps > 0)
            {
                float predict = plan.steps / mrClick;
                plan.moveSecsPred = Mathf.CeilToInt(predict);
                plan.moveSecsCharge = Mathf.CeilToInt(Mathf.Max(0f, plan.moveSecsPred - 0.2f));
            }
            else
            {
                plan.moveSecsPred = 0;
                plan.moveSecsCharge = 0;
            }

            if (plan.type == TargetType.MoveAndAttack)
            {
                float attackTime = config != null ? Mathf.Max(0f, config.AttackTimeSeconds) : 0f;
                plan.attackSecsCharge = Mathf.CeilToInt(attackTime);
            }

            return plan;
        }

        void PaintPlan(AttackPlan plan)
        {
            _painter.Clear();
            if (plan.type == TargetType.Invalid)
            {
                if (authoring?.Layout != null && authoring.Layout.Contains(plan.target))
                    _painter.Paint(new[] { plan.target }, invalidColor);
                AttackEventsV2.RaiseAimShown(driver.UnitRef, System.Array.Empty<Hex>());
                return;
            }

            if (plan.path != null && plan.path.Count > 0)
                _painter.Paint(plan.path, pathColor);

            AttackEventsV2.RaiseAimShown(driver.UnitRef, plan.path ?? System.Array.Empty<Hex>());
        }

        IEnumerator ExecutePlan(AttackPlan plan)
        {
            if (_moving) yield break;
            if (plan.type == TargetType.Invalid || plan.path == null || plan.path.Count == 0) yield break;

            var unit = driver.UnitRef;
            bool wantsAttack = plan.type == TargetType.MoveAndAttack;
            bool initialWantsAttack = wantsAttack;
            bool downgradedByTime = false;
            int moveBudget = Mathf.Max(0, plan.moveSecsCharge);
            int attackTime = wantsAttack ? Mathf.Max(0, plan.attackSecsCharge) : 0;

            if (ctx != null && ctx.Entangled && plan.steps > 0)
            {
                AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.CantMove, "Can't move while entangled.");
                yield break;
            }

            if (simulateTurnTime)
            {
                if (moveBudget > 0 && _turnSecondsLeft < moveBudget)
                {
                    AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "No More Time");
                    yield break;
                }
                if (wantsAttack && _turnSecondsLeft < moveBudget + attackTime)
                {
                    wantsAttack = false;
                    attackTime = 0;
                    downgradedByTime = true;
                }
            }

            if (!CanAffordMoveSeconds(moveBudget))
            {
                AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "Not enough energy for move.");
                yield break;
            }

            if (wantsAttack)
            {
                if (_cost == null || config == null)
                {
                    wantsAttack = false;
                    attackTime = 0;
                }
                else
                {
                    if (_cost.IsOnCooldown(unit, config))
                    {
                        AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.OnCooldown, "Attack on cooldown.");
                        yield break;
                    }
                    if (!_cost.HasEnough(unit, config))
                    {
                        AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                        yield break;
                    }
                }
            }

            PayMoveSeconds(moveBudget);
            bool attackCostPaid = false;
            if (wantsAttack && _cost != null && config != null)
            {
                _cost.Pay(unit, config);
                attackCostPaid = true;
            }

            float baseMR = Mathf.Max(0.01f, plan.baseMoveRateNoEnv);
            float threshold = moveConfig != null ? Mathf.Max(0.01f, moveConfig.refundThresholdSeconds) : Mathf.Max(0.01f, config != null ? config.refundThresholdSeconds : 0.8f);
            var sim = MoveSimulator.Run(plan.path, baseMR, moveBudget, EnvMult, threshold, debugLog);
            var reached = sim.ReachedPath;
            if (reached == null || reached.Count == 0)
                reached = new List<Hex> { unit.Position };

            int refunded = Mathf.Max(0, sim.RefundedSeconds);
            int spentMove = Mathf.Max(0, moveBudget - refunded);
            bool truncatedByBudget = reached.Count < plan.path.Count;

            if (plan.steps > 0 && reached.Count < 2)
            {
                RefundMoveSeconds(moveBudget);
                if (attackCostPaid)
                {
                    _cost.Refund(unit, config);
                    attackCostPaid = false;
                }
                yield break;
            }

            _moving = true;

            if (driver.unitView != null && reached.Count >= 2)
            {
                var fromW = authoring.Layout.World(reached[0], y);
                var toW = authoring.Layout.World(reached[^1], y);
                float keep = config ? config.keepDeg : 45f;
                float turn = config ? config.turnDeg : 135f;
                float speed = config ? config.turnSpeedDegPerSec : 720f;
                var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(unit.Facing, fromW, toW, keep, turn);
                yield return HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                unit.Facing = nf;
                if (_actor != null) _actor.Facing = nf;
            }

            AttackEventsV2.RaiseMoveStarted(unit, reached);

            bool stoppedByExternal = false;
            bool attackEligible = wantsAttack;
            bool attackRolledBack = false;

            for (int i = 1; i < reached.Count; i++)
            {
                if (ctx != null && ctx.Entangled)
                {
                    stoppedByExternal = true;
                    break;
                }

                var from = reached[i - 1];
                var to = reached[i];

                if (_occ.IsBlocked(to, _actor))
                {
                    stoppedByExternal = true;
                    break;
                }
                if (env != null && env.IsPit(to))
                {
                    stoppedByExternal = true;
                    break;
                }

                AttackEventsV2.RaiseMoveStep(unit, from, to, i, reached.Count - 1);

                var fromW = authoring.Layout.World(from, y);
                var toW = authoring.Layout.World(to, y);

                float fromMult = Mathf.Clamp(EnvMult(from), 0.1f, 5f);
                float stickyMult = GetStickyMultiplierNow();
                float effMR = Mathf.Max(0.01f, baseMR) * fromMult * stickyMult;
                float stepDuration = Mathf.Max(minStepSeconds, 1f / Mathf.Max(0.01f, effMR));
                HexMoveEvents.RaiseStepSpeed(unit, effMR, baseMR);

                if (attackEligible && effMR + 1e-4f < plan.mrClick)
                {
                    attackEligible = false;
                    attackRolledBack = true;
                    if (attackTime > 0 && simulateTurnTime)
                        HexMoveEvents.RaiseTimeRefunded(unit, attackTime);
                    if (attackCostPaid)
                    {
                        _cost.Refund(unit, config);
                        attackCostPaid = false;
                    }
                }

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / stepDuration;
                    if (driver.unitView != null)
                        driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                _occ.TryMove(_actor, to);

                if (_sticky != null && status != null && _sticky.TryGetSticky(to, out var stickM, out var stickTurns))
                {
                    if (stickTurns > 0 && !Mathf.Approximately(stickM, 1f))
                        status.ApplyStickyMultiplier(stickM, stickTurns);
                }

                if (driver.Map != null)
                {
                    if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to);
                }
                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;
            AttackEventsV2.RaiseMoveFinished(unit, unit.Position);

            if (truncatedByBudget && !stoppedByExternal)
                HexMoveEvents.RaiseNoMoreTime(unit);

            if (simulateTurnTime)
                _turnSecondsLeft = Mathf.Max(0, _turnSecondsLeft - spentMove - (attackEligible ? attackTime : 0));

            if (refunded > 0)
                RefundMoveSeconds(refunded);

            if (status != null && spentMove > 0)
                status.ConsumeSeconds(spentMove);

            if (attackEligible && wantsAttack && !stoppedByExternal && !truncatedByBudget)
            {
                AttackEventsV2.RaiseHit(unit, plan.target);
            }
            else if (initialWantsAttack && !downgradedByTime)
            {
                if (attackCostPaid)
                {
                    _cost.Refund(unit, config);
                    attackCostPaid = false;
                }
                string msg = attackRolledBack ? "Attack cancelled (slowed)." : "Out of reach.";
                AttackEventsV2.RaiseMiss(unit, msg);
            }
        }

        bool CanAffordMoveSeconds(int seconds)
        {
            if (seconds <= 0 || _moveCost == null || moveConfig == null) return true;
            if (_moveCost is MoveCostServiceV2Adapter adapter && adapter.stats != null)
            {
                int need = seconds * Mathf.Max(0, moveConfig.energyCost);
                return adapter.stats.Energy >= need;
            }
            return _moveCost.HasEnough(driver.UnitRef, moveConfig);
        }

        void PayMoveSeconds(int seconds)
        {
            if (seconds <= 0 || _moveCost == null || moveConfig == null) return;
            for (int i = 0; i < seconds; i++)
                _moveCost.Pay(driver.UnitRef, moveConfig);
        }

        void RefundMoveSeconds(int seconds)
        {
            if (seconds <= 0) return;
            if (_moveCost != null && moveConfig != null)
                _moveCost.RefundSeconds(driver.UnitRef, moveConfig, seconds);
            HexMoveEvents.RaiseTimeRefunded(driver.UnitRef, seconds);
        }

        static FootprintShape CreateSingleFallback()
        {
            var s = ScriptableObject.CreateInstance<FootprintShape>();
            s.name = "Footprint_Single_Runtime";
            s.offsets = new() { new L2(0, 0) };
            return s;
        }
    }
}
