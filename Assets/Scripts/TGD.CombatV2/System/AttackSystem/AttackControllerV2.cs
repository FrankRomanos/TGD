// File: TGD.CombatV2/AttackControllerV2.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackControllerV2 : MonoBehaviour, IActionToolV2, IActionExecReportV2
    {
        const float MR_MIN = 1f;
        const float MR_MAX = 12f;
        const float ENV_MIN = 0.1f;
        const float ENV_MAX = 5f;

        public string Id => "Attack";

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
        public AttackActionConfigV2 attackConfig;
        public MoveActionConfig moveConfig;
        public MonoBehaviour enemyProvider;

        [Header("Turn Manager Binding")]
        public bool UseTurnManager = true;
        public bool ManageEnergyLocally = false;
        public bool ManageTurnTimeLocally = false;

        [Header("Costs & Turn")]
        public bool simulateTurnTime = true;
        public int baseTurnSeconds = 6;
        public TurnManagerV2 turnManager;
        TurnManagerV2 _boundTurnManager;

        [Header("Animation")]
        public float stepSeconds = 0.12f;
        public float minStepSeconds = 0.06f;
        public float y = 0.01f;

        [Header("Visuals")]
        public Color previewColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color invalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Debug")]
        public bool debugLog = true;

        HexAreaPainter _painter;
        HexOccupancy _occ;
        IGridActor _actor;
        IStickyMoveSource _sticky;
        IEnemyLocator _enemyLocator;

        bool _aiming;
        bool _moving;
        PreviewData _currentPreview;
        Hex? _hover;

        float _turnSecondsLeft = -1f;
        int _attacksThisTurn;
        int _reportUsedSeconds;
        int _reportRefundedSeconds;
        int _reportMoveUsedSeconds;
        int _reportMoveRefundSeconds;
        int _reportAttackUsedSeconds;
        int _reportAttackRefundSeconds;
        public int ReportMoveEnergyNet { get; private set; }
        public int ReportAttackEnergyNet { get; private set; }
        bool _reportPending;
        int _reportComboBaseCount;
        int _pendingComboBaseCount;

        struct PendingAttack
        {
            public bool active;
            public bool strikeProcessed;
            public Unit unit;
            public Hex target;
            public int comboIndex;
        }

        PendingAttack _pendingAttack;
        void ClearExecReport()
        {
            _reportUsedSeconds = 0;
            _reportRefundedSeconds = 0;
            _reportMoveUsedSeconds = 0;
            _reportMoveRefundSeconds = 0;
            _reportAttackUsedSeconds = 0;
            _reportAttackRefundSeconds = 0;
            ReportMoveEnergyNet = 0;
            ReportAttackEnergyNet = 0;
            _reportPending = false;
            _reportComboBaseCount = 0;
            _pendingComboBaseCount = 0;
        }

        void SetExecReport(int usedSeconds, int refundedSeconds, bool attackExecuted)
        {
            _reportUsedSeconds = Mathf.Max(0, usedSeconds);
            _reportRefundedSeconds = Mathf.Max(0, refundedSeconds);
            _reportComboBaseCount = attackExecuted ? Mathf.Max(0, _pendingComboBaseCount) : 0;
            _reportPending = true;
            _pendingComboBaseCount = 0;
            LogAttackSummary();
        }
        void LogAttackSummary()
        {
            var unit = driver != null ? driver.UnitRef : null;
            string label = TurnManagerV2.FormatUnitLabel(unit);
            Debug.Log($"[Attack] Use moveSecs={_reportMoveUsedSeconds} atkSecs={_reportAttackUsedSeconds} energyMove={ReportMoveEnergyNet} energyAtk={ReportAttackEnergyNet} U={label}", this);
        }

        float MaxTurnSeconds => Mathf.Max(0f, baseTurnSeconds + (ctx ? ctx.Speed : 0));

        public void AttachTurnManager(TurnManagerV2 manager)
        {
            if (_boundTurnManager != null)
                _boundTurnManager.TurnStarted -= OnTurnStarted;

            turnManager = manager;
            _boundTurnManager = null;

            UseTurnManager = manager != null;
            simulateTurnTime = !UseTurnManager;
            ManageTurnTimeLocally = !UseTurnManager;
            ManageEnergyLocally = !UseTurnManager;
            if (!ManageTurnTimeLocally)
                _turnSecondsLeft = -1f;

            if (!UseTurnManager || !isActiveAndEnabled)
                return;

            turnManager.TurnStarted += OnTurnStarted;
            _boundTurnManager = turnManager;
        }

        IResourcePool ResolveResourcePool()
        {
            if (!UseTurnManager || turnManager == null || driver == null || driver.UnitRef == null)
                return null;
            return turnManager.GetResources(driver.UnitRef);
        }

        int ResolveEnergyAvailable(IResourcePool pool)
        {
            if (pool != null)
                return pool.Get("Energy");
            var stats = ctx != null ? ctx.stats : null;
            return stats != null ? stats.Energy : int.MaxValue;
        }

        bool TryReserveEnergy(ref int available, int cost)
        {
            if (cost <= 0)
                return true;
            if (available == int.MaxValue)
                return true;
            if (available < cost)
                return false;
            available -= cost;
            return true;
        }

        int ExternalTimeRemaining()
        {
            if (!UseTurnManager || turnManager == null || driver == null || driver.UnitRef == null)
                return int.MaxValue;

            var budget = turnManager.GetBudget(driver.UnitRef);
            return budget != null ? Mathf.Max(0, budget.Remaining) : int.MaxValue;
        }

        struct MoveRatesSnapshot
        {
            public int baseRate;
            public float buffMult;
            public float stickyMult;
            public int flatAfter;
            public float startEnvMult;
            public float mrNoEnv;
            public float mrClick;
            public bool startIsSticky;
        }

        sealed class PreviewData
        {
            public bool valid;
            public bool targetIsEnemy;
            public Hex targetHex;
            public Hex landingHex;
            public List<Hex> path;
            public int steps;
            public int moveSecsPred;
            public int moveSecsCharge;
            public int attackSecsCharge;
            public int moveEnergyCost;
            public int attackEnergyCost;
            public float mrClick;
            public float mrNoEnv;
            public MoveRatesSnapshot rates;
            public AttackRejectReasonV2 rejectReason;
            public string rejectMessage;
        }

        void Awake()
        {
            _painter = new HexAreaPainter(tiler);
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            if (!status) status = GetComponentInParent<MoveRateStatusRuntime>(true);
            if (!turnManager) turnManager = GetComponentInParent<TurnManagerV2>(true);
            _sticky = (stickySource as IStickyMoveSource) ?? (env as IStickyMoveSource);
            _enemyLocator = enemyProvider as IEnemyLocator;
            if (_enemyLocator == null)
                _enemyLocator = GetComponentInParent<IEnemyLocator>(true);
        }

        void OnEnable()
        {
            ClearPendingAttack();
            AttackEventsV2.AttackStrikeFired += OnAttackStrikeFired;
            AttackEventsV2.AttackAnimationEnded += OnAttackAnimationEnded;
            AttachTurnManager(turnManager);
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

            _occ = occService ? occService.Get() : new HexOccupancy(authoring.Layout);
            var fp = footprintForActor ? footprintForActor : CreateSingleFallback();
            _actor = new UnitGridAdapter(driver.UnitRef, fp);
            if (!_occ.TryPlace(_actor, driver.UnitRef.Position, driver.UnitRef.Facing))
                _occ.TryPlace(_actor, driver.UnitRef.Position, driver.UnitRef.Facing);
        }

        void OnDisable()
        {
            AttackEventsV2.AttackStrikeFired -= OnAttackStrikeFired;
            AttackEventsV2.AttackAnimationEnded -= OnAttackAnimationEnded;
            if (_boundTurnManager != null)
            {
                _boundTurnManager.TurnStarted -= OnTurnStarted;
                _boundTurnManager = null;
            }
            ClearPendingAttack();
            _painter?.Clear();
            _currentPreview = null;
            _hover = null;
        }

        void OnTurnStarted(Unit unit)
        {
            if (!UseTurnManager || turnManager == null || driver == null || driver.UnitRef != unit)
                return;

            _attacksThisTurn = 0;
            _pendingComboBaseCount = 0;
            _turnSecondsLeft = -1f;
        }

        void EnsureTurnTimeInited()
        {
            if (!ManageTurnTimeLocally) return;
            if (_turnSecondsLeft >= 0f) return;
            _turnSecondsLeft = MaxTurnSeconds;
        }

        bool IsReady => authoring?.Layout != null && driver != null && driver.IsReady && _occ != null && _actor != null;

        void RaiseRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            AttackEventsV2.RaiseRejected(unit, reason, message);
        }

        public void OnEnterAim()
        {
            EnsureTurnTimeInited();
            if (!IsReady)
            {
                RaiseRejected(driver ? driver.UnitRef : null, AttackRejectReasonV2.NotReady, "Not ready.");
                return;
            }
            int needSec = Mathf.Max(1, Mathf.CeilToInt(moveConfig ? moveConfig.timeCostSeconds : 1f));
            if (UseTurnManager && turnManager != null && driver != null && driver.UnitRef != null)
            {
                var budget = turnManager.GetBudget(driver.UnitRef);
                if (budget == null || !budget.HasTime(needSec))
                {
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "No more time.");
                    return;
                }
            }
            else if (ManageTurnTimeLocally && _turnSecondsLeft + 1e-4f < needSec)
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "No more time.");
                return;
            }

            var pool = ResolveResourcePool();
            int energyAvailable = ResolveEnergyAvailable(pool);
            int moveEnergyCost = MoveEnergyPerSecond();
            bool anyEnergyCost = (moveEnergyCost > 0) || (attackConfig != null && attackConfig.baseEnergyCost > 0f);
            if (anyEnergyCost && energyAvailable <= 0)
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                return;
            }
            if (moveEnergyCost > 0 && energyAvailable < moveEnergyCost)
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                return;
            }

            _aiming = true;
            _hover = null;
            _currentPreview = null;
            _painter.Clear();

            AttackEventsV2.RaiseAimShown(driver.UnitRef, System.Array.Empty<Hex>());
        }

        public void OnExitAim()
        {
            _aiming = false;
            _hover = null;
            _currentPreview = null;
            _painter.Clear();
            AttackEventsV2.RaiseAimHidden();
        }

        public void OnHover(Hex hex)
        {
            if (!_aiming || _moving) return;
            if (!IsReady) return;
            if (_hover.HasValue && _hover.Value.Equals(hex) && _currentPreview != null) return;
            _hover = hex;
            _currentPreview = BuildPreview(hex, false);
            RenderPreview(_currentPreview);
        }

        public IEnumerator OnConfirm(Hex hex)
        {
            ClearExecReport();
            EnsureTurnTimeInited();
            if (!IsReady)
            {
                RaiseRejected(driver ? driver.UnitRef : null, AttackRejectReasonV2.NotReady, "Not ready.");
                yield break;
            }
            if (_moving)
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.Busy, "Attack in progress.");
                yield break;
            }

            var preview = (_currentPreview != null && _currentPreview.valid && _currentPreview.targetHex.Equals(hex))
                ? ClonePreview(_currentPreview)
                : BuildPreview(hex, true);

            if (preview == null || !preview.valid)
            {
                RaiseRejected(driver.UnitRef,
                    preview != null ? preview.rejectReason : AttackRejectReasonV2.NoPath,
                    preview != null ? preview.rejectMessage : "Invalid target.");
                yield break;
            }

            if (ctx != null && ctx.Entangled && (preview.path == null || preview.path.Count > 1))
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "Can't move while entangled.");
                yield break;
            }

            preview.moveEnergyCost = Mathf.Max(0, preview.moveSecsCharge) * MoveEnergyPerSecond();
            if (preview.targetIsEnemy)
            {
                preview.attackEnergyCost = attackConfig ? Mathf.CeilToInt(attackConfig.baseEnergyCost * (1f + 0.5f * _attacksThisTurn)) : 0;
            }
            else
            {
                preview.attackEnergyCost = 0;
                preview.attackSecsCharge = 0;
            }

            bool attackPlanned = preview.targetIsEnemy;
            _pendingComboBaseCount = attackPlanned ? Mathf.Max(0, _attacksThisTurn) : 0;
            int moveSecsCharge = Mathf.Max(0, preview.moveSecsCharge);
            int attackSecsCharge = preview.targetIsEnemy ? Mathf.Max(0, preview.attackSecsCharge) : 0;
            int moveEnergyCost = Mathf.Max(0, preview.moveEnergyCost);
            int attackEnergyCost = preview.targetIsEnemy ? Mathf.Max(0, preview.attackEnergyCost) : 0;

            if (UseTurnManager)
            {
                int timeLeft = ExternalTimeRemaining();
                if (timeLeft + 1e-4f < moveSecsCharge)
                {
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "No more time.");
                    yield break;
                }
                if (attackPlanned && timeLeft + 1e-4f < moveSecsCharge + attackSecsCharge)
                {
                    attackPlanned = false;
                    attackSecsCharge = 0;
                    attackEnergyCost = 0;
                    _pendingComboBaseCount = 0;
                }
            }
            else if (simulateTurnTime)
            {
                if (_turnSecondsLeft + 1e-4f < moveSecsCharge)
                {
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "No more time.");
                    yield break;
                }
                if (attackPlanned && _turnSecondsLeft + 1e-4f < moveSecsCharge + attackSecsCharge)
                {
                    attackPlanned = false;
                    attackSecsCharge = 0;
                    attackEnergyCost = 0;
                    _pendingComboBaseCount = 0;
                }
            }

            var resourcePool = ResolveResourcePool();
            int energyAvailable = ResolveEnergyAvailable(resourcePool);
            if (!TryReserveEnergy(ref energyAvailable, moveEnergyCost))
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy for move.");
                yield break;
            }
            if (attackPlanned && !TryReserveEnergy(ref energyAvailable, attackEnergyCost))
            {
                RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy for attack.");
                yield break;
            }

            if (ManageEnergyLocally)
                SpendEnergy(moveEnergyCost);
            int attackEnergySpent = 0;
            if (attackPlanned)
            {
                if (ManageEnergyLocally && attackEnergyCost > 0)
                    SpendEnergy(attackEnergyCost);
                attackEnergySpent = Mathf.Max(0, attackEnergyCost);
                _attacksThisTurn = Mathf.Max(0, _attacksThisTurn + 1);
            }

            if (ManageTurnTimeLocally)
            {
                _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft - moveSecsCharge, 0f, MaxTurnSeconds);
                if (attackPlanned)
                    _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft - attackSecsCharge, 0f, MaxTurnSeconds);
            }

            _currentPreview = null;
            _hover = null;
            _painter.Clear();
            AttackEventsV2.RaiseAimHidden();

            _moving = true;
            yield return RunAttack(preview, attackPlanned, moveSecsCharge, moveEnergyCost, attackSecsCharge, attackEnergySpent);
            _moving = false;
        }

        void RenderPreview(PreviewData preview)
        {
            _painter.Clear();
            if (preview == null)
            {
                AttackEventsV2.RaiseAimShown(driver.UnitRef, System.Array.Empty<Hex>());
                return;
            }

            if (!preview.valid || preview.path == null)
            {
                if (_hover.HasValue)
                    _painter.Paint(new[] { _hover.Value }, invalidColor);
                AttackEventsV2.RaiseAimShown(driver.UnitRef, System.Array.Empty<Hex>());
            }

            _painter.Paint(preview.path, previewColor);
            AttackEventsV2.RaiseAimShown(driver.UnitRef, preview.path);
        }

        PreviewData BuildPreview(Hex target, bool logInvalid)
        {
            if (!IsReady)
            {
                return new PreviewData
                {
                    valid = false,
                    rejectReason = AttackRejectReasonV2.NotReady,
                    rejectMessage = "Not ready."
                };
            }

            var layout = authoring.Layout;
            var unit = driver.UnitRef;
            var start = unit.Position;

            var rates = BuildMoveRates(start);
            var preview = new PreviewData
            {
                targetHex = target,
                rates = rates,
                mrClick = rates.mrClick,
                mrNoEnv = rates.mrNoEnv,
                attackSecsCharge = attackConfig ? Mathf.Max(0, attackConfig.baseTimeSeconds) : 0,
                attackEnergyCost = attackConfig ? Mathf.CeilToInt(attackConfig.baseEnergyCost * (1f + 0.5f * _attacksThisTurn)) : 0
            };

            if (!layout.Contains(target))
            {
                preview.valid = false;
                preview.rejectReason = AttackRejectReasonV2.NoPath;
                preview.rejectMessage = "Target out of board.";
                return preview;
            }
            if (env != null && env.IsPit(target))
            {
                preview.valid = false;
                preview.rejectReason = AttackRejectReasonV2.NoPath;
                preview.rejectMessage = "Target is pit.";
                return preview;
            }

            bool isEnemy = IsEnemyHex(target);
            preview.targetIsEnemy = isEnemy;

            List<Hex> path = null;
            Hex landing = target;

            if (isEnemy)
            {
                int range = attackConfig ? Mathf.Max(1, attackConfig.meleeRange) : 1;
                if (!TryFindMeleePath(start, target, range, out landing, out path))
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "No landing.";
                    return preview;
                }
            }
            else
            {
                if (_occ != null && _occ.IsBlocked(target, _actor))
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "Cell occupied.";
                    return preview;
                }

                path = ShortestPath(start, target, cell => IsBlockedForMove(cell, start, target));
                if (path == null)
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "No path.";
                    return preview;
                }
            }

            preview.landingHex = landing;
            preview.path = path;
            preview.steps = Mathf.Max(0, (path?.Count ?? 1) - 1);

            float mrClick = Mathf.Max(MR_MIN, rates.mrClick);
            int predSecs = preview.steps > 0 ? Mathf.CeilToInt(preview.steps / Mathf.Max(0.01f, mrClick)) : 0;
            int chargeSecs = predSecs;
            if (isEnemy)
            {
                float charge = Mathf.Max(0f, predSecs - 0.2f);
                chargeSecs = Mathf.CeilToInt(charge);
            }

            preview.moveSecsPred = predSecs;
            preview.moveSecsCharge = chargeSecs;
            preview.moveEnergyCost = Mathf.Max(0, chargeSecs) * MoveEnergyPerSecond();

            preview.valid = true;
            return preview;
        }

        PreviewData ClonePreview(PreviewData src)
        {
            if (src == null) return null;
            return new PreviewData
            {
                valid = src.valid,
                targetIsEnemy = src.targetIsEnemy,
                targetHex = src.targetHex,
                landingHex = src.landingHex,
                path = src.path != null ? new List<Hex>(src.path) : null,
                steps = src.steps,
                moveSecsPred = src.moveSecsPred,
                moveSecsCharge = src.moveSecsCharge,
                attackSecsCharge = src.attackSecsCharge,
                moveEnergyCost = src.moveEnergyCost,
                attackEnergyCost = src.attackEnergyCost,
                mrClick = src.mrClick,
                mrNoEnv = src.mrNoEnv,
                rates = src.rates,
                rejectReason = src.rejectReason,
                rejectMessage = src.rejectMessage
            };
        }

        IEnumerator RunAttack(
            PreviewData preview,
            bool attackPlanned,
            int moveSecsCharge,
            int moveEnergyPaid,
            int attackSecsCharge,
            int attackEnergyPaid)
        {
            if (preview == null || preview.path == null || preview.path.Count == 0)
                yield break;

            var layout = authoring.Layout;
            var unit = driver.UnitRef;
            Transform view = driver.unitView != null ? driver.unitView : transform;

            var path = preview.path;
            var start = path[0];

            if (view != null && path.Count >= 2)
            {
                var fromW = layout.World(path[0], y);
                var toW = layout.World(path[^1], y);
                float keep = attackConfig ? attackConfig.keepDeg : 45f;
                float turn = attackConfig ? attackConfig.turnDeg : 135f;
                float speed = attackConfig ? attackConfig.turnSpeedDegPerSec : 720f;
                var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW, keep, turn);
                yield return HexFacingUtil.RotateToYaw(view, yaw, speed);
                driver.UnitRef.Facing = nf;
                _actor.Facing = nf;
            }

            float mrNoEnv = preview.mrNoEnv;
            float refundThreshold = attackConfig ? Mathf.Max(0.01f, attackConfig.refundThresholdSeconds) : 0.8f;

            var sim = MoveSimulator.Run(
                path,
                mrNoEnv,
                preview.mrClick,
                moveSecsCharge,
                SampleStepModifier,
                refundThreshold,
                debugLog);

            var reached = sim.ReachedPath ?? new List<Hex>();
            int refundedSeconds = Mathf.Max(0, sim.RefundedSeconds);
            float usedSeconds = Mathf.Max(0f, sim.UsedSeconds);
            var stepRates = sim.StepEffectiveRates;

            AttackEventsV2.RaiseAttackMoveStarted(unit, reached);

            int moveEnergyRate = MoveEnergyPerSecond();

            if (reached.Count <= 1)
            {
                if (moveEnergyPaid > 0 && ManageEnergyLocally)
                    RefundMoveEnergy(moveEnergyPaid);
                if (ManageTurnTimeLocally)
                {
                    float refundMove = moveSecsCharge - usedSeconds + refundedSeconds;
                    if (refundMove > 0f)
                        _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + refundMove, 0f, MaxTurnSeconds);
                }
                AttackEventsV2.RaiseAttackMoveFinished(unit, unit.Position);

                if (attackPlanned)
                {
                    // （可选）面向目标；不需要可删
                    if (authoring?.Layout != null && driver?.unitView != null)
                    {
                        var fromW = authoring.Layout.World(unit.Position, y);
                        var toW = authoring.Layout.World(preview.targetHex, y);
                        float keep = attackConfig ? attackConfig.keepDeg : 45f;
                        float turn = attackConfig ? attackConfig.turnDeg : 135f;
                        float speed = attackConfig ? attackConfig.turnSpeedDegPerSec : 720f;
                        var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW, keep, turn);
                        yield return HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                        driver.UnitRef.Facing = nf;
                        _actor.Facing = nf;
                    }
                    TriggerAttackAnimation(unit, preview.targetHex);
                }
                int meleeAttackUsedSeconds = attackPlanned ? Mathf.Max(0, attackSecsCharge) : 0;
                int meleeMoveRefundSeconds = Mathf.Max(0, moveSecsCharge);
                _reportMoveUsedSeconds = 0;
                _reportMoveRefundSeconds = meleeMoveRefundSeconds;
                _reportAttackUsedSeconds = meleeAttackUsedSeconds;
                _reportAttackRefundSeconds = 0;
                ReportMoveEnergyNet = 0;
                ReportAttackEnergyNet = attackPlanned ? Mathf.Max(0, attackEnergyPaid) : 0;
                SetExecReport(meleeAttackUsedSeconds, meleeMoveRefundSeconds, attackPlanned);
                yield break;
            }

            bool truncated = reached.Count < path.Count;
            bool stoppedByExternal = false;
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

                AttackEventsV2.RaiseAttackMoveStep(unit, from, to, i, reached.Count - 1);

                float effMR = (stepRates != null && (i - 1) < stepRates.Count)
                    ? stepRates[i - 1]
                    : Mathf.Clamp(mrNoEnv, MR_MIN, MR_MAX);
                float stepDuration = Mathf.Max(minStepSeconds, 1f / Mathf.Max(MR_MIN, effMR));

                if (attackPlanned && !attackRolledBack && effMR + 1e-4f < preview.mrClick)
                {
                    //if (debugLog)
                    //    Debug.Log($"[Attack] rollback: effMR={effMR:F2} < MR_click={preview.mrClick:F2} at step i={i}", this);
                    attackRolledBack = true;
                    if (attackEnergyPaid > 0 && ManageEnergyLocally)
                        RefundAttackEnergy(attackEnergyPaid);
                    if (ManageTurnTimeLocally)
                        _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + attackSecsCharge, 0f, MaxTurnSeconds);
                    _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                    AttackEventsV2.RaiseMiss(unit, "Attack cancelled (slowed).");
                }

                var fromW = layout.World(from, y);
                var toW = layout.World(to, y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / stepDuration;
                    if (view != null)
                        view.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                _occ.TryMove(_actor, to);
                if (driver.Map != null)
                {
                    if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to);
                }
                unit.Position = to;
                driver.SyncView();

                if (_sticky != null && status != null && _sticky.TryGetSticky(to, out var mult, out var turns, out var tag))
                {
                    if (turns > 0 && !Mathf.Approximately(mult, 1f))
                    {
                        status.ApplyOrRefreshExclusive(tag, mult, turns, to.ToString());
                    }
                }
            }

            AttackEventsV2.RaiseAttackMoveFinished(unit, unit.Position);

            if (ManageTurnTimeLocally)
            {
                float refundMove = moveSecsCharge - usedSeconds + refundedSeconds;
                if (refundMove > 0f)
                    _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + refundMove, 0f, MaxTurnSeconds);
            }

            if (refundedSeconds > 0)
            {
                int refundEnergy = Mathf.Max(0, refundedSeconds * moveEnergyRate);
                if (ManageEnergyLocally && refundEnergy > 0)
                    RefundMoveEnergy(refundEnergy);
            }

            bool attackSuccess = attackPlanned && !attackRolledBack && !truncated && !stoppedByExternal;
            if (attackSuccess)
            {
                TriggerAttackAnimation(unit, preview.targetHex);
            }
            else if (attackPlanned)
            {
                if (!attackRolledBack)
                {
                    if (attackEnergyPaid > 0 && ManageEnergyLocally)
                        RefundAttackEnergy(attackEnergyPaid);
                    _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                    AttackEventsV2.RaiseMiss(unit, "Out of reach.");
                    if (ManageTurnTimeLocally)
                        _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + attackSecsCharge, 0f, MaxTurnSeconds);
                }
            }
            int moveUsedSeconds = Mathf.Max(0, Mathf.CeilToInt(usedSeconds));
            int moveRefundSeconds = Mathf.Max(0, refundedSeconds);
            int attackUsedSeconds = attackSuccess ? Mathf.Max(0, attackSecsCharge) : 0;
            int attackRefundSeconds = (attackPlanned && !attackSuccess) ? Mathf.Max(0, attackSecsCharge) : 0;
            _reportMoveUsedSeconds = moveUsedSeconds;
            _reportMoveRefundSeconds = moveRefundSeconds;
            _reportAttackUsedSeconds = attackUsedSeconds;
            _reportAttackRefundSeconds = attackRefundSeconds;

            int netMoveSeconds = Mathf.Max(0, moveSecsCharge - moveRefundSeconds);
            ReportMoveEnergyNet = Mathf.Max(0, netMoveSeconds * moveEnergyRate);
            ReportAttackEnergyNet = attackSuccess ? Mathf.Max(0, attackEnergyPaid) : 0;

            SetExecReport(
                moveUsedSeconds + attackUsedSeconds,
                moveRefundSeconds + attackRefundSeconds,
                attackSuccess);
        }
        int IActionExecReportV2.UsedSeconds => _reportPending ? _reportUsedSeconds : 0;
        int IActionExecReportV2.RefundedSeconds => _reportPending ? _reportRefundedSeconds : 0;

        void IActionExecReportV2.Consume()
        {
            ClearExecReport();
        }

        public int ReportComboBaseCount => _reportComboBaseCount;

        public int ReportMoveUsedSeconds => _reportPending ? _reportMoveUsedSeconds : 0;
        public int ReportMoveRefundSeconds => _reportPending ? _reportMoveRefundSeconds : 0;
        public int ReportAttackUsedSeconds => _reportPending ? _reportAttackUsedSeconds : 0;
        public int ReportAttackRefundSeconds => _reportPending ? _reportAttackRefundSeconds : 0;


        MoveRatesSnapshot BuildMoveRates(Hex start)
        {
            int baseRate = ctx != null ? Mathf.Max(1, ctx.BaseMoveRate) : GetFallbackBaseRate();
            baseRate = Mathf.Clamp(baseRate, (int)MR_MIN, (int)MR_MAX);

            float buffMult = 1f;
            int flatAfter = 0;
            if (ctx != null)
            {
                buffMult = 1f + Mathf.Max(-0.99f, ctx.MoveRatePctAdd);
                flatAfter = ctx.MoveRateFlatAdd;
            }

            float stickyMult = status != null ? status.GetProduct() : 1f;

            var startSample = SampleStepModifier(start);
            float startEnv = Mathf.Clamp(startSample.Multiplier <= 0f ? 1f : startSample.Multiplier, ENV_MIN, ENV_MAX);
            bool startIsSticky = startSample.Sticky;

            float mrNoEnv = Mathf.Clamp(baseRate * buffMult * stickyMult + flatAfter, MR_MIN, MR_MAX);
            float startUse = startIsSticky ? 1f : startEnv;
            startUse = Mathf.Clamp(startUse, ENV_MIN, ENV_MAX);
            float mrClick = Mathf.Clamp(mrNoEnv * startUse, MR_MIN, MR_MAX);

            return new MoveRatesSnapshot
            {
                baseRate = baseRate,
                buffMult = buffMult,
                stickyMult = stickyMult,
                flatAfter = flatAfter,
                startEnvMult = startEnv,
                mrNoEnv = mrNoEnv,
                mrClick = mrClick,
                startIsSticky = startIsSticky
            };
        }


        MoveSimulator.StickySample SampleStepModifier(Hex hex)
        {
            float mult = 1f;
            bool sticky = false;
            bool hasStickySource = false;

            if (_sticky != null && _sticky.TryGetSticky(hex, out var stickM, out var stickTurns, out var tag))
            {
                if (!Mathf.Approximately(stickM, 1f))
                {
                    mult *= stickM;
                    hasStickySource = true;
                    sticky = stickTurns > 0;
                }
            }

            if (!hasStickySource && env != null)
            {
                float envMult = Mathf.Clamp(env.GetSpeedMult(hex), ENV_MIN, ENV_MAX);
                if (!Mathf.Approximately(envMult, 1f))
                {
                    mult *= envMult;
                }
            }
            bool isSticky = sticky && !Mathf.Approximately(mult, 1f);
            return new MoveSimulator.StickySample(mult, isSticky);
        }

        int GetFallbackBaseRate()
        {
            if (ctx != null) return ctx.BaseMoveRate;
            return 3;
        }

        int MoveEnergyPerSecond() => moveConfig != null ? Mathf.Max(0, moveConfig.energyCost) : 0;

        bool IsEnemyHex(Hex hex)
        {
            if (_enemyLocator != null && _enemyLocator.IsEnemy(hex))
                return true;
            if (_occ != null)
            {
                var actor = _occ.Get(hex);
                if (actor != null && actor != _actor)
                    return true;
            }
            return false;
        }

        bool IsBlockedForMove(Hex cell, Hex start, Hex landing)
        {
            if (authoring?.Layout == null) return true;
            if (!authoring.Layout.Contains(cell)) return true;
            if (env != null && env.IsPit(cell)) return true;
            if (cell.Equals(start)) return false;
            if (cell.Equals(landing)) return false;
            return _occ != null && _occ.IsBlocked(cell, _actor);
        }

        bool TryFindMeleePath(Hex start, Hex target, int range, out Hex landing, out List<Hex> bestPath)
        {
            landing = target;
            bestPath = null;

            var enemyCells = GetEnemyCellsForTarget(target);
            int meleeRange = Mathf.Max(1, range);

            foreach (var cell in enemyCells)
            {
                if (Hex.Distance(start, cell) <= meleeRange)
                {
                    landing = start;
                    bestPath = new List<Hex> { start };
                    return true;
                }
            }

            var candidates = new HashSet<Hex>();
            int bestEnemyDist = int.MaxValue;
            int bestLen = int.MaxValue;
            Hex bestLanding = target;
            foreach (var cell in enemyCells)
            {
                for (int dist = 1; dist <= meleeRange; dist++)
                {
                    foreach (var candidate in Hex.Ring(cell, dist))
                    {
                        if (!candidates.Add(candidate)) continue;
                        if (!authoring.Layout.Contains(candidate)) continue;
                        if (env != null && env.IsPit(candidate)) continue;
                        if (_occ != null && !_occ.CanPlace(_actor, candidate, _actor.Facing, ignore: _actor)) continue;

                        var path = ShortestPath(start, candidate, c => IsBlockedForMove(c, start, candidate));
                        if (path == null) continue;

                        int enemyDist = DistanceToEnemy(candidate, enemyCells);
                        if (enemyDist > meleeRange) continue;

                        int len = path.Count;
                        if (enemyDist < bestEnemyDist || (enemyDist == bestEnemyDist && len < bestLen))
                        {
                            bestEnemyDist = enemyDist;
                            bestLen = len;
                            bestPath = path;
                            bestLanding = candidate;
                        }
                    }
                }
            }

            if (bestPath != null)
            {
                landing = bestLanding;
                return true;
            }

            landing = target;
            bestPath = null;
            return false;
        }

        static int DistanceToEnemy(Hex candidate, IReadOnlyList<Hex> enemyCells)
        {
            int best = int.MaxValue;
            if (enemyCells == null) return best;
            for (int i = 0; i < enemyCells.Count; i++)
            {
                int dist = Hex.Distance(candidate, enemyCells[i]);
                if (dist < best)
                    best = dist;
            }

            return best;
        }

        IReadOnlyList<Hex> GetEnemyCellsForTarget(Hex target)
        {
            if (_occ == null)
                return new[] { target };

            var enemy = _occ.Get(target);
            if (enemy == null || enemy == _actor)
                return new[] { target };

            var cells = _occ.CellsOf(enemy);
            if (cells != null && cells.Count > 0)
                return cells;

            return new[] { target };
        }

        static readonly Hex[] Neigh =
        {
            new Hex(+1, 0),
            new Hex(+1, -1),
            new Hex(0, -1),
            new Hex(-1, 0),
            new Hex(-1, +1),
            new Hex(0, +1)
        };

        List<Hex> ShortestPath(Hex start, Hex goal, System.Func<Hex, bool> isBlocked)
        {
            var came = new Dictionary<Hex, Hex>();
            var q = new Queue<Hex>();
            q.Enqueue(start);
            came[start] = start;

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(goal)) break;
                for (int i = 0; i < Neigh.Length; i++)
                {
                    var nb = new Hex(cur.q + Neigh[i].q, cur.r + Neigh[i].r);
                    if (came.ContainsKey(nb)) continue;
                    if (isBlocked != null && isBlocked(nb)) continue;
                    came[nb] = cur;
                    q.Enqueue(nb);
                }
            }

            if (!came.ContainsKey(goal)) return null;
            var path = new List<Hex> { goal };
            var c = goal;
            while (!c.Equals(start))
            {
                c = came[c];
                path.Add(c);
            }
            path.Reverse();
            return path;
        }

        void SpendEnergy(int amount)
        {
            if (amount <= 0) return;
            var stats = ctx != null ? ctx.stats : null;
            if (stats == null) return;
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy - amount, 0, stats.MaxEnergy);
        }

        void RefundMoveEnergy(int amount)
        {
            if (amount <= 0) return;
            var stats = ctx != null ? ctx.stats : null;
            if (stats == null) return;
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy + amount, 0, stats.MaxEnergy);
        }

        void RefundAttackEnergy(int amount)
        {
            if (amount <= 0) return;
            var stats = ctx != null ? ctx.stats : null;
            if (stats == null) return;
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy + amount, 0, stats.MaxEnergy);
            if (debugLog)
                Debug.Log($"[Attack] Refund attack energy +{amount} ({before}->{stats.Energy})", this);
        }

        static FootprintShape CreateSingleFallback()
        {
            var s = ScriptableObject.CreateInstance<FootprintShape>();
            s.name = "Footprint_Single_Runtime";
            s.offsets = new() { new L2(0, 0) };
            return s;
        }
        void OnAttackStrikeFired(Unit unit, int comboIndex)
        {
            if (!_pendingAttack.active || _pendingAttack.unit != unit)
                return;
            if (_pendingAttack.strikeProcessed)
                return;
            if (_pendingAttack.comboIndex > 0 && comboIndex > 0 && comboIndex != _pendingAttack.comboIndex)
                return;
            if (!IsEnemyHex(_pendingAttack.target))
            {
                ClearPendingAttack();
                return;
            }

            AttackEventsV2.RaiseHit(unit, _pendingAttack.target);
            _pendingAttack.strikeProcessed = true;
        }

        void OnAttackAnimationEnded(Unit unit, int comboIndex)
        {
            if (_pendingAttack.unit != unit)
                return;
            if (_pendingAttack.comboIndex > 0 && comboIndex > 0 && comboIndex != _pendingAttack.comboIndex)
                return;

            ClearPendingAttack();
        }

        void ClearPendingAttack()
        {
            _pendingAttack.active = false;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = null;
            _pendingAttack.target = default;
            _pendingAttack.comboIndex = 0;
        }

        int ResolveComboIndex()
        {
            return Mathf.Clamp(Mathf.Max(1, _attacksThisTurn), 1, 4);
        }

        void TriggerAttackAnimation(Unit unit, Hex target)
        {
            if (unit == null)
                return;

            int comboIndex = ResolveComboIndex();
            ClearPendingAttack();
            _pendingAttack.active = true;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = unit;
            _pendingAttack.target = target;
            _pendingAttack.comboIndex = comboIndex;

            AttackEventsV2.RaiseAttackAnimation(unit, comboIndex);
        }
    }
}
