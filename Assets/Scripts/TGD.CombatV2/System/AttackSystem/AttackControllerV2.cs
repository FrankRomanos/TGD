// File: TGD.CombatV2/AttackControllerV2.cs
using System.Collections;
using System.Collections.Generic;
using TGD.CombatV2.Integration;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.HexBoard;
using TGD.HexBoard.Path;
using UnityEngine;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerOccupancyBridge))]
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
        public HexEnvironmentSystem env;
        public DefaultTargetValidator targetValidator;
        public HexOccupancyService occupancyService;

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
        IActorOccupancyBridge _bridge;
        IStickyMoveSource _sticky;
        IEnemyLocator _enemyLocator;

        TargetingSpec _attackSpec;

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
        int _reportEnergyMoveNet;
        int _reportEnergyAtkNet;
        bool _reportFreeMove;
        bool _reportPending;
        int _reportComboBaseCount;
        int _pendingComboBaseCount;
        readonly HashSet<Hex> _tempReservedThisAction = new();

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
            _reportEnergyMoveNet = 0;
            _reportEnergyAtkNet = 0;
            _reportFreeMove = false;
            _reportPending = false;
            _reportComboBaseCount = 0;
            _pendingComboBaseCount = 0;
        }

        void SetExecReport(int usedSeconds, int refundedSeconds, int energyMoveNet, int energyAtkNet, bool attackExecuted, bool freeMove)
        {
            _reportUsedSeconds = Mathf.Max(0, usedSeconds);
            _reportRefundedSeconds = Mathf.Max(0, refundedSeconds);
            _reportEnergyMoveNet = energyMoveNet;
            _reportEnergyAtkNet = energyAtkNet;
            _reportFreeMove = freeMove;
            _reportComboBaseCount = attackExecuted ? Mathf.Max(0, _pendingComboBaseCount) : 0;
            _reportPending = true;
            _pendingComboBaseCount = 0;
            LogAttackSummary();
        }
        void LogAttackSummary()
        {
            var unit = driver != null ? driver.UnitRef : null;
            string label = TurnManagerV2.FormatUnitLabel(unit);
            string suffix = _reportFreeMove ? " (FreeMove)" : string.Empty;
            Debug.Log($"[Attack] Use moveSecs={_reportMoveUsedSeconds} atkSecs={_reportAttackUsedSeconds} energyMove={_reportEnergyMoveNet} energyAtk={_reportEnergyAtkNet} U={label}{suffix}", this);
        }

        float MaxTurnSeconds => Mathf.Max(0f, baseTurnSeconds + (ctx ? ctx.Speed : 0));

        public void AttachTurnManager(TurnManagerV2 manager)
        {
            if (_boundTurnManager != null)
            {
                _boundTurnManager.TurnStarted -= OnTurnStarted;
                _boundTurnManager.SideEnded -= OnSideEnded;
            }

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
            turnManager.SideEnded += OnSideEnded;
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
        public struct PlannedAttackCost
        {
            public int moveSecs;
            public int moveEnergy;
            public int atkSecs;
            public int atkEnergy;
            public bool valid;
        }

        public PlannedAttackCost PeekPlannedCost(Hex target)
        {
            int fallbackMoveSecs = Mathf.Max(1, Mathf.CeilToInt(moveConfig ? moveConfig.timeCostSeconds : 1f));
            int moveEnergyRate = moveConfig ? Mathf.Max(0, moveConfig.energyCost) : 0;
            int fallbackAtkSecs = attackConfig ? Mathf.Max(0, attackConfig.baseTimeSeconds) : 0;
            int fallbackAtkEnergy = attackConfig ? Mathf.Max(0, attackConfig.baseEnergyCost) : 0;

            var result = new PlannedAttackCost
            {
                moveSecs = fallbackMoveSecs,
                moveEnergy = moveEnergyRate * fallbackMoveSecs,
                atkSecs = fallbackAtkSecs,
                atkEnergy = fallbackAtkEnergy,
                valid = false
            };

            PreviewData preview = null;
            if (_currentPreview != null && _currentPreview.valid && _currentPreview.targetHex.Equals(target))
            {
                preview = _currentPreview;
            }
            else
            {
                preview = BuildPreview(target, true);
            }

            if (preview != null && preview.valid)
            {
                result.valid = true;
                result.moveSecs = Mathf.Max(0, preview.moveSecsCharge);
                result.moveEnergy = Mathf.Max(0, preview.moveEnergyCost);
                if (preview.targetIsEnemy)
                {
                    result.atkSecs = Mathf.Max(0, preview.attackSecsCharge);
                    result.atkEnergy = Mathf.Max(0, preview.attackEnergyCost);
                }
                else
                {
                    result.atkSecs = 0;
                    result.atkEnergy = 0;
                }
            }

            return result;
        }

        public (int moveSecs, int atkSecs, int energyMove, int energyAtk) GetPlannedCost()
        {
            int moveSecs = Mathf.Max(1, Mathf.CeilToInt(moveConfig ? moveConfig.timeCostSeconds : 1f));
            int moveEnergy = moveSecs * (moveConfig ? Mathf.Max(0, moveConfig.energyCost) : 0);
            int atkSecs = Mathf.Max(0, Mathf.CeilToInt(attackConfig ? attackConfig.baseTimeSeconds : 0f));
            int atkEnergy = attackConfig ? Mathf.Max(0, attackConfig.baseEnergyCost) : 0;
            return (moveSecs, atkSecs, moveEnergy, atkEnergy);
        }

        public int ReportUsedSeconds => _reportPending ? _reportUsedSeconds : 0;
        public int ReportRefundedSeconds => _reportPending ? _reportRefundedSeconds : 0;
        public int ReportEnergyMoveNet => _reportPending ? _reportEnergyMoveNet : 0;
        public int ReportEnergyAtkNet => _reportPending ? _reportEnergyAtkNet : 0;
        public int ReportMoveEnergyNet => ReportEnergyMoveNet;
        public int ReportAttackEnergyNet => ReportEnergyAtkNet;
        public bool ReportFreeMoveApplied => _reportPending && _reportFreeMove;
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
            public PlanKind plan;
            public TargetCheckResult targetCheck;
            public AttackRejectReasonV2 rejectReason;
            public string rejectMessage;
        }

        IGridActor SelfActor => _bridge?.Actor as IGridActor;

        Hex CurrentAnchor
        {
            get
            {
                if (_bridge != null)
                    return _bridge.CurrentAnchor;
                if (SelfActor != null)
                    return SelfActor.Anchor;
                return driver != null && driver.UnitRef != null ? driver.UnitRef.Position : Hex.Zero;
            }
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

            _bridge = GetComponent<IActorOccupancyBridge>();

            if (!targetValidator)
                targetValidator = GetComponent<DefaultTargetValidator>() ?? GetComponentInParent<DefaultTargetValidator>(true);

            if (!occupancyService)
                occupancyService = GetComponent<HexOccupancyService>() ?? GetComponentInParent<HexOccupancyService>(true);

            if (!occupancyService && driver != null)
            {
                occupancyService = driver.GetComponentInParent<HexOccupancyService>(true);
                if (!occupancyService && driver.authoring != null)
                    occupancyService = driver.authoring.GetComponent<HexOccupancyService>() ?? driver.authoring.GetComponentInParent<HexOccupancyService>(true);
            }

            if (occupancyService == null && _bridge is PlayerOccupancyBridge concreteBridge && concreteBridge.occupancyService)
                occupancyService = concreteBridge.occupancyService;

            _attackSpec = new TargetingSpec
            {
                occupant = TargetOccupantMask.Enemy | TargetOccupantMask.Empty,
                terrain = TargetTerrainMask.Any,
                allowSelf = false,
                requireOccupied = false,
                requireEmpty = false,
                maxRangeHexes = -1
            };
        }

        void OnEnable()
        {
            ClearPendingAttack();
            AttackEventsV2.AttackStrikeFired += OnAttackStrikeFired;
            AttackEventsV2.AttackAnimationEnded += OnAttackAnimationEnded;
            AttackEventsV2.AttackMoveFinished += OnAttackMoveFinished;
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

            if (_bridge == null)
                _bridge = GetComponent<IActorOccupancyBridge>();

            if (occupancyService)
                _occ = occupancyService.Get();
            else if (_bridge is PlayerOccupancyBridge concreteBridge && concreteBridge.occupancyService)
            {
                occupancyService = concreteBridge.occupancyService;
                _occ = occupancyService ? occupancyService.Get() : null;
            }

            _bridge?.EnsurePlacedNow();
        }

        void OnDisable()
        {
            AttackEventsV2.AttackStrikeFired -= OnAttackStrikeFired;
            AttackEventsV2.AttackAnimationEnded -= OnAttackAnimationEnded;
            AttackEventsV2.AttackMoveFinished -= OnAttackMoveFinished;
            if (_boundTurnManager != null)
            {
                _boundTurnManager.TurnStarted -= OnTurnStarted;
                _boundTurnManager.SideEnded -= OnSideEnded;
                _boundTurnManager = null;
            }
            ClearPendingAttack();
            ClearTempReservations("OnDisable");
            _painter?.Clear();
            _currentPreview = null;
            _hover = null;
            _occ = null;
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

        bool IsReady => authoring?.Layout != null && driver != null && driver.IsReady && _occ != null && _bridge != null && SelfActor != null;

        void RaiseRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            AttackEventsV2.RaiseRejected(unit, reason, message);
        }

        public bool TryPrecheckAim(out string reason, bool raiseHud = true)
        {
            EnsureTurnTimeInited();
            if (!IsReady)
            {
                if (raiseHud)
                    RaiseRejected(driver ? driver.UnitRef : null, AttackRejectReasonV2.NotReady, "Not ready.");
                reason = "(not-ready)";
                return false;
            }

            if (ctx != null && ctx.Entangled)
            {
                if (raiseHud)
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "Can't move while entangled.");
                reason = "(entangled)";
                return false;
            }
            int needSec = Mathf.Max(1, Mathf.CeilToInt(moveConfig ? moveConfig.timeCostSeconds : 1f));
            if (UseTurnManager && turnManager != null && driver != null && driver.UnitRef != null)
            {
                var budget = turnManager.GetBudget(driver.UnitRef);
                if (budget == null || !budget.HasTime(needSec))
                {
                    if (raiseHud)
                        RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "No more time.");
                    reason = "(no-time)";
                    return false;
                }
            }
            else if (ManageTurnTimeLocally && _turnSecondsLeft + 1e-4f < needSec)
            {
                if (raiseHud)
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "No more time.");
                reason = "(no-time)";
                return false;
            }

            var pool = ResolveResourcePool();
            int energyAvailable = ResolveEnergyAvailable(pool);
            int moveEnergyCost = MoveEnergyPerSecond();
            bool anyEnergyCost = (moveEnergyCost > 0) || (attackConfig != null && attackConfig.baseEnergyCost > 0f);
            if (anyEnergyCost && energyAvailable <= 0)
            {
                if (raiseHud)
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                reason = "(no-energy)";
                return false;
            }
            if (moveEnergyCost > 0 && energyAvailable < moveEnergyCost)
            {
                if (raiseHud)
                    RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                reason = "(no-energy)";
                return false;
            }
            reason = null;
            return true;
        }

        public void OnEnterAim()
        {
            if (!TryPrecheckAim(out _))
                return;

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

            _bridge?.EnsurePlacedNow();
            if (occupancyService)
                _occ = occupancyService.Get();

            var unit = driver != null ? driver.UnitRef : null;
            var targetCheck = ValidateAttackTarget(unit, hex);
            if (debugLog)
                Debug.Log($"[Action][Attack] Click {hex} ¡ú {targetCheck}", this);

            if (!targetCheck.ok)
            {
                RaiseTargetRejected(unit, targetCheck.reason);
                yield break;
            }

            if (targetCheck.plan != PlanKind.MoveOnly && targetCheck.plan != PlanKind.MoveAndAttack)
            {
                RaiseTargetRejected(unit, TargetInvalidReason.Unknown);
                yield break;
            }

            var preview = BuildPreview(hex, true, targetCheck);

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

            if (!UseTurnManager && ManageEnergyLocally)
                SpendEnergy(moveEnergyCost);
            int attackEnergySpent = 0;
            if (attackPlanned)
            {
                if (!UseTurnManager && ManageEnergyLocally && attackEnergyCost > 0)
                    SpendEnergy(attackEnergyCost);
                attackEnergySpent = Mathf.Max(0, attackEnergyCost);
                _attacksThisTurn = Mathf.Max(0, _attacksThisTurn + 1);
            }

            if (!UseTurnManager && ManageTurnTimeLocally)
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

        TargetCheckResult ValidateAttackTarget(Unit unit, Hex hex)
        {
            if (targetValidator == null || _attackSpec == null || unit == null)
                return new TargetCheckResult { ok = false, reason = TargetInvalidReason.Unknown, hit = HitKind.None, plan = PlanKind.None };
            return targetValidator.Check(unit, hex, _attackSpec);
        }

        void RaiseTargetRejected(Unit unit, TargetInvalidReason reason)
        {
            var (mapped, message) = MapAttackReject(reason);
            if (debugLog)
                Debug.Log($"[Action][Attack] Reject {reason}", this);
            RaiseRejected(unit, mapped, message);
        }

        static (AttackRejectReasonV2 reason, string message) MapAttackReject(TargetInvalidReason reason)
        {
            switch (reason)
            {
                case TargetInvalidReason.Self:
                    return (AttackRejectReasonV2.NoPath, "Self target not allowed.");
                case TargetInvalidReason.Friendly:
                    return (AttackRejectReasonV2.NoPath, "Cannot attack ally.");
                case TargetInvalidReason.EnemyNotAllowed:
                    return (AttackRejectReasonV2.NoPath, "Enemy not allowed.");
                case TargetInvalidReason.EmptyNotAllowed:
                    return (AttackRejectReasonV2.NoPath, "Target must be occupied.");
                case TargetInvalidReason.Blocked:
                    return (AttackRejectReasonV2.NoPath, "Blocked by obstacle.");
                case TargetInvalidReason.OutOfRange:
                    return (AttackRejectReasonV2.CantMove, "Out of range.");
                case TargetInvalidReason.None:
                    return (AttackRejectReasonV2.NoPath, "Invalid target.");
                default:
                    return (AttackRejectReasonV2.NoPath, "Invalid target.");
            }
        }

        PreviewData BuildPreview(Hex target, bool logInvalid, TargetCheckResult? overrideCheck = null)
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
                attackSecsCharge = attackConfig ? Mathf.Max(0, attackConfig.baseTimeSeconds) : 0
            };

            if (layout != null && !layout.Contains(target))
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

            var check = overrideCheck ?? ValidateAttackTarget(unit, target);
            preview.targetCheck = check;
            preview.plan = check.plan;

            if (!check.ok)
            {
                var (reject, message) = MapAttackReject(check.reason);
                preview.valid = false;
                preview.rejectReason = reject;
                preview.rejectMessage = message;
                if (logInvalid && debugLog)
                    Debug.Log($"[Action][Attack] BuildPreview reject {message}", this);
                return preview;
            }

            bool treatAsEnemy = check.plan == PlanKind.MoveAndAttack;
            bool treatAsMoveOnly = check.plan == PlanKind.MoveOnly;
            if (!treatAsEnemy && !treatAsMoveOnly)
            {
                preview.valid = false;
                preview.rejectReason = AttackRejectReasonV2.NoPath;
                preview.rejectMessage = "Unsupported plan.";
                return preview;
            }

            preview.targetIsEnemy = treatAsEnemy;

            _bridge?.EnsurePlacedNow();
            if (occupancyService)
                _occ = occupancyService.Get();

            var passability = PassabilityFactory.ForApproach(_occ, SelfActor, CurrentAnchor);
            List<Hex> path = null;
            Hex landing = target;

            if (treatAsEnemy)
            {
                int range = attackConfig ? Mathf.Max(1, attackConfig.meleeRange) : 1;
                if (!TryFindMeleePath(start, target, range, passability, out landing, out path))
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "No landing.";
                    return preview;
                }
            }
            else
            {
                if ((passability != null && passability.IsBlocked(target)) || (passability == null && _occ != null && _occ.IsBlocked(target, SelfActor)))
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "Cell occupied.";
                    return preview;
                }

                path = ShortestPath(start, target, cell => IsBlockedForMove(cell, start, target, passability));
                if (path == null)
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "No path.";
                    return preview;
                }
            }

            if (!treatAsEnemy)
            {
                preview.attackSecsCharge = 0;
                preview.attackEnergyCost = 0;
            }

            preview.landingHex = landing;
            preview.path = path;
            preview.steps = Mathf.Max(0, (path?.Count ?? 1) - 1);

            float mrClick = Mathf.Max(MR_MIN, rates.mrClick);
            int predSecs = preview.steps > 0 ? Mathf.CeilToInt(preview.steps / Mathf.Max(0.01f, mrClick)) : 0;
            int chargeSecs = predSecs;
            if (treatAsEnemy)
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
                plan = src.plan,
                targetCheck = src.targetCheck,
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

            ClearTempReservations("PreAction");

            var layout = authoring.Layout;
            var unit = driver.UnitRef;
            Transform view = driver.unitView != null ? driver.unitView : transform;

            var path = preview.path;
            _bridge?.EnsurePlacedNow();
            if (occupancyService)
                _occ = occupancyService.Get();
            var passability = PassabilityFactory.ForApproach(_occ, SelfActor, CurrentAnchor);
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
                if (SelfActor != null) SelfActor.Facing = nf;
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
            int moveEnergyRate = MoveEnergyPerSecond();

            try
            {
                if (reached.Count > 1)
                {
                    for (int i = 1; i < reached.Count; i++)
                        ReserveTemp(reached[i]);
                }

                AttackEventsV2.RaiseAttackMoveStarted(unit, reached);

                if (reached.Count <= 1)
                {
                    if (moveEnergyPaid > 0 && !UseTurnManager && ManageEnergyLocally)
                        RefundMoveEnergy(moveEnergyPaid);
                    if (!UseTurnManager && ManageTurnTimeLocally)
                    {
                        float refundMove = moveSecsCharge - usedSeconds + refundedSeconds;
                        if (refundMove > 0f)
                            _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + refundMove, 0f, MaxTurnSeconds);
                    }
                    AttackEventsV2.RaiseAttackMoveFinished(unit, unit.Position);

                    if (attackPlanned)
                    {
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
                            if (SelfActor != null) SelfActor.Facing = nf;
                        }
                        TriggerAttackAnimation(unit, preview.targetHex);
                    }
                    int meleeAttackUsedSeconds = attackPlanned ? Mathf.Max(0, attackSecsCharge) : 0;
                    int meleeMoveRefundSeconds = Mathf.Max(0, moveSecsCharge);
                    _reportMoveUsedSeconds = 0;
                    _reportMoveRefundSeconds = meleeMoveRefundSeconds;
                    _reportAttackUsedSeconds = meleeAttackUsedSeconds;
                    _reportAttackRefundSeconds = 0;
                    int meleeMoveEnergyNet = 0;
                    int meleeAttackEnergyNet = attackPlanned ? Mathf.Max(0, attackEnergyPaid) : 0;
                    SetExecReport(
                        meleeAttackUsedSeconds,
                        meleeMoveRefundSeconds,
                        meleeMoveEnergyNet,
                        meleeAttackEnergyNet,
                        attackPlanned,
                        false);
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

                    if (passability != null && passability.IsBlocked(to))
                    {
                        stoppedByExternal = true;
                        break;
                    }
                    if (passability == null && _occ != null && _occ.IsBlocked(to, SelfActor))
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
                        attackRolledBack = true;
                        if (attackEnergyPaid > 0 && !UseTurnManager && ManageEnergyLocally)
                            RefundAttackEnergy(attackEnergyPaid);
                        if (!UseTurnManager && ManageTurnTimeLocally)
                            _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + attackSecsCharge, 0f, MaxTurnSeconds);
                        _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                        AttackEventsV2.RaiseMiss(unit, "Attack cancelled (slowed).");
                    }

                    var fromW = layout.World(from, y);
                    var toW = layout.World(to, y);
                    string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                    float t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime / stepDuration;
                        if (view != null)
                            view.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                        yield return null;
                    }

                    _bridge?.MoveCommit(to, SelfActor != null ? SelfActor.Facing : driver.UnitRef.Facing);
                    if (driver.Map != null)
                    {
                        if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to);
                    }
                    unit.Position = to;
                    driver.SyncView();
                    _tempReservedThisAction.Add(to);

                    if (_sticky != null && status != null &&
           _sticky.TryGetSticky(to, out var mult, out var turns, out var tag) &&
           turns > 0 && !Mathf.Approximately(mult, 1f))
                    {
                        status.ApplyOrRefreshExclusive(tag, mult, turns, to.ToString());
                        Debug.Log($"[Sticky] Apply U={unitLabel} tag={tag}@{to} mult={mult:F2} turns={turns}", this);
                    }
                }

                if (driver != null && driver.UnitRef != null)
                {
                    _bridge?.MoveCommit(driver.UnitRef.Position, driver.UnitRef.Facing);
                }

                AttackEventsV2.RaiseAttackMoveFinished(unit, unit.Position);

                if (!UseTurnManager && ManageTurnTimeLocally)
                {
                    float refundMove = moveSecsCharge - usedSeconds + refundedSeconds;
                    if (refundMove > 0f)
                        _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + refundMove, 0f, MaxTurnSeconds);
                }

                if (refundedSeconds > 0)
                {
                    int refundEnergy = Mathf.Max(0, refundedSeconds * moveEnergyRate);
                    if (!UseTurnManager && ManageEnergyLocally && refundEnergy > 0)
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
                        if (attackEnergyPaid > 0 && !UseTurnManager && ManageEnergyLocally)
                            RefundAttackEnergy(attackEnergyPaid);
                        _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                        AttackEventsV2.RaiseMiss(unit, "Out of reach.");
                        if (!UseTurnManager && ManageTurnTimeLocally)
                            _turnSecondsLeft = Mathf.Clamp(_turnSecondsLeft + attackSecsCharge, 0f, MaxTurnSeconds);
                    }
                }
                float cutoff = attackConfig ? Mathf.Max(0f, attackConfig.freeMoveCutoffSeconds) : 0.2f;
                bool isMelee = attackPlanned;
                bool canFree = isMelee && moveSecsCharge >= 1;
                bool freeMoveApplied = false;

                if (canFree && reached != null && reached.Count >= 2 && usedSeconds < cutoff)
                {
                    int forceFree = 1;
                    refundedSeconds = Mathf.Max(refundedSeconds, forceFree);
                    freeMoveApplied = true;

                    if (debugLog)
                    {
                        string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                        Debug.Log($"[Attack] FreeMove1s U={unitLabel} used={usedSeconds:F2}s (<{cutoff:F2})", this);
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

                int netMoveSeconds = moveSecsCharge - moveRefundSeconds;
                int moveEnergyNet = netMoveSeconds * moveEnergyRate;
                int attackEnergyNet = attackSuccess ? Mathf.Max(0, attackEnergyPaid) : 0;

                SetExecReport(
                    moveUsedSeconds + attackUsedSeconds,
                    moveRefundSeconds + attackRefundSeconds,
                    moveEnergyNet,
                    attackEnergyNet,
                    attackSuccess,
                    freeMoveApplied);
            }
            finally
            {
                ClearTempReservations("ActionEnd");
            }
        }
        int IActionExecReportV2.UsedSeconds => ReportUsedSeconds;
        int IActionExecReportV2.RefundedSeconds => ReportRefundedSeconds;

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
            if (_enemyLocator is SimpleEnemyRegistry registry)
            {
                if (registry.IsEnemyAt(hex, _occ))
                    return true;
                if (_occ != null && _occ.TryGetActor(hex, out var actorAt) && registry.IsEnemyActor(actorAt))
                    return true;
                return false;
            }
            if (_enemyLocator != null && _enemyLocator.IsEnemy(hex))
                return true;

            if (_occ != null && _occ.TryGetActor(hex, out var actor) && actor != null && actor != SelfActor)
                return true;

            return false;
        }

        bool IsBlockedForMove(Hex cell, Hex start, Hex landing, IPassability passability = null)
        {
            if (authoring?.Layout == null) return true;
            if (!authoring.Layout.Contains(cell)) return true;
            if (env != null && env.IsPit(cell)) return true;
            if (cell.Equals(start)) return false;
            if (cell.Equals(landing)) return false;
            if (_tempReservedThisAction.Contains(cell)) return true;
            if (passability != null && passability.IsBlocked(cell)) return true;
            if (passability == null && _occ != null && SelfActor != null)
            {
                if (!_occ.CanPlaceIgnoringTemp(SelfActor, cell, SelfActor.Facing, ignore: SelfActor))
                    return true;
            }
            return false;
        }

        bool TryFindMeleePath(Hex start, Hex target, int range, IPassability passability, out Hex landing, out List<Hex> bestPath)
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
                        if (passability != null && passability.IsBlocked(candidate)) continue;
                        if (passability == null && _occ != null && SelfActor != null && !_occ.CanPlaceIgnoringTemp(SelfActor, candidate, SelfActor.Facing, ignore: SelfActor)) continue;

                        var path = ShortestPath(start, candidate, c => IsBlockedForMove(c, start, candidate, passability));
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
            if (enemy == null || enemy == SelfActor)
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
        void OnAttackMoveFinished(Unit unit, Hex end)
        {
            if (!IsMyUnit(unit))
                return;
            ClearTempReservations("AttackMoveFinished", true);
        }

        void OnSideEnded(bool isPlayerSide)
        {
            if (!UseTurnManager || turnManager == null || driver == null)
                return;

            var unit = driver.UnitRef;
            if (unit == null)
                return;

            bool belongs = isPlayerSide ? turnManager.IsPlayerUnit(unit) : turnManager.IsEnemyUnit(unit);
            if (!belongs)
                return;

            ClearTempReservations("SideEnd", true);
        }
        void ClearPendingAttack()
        {
            _pendingAttack.active = false;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = null;
            _pendingAttack.target = default;
            _pendingAttack.comboIndex = 0;
        }
        bool IsMyUnit(Unit unit) => driver != null && driver.UnitRef == unit;

        void ReserveTemp(Hex cell)
        {
            if (_occ == null || _bridge == null || SelfActor == null)
                return;
            if (_tempReservedThisAction.Contains(cell))
                return;
            if (!_occ.TempReserve(cell, SelfActor))
                return;

            _tempReservedThisAction.Add(cell);
            string unitLabel = TurnManagerV2.FormatUnitLabel(driver != null ? driver.UnitRef : null);
            if (debugLog)
            {
                Debug.Log($"[Occ] TempReserve U={unitLabel} @{cell}", this);
            }
        }

        void ClearTempReservations(string reason, bool logAlways = false)
        {
            int tracked = _tempReservedThisAction.Count;
            int occCleared = (_occ != null && _bridge != null && SelfActor != null) ? _occ.TempClearForOwner(SelfActor) : 0;
            int count = Mathf.Max(tracked, occCleared);
            _tempReservedThisAction.Clear();
            if (logAlways || count > 0)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(driver != null ? driver.UnitRef : null);
                if (debugLog)
                {
                    Debug.Log($"[Occ] TempClear U={unitLabel} count={count} ({reason})", this);
                }
            }
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