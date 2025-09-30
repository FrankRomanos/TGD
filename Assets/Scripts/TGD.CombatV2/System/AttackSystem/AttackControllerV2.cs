// File: TGD.CombatV2/AttackControllerV2.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2; // 已有

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackControllerV2 : MonoBehaviour,IActionToolV2
    {
        public string Id => "Attack";

        public void OnEnterAim()
        {
            _hover = null;
            _painter.Clear();
            AttackEventsV2.RaiseAimShown(driver.UnitRef, System.Array.Empty<Hex>());
        }

        public void OnExitAim()
        {
            _painter.Clear();
            AttackEventsV2.RaiseAimHidden();
        }

        public void OnHover(Hex hex)
        {
            HighlightLandingRing(hex); // 你原来的落脚环预览
        }

        public IEnumerator OnConfirm(Hex hex)
        {
            yield return PlanAndRunMeleeTo(hex); // 直接沿用你现有的协程
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

        [Header("Config")]
        public AttackActionConfigV2 config;
        public MonoBehaviour costProvider;     // 赋 AttackCostServiceV2Adapter
        IAttackCostService _cost;

        [Header("Aim & Picking")]
        public Camera pickCamera;
        public LayerMask pickMask = ~0;
        public float rayMaxDistance = 2000f;
        public float pickPlaneY = 0.01f;

        [Header("Visuals")]
        public Color landingRingColor = new(1f, 0.9f, 0.2f, 0.85f);
        public float stepSeconds = 0.12f;
        public float y = 0.01f;

        // runtime
        HexAreaPainter _painter;
        HexOccupancy _occ;
        IGridActor _actor;
        bool _aiming = false;
        bool _moving = false;
        Hex? _hover;

        bool _guardPushed = false;

        void Awake()
        {
            _painter = new HexAreaPainter(tiler);
            _cost = costProvider as IAttackCostService;
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
        }

        void Start()
        {
            tiler?.EnsureBuilt();
            driver?.EnsureInit();
            if (authoring?.Layout == null || driver == null || !driver.IsReady) { enabled = false; return; }

            _occ = occService ? occService.Get() : new HexOccupancy(authoring.Layout);
            var fp = footprintForActor ? footprintForActor : CreateSingleFallback();
            _actor = new UnitGridAdapter(driver.UnitRef, fp);
        }

        void OnDisable() 
        { 
            _painter?.Clear(); 

        }

        void Update()
        {
            if (authoring == null || driver == null || !driver.IsReady) return;

            if (!_aiming || _moving) return;

            // hover 目标格
            var h = PickHexUnderMouse();
            if (h.HasValue && (!_hover.HasValue || !_hover.Value.Equals(h.Value)))
            {
                _hover = h;
                HighlightLandingRing(h.Value);  // ★ 内部已处理“缠绕只显示当前格是否可打”
            }

            // 左键执行
            if (Input.GetMouseButtonDown(0) && _hover.HasValue)
            {
                StartCoroutine(PlanAndRunMeleeTo(_hover.Value));
                _aiming = false;
                _painter.Clear();
                AttackEventsV2.RaiseAimHidden();
            }

            // 右键/ESC 取消
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                _aiming = false;
                _painter.Clear();
                _hover = null;
                AttackEventsV2.RaiseAimHidden();
            }
        }

        // —— 预览：只高亮“合法落脚点”；缠绕时仅允许当前格作为落脚 —— //
        void HighlightLandingRing(Hex target)
        {
            var L = authoring.Layout;
            int range = config ? Mathf.Max(1, config.meleeRange) : 1;
            var ring = new List<Hex>();

            var start = _actor != null ? _actor.Anchor : driver.UnitRef.Position;   // ★ 当前格
            bool entangled = (ctx != null && ctx.Entangled);                        // ★

            foreach (var h in Hex.Ring(target, range))
            {
                if (!L.Contains(h)) continue;
                if (env != null && env.IsPit(h)) continue;

                // 缠绕：仅允许当前格被高亮
                if (entangled && !h.Equals(start)) continue;                         // ★

                if (!_occ.CanPlace(_actor, h, _actor.Facing, ignore: null)) continue;
                ring.Add(h);
            }

            _painter.Clear();
            if (ring.Count > 0) _painter.Paint(ring, landingRingColor);
            AttackEventsV2.RaiseAimShown(driver.UnitRef, ring);
        }

        IEnumerator PlanAndRunMeleeTo(Hex target)
        {
            if (_moving) yield break;
            _moving = true;

            // —— 规划：缠绕=移动预算 0 —— //
            float baseMR = (ctx != null) ? Mathf.Max(0.01f, ctx.MoveRate) : 3f;
            bool entangled = (ctx != null && ctx.Entangled);                       // ★
            int baseBudget = Mathf.Max(1, config ? config.baseTimeSeconds : 2);
            int budget = entangled ? 0 : baseBudget;                               // ★ 缠绕时禁止移动

            var plan = AttackPlannerV2.PlanMeleeApproach(
                authoring.Layout, _occ, _actor,
                driver.UnitRef.Position, target,
                config ? config.meleeRange : 1,
                isPit: (h) => env != null && env.IsPit(h),
                budgetSeconds: budget,                                             // ★ 允许 0
                baseMoveRate: baseMR,
                getEnvMult: (h) => env != null ? env.GetSpeedMult(h) : 1f,
                refundThresholdSeconds: config ? config.refundThresholdSeconds : 0.8f
            );

            // —— 缠绕保险：若规划仍需要移动，则拒绝 —— //
            bool needsMove = plan.truncatedPath != null && plan.truncatedPath.Count > 1; // ★
            if (entangled && needsMove)                                                  // ★
            {
                AttackEventsV2.RaiseRejected(driver.UnitRef, AttackRejectReasonV2.CantMove, "Can't move while entangled.");
                _moving = false;
                yield break;
            }

            if (plan.truncatedPath == null || plan.truncatedPath.Count < 1)
            {
                AttackEventsV2.RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NoPath, "Can't get close.");
                _moving = false;
                yield break;
            }

            // —— 费用：资源不足/冷却中直接拒绝 —— 
            if (_cost != null && config != null)
            {
                if (_cost.IsOnCooldown(driver.UnitRef, config))
                { AttackEventsV2.RaiseRejected(driver.UnitRef, AttackRejectReasonV2.OnCooldown, "Attack on cooldown."); _moving = false; yield break; }

                if (!_cost.HasEnough(driver.UnitRef, config))
                { AttackEventsV2.RaiseRejected(driver.UnitRef, AttackRejectReasonV2.NotEnoughResource, "Not enough energy."); _moving = false; yield break; }

                _cost.Pay(driver.UnitRef, config);
            }

            // —— 起手转向（即使不移动也先朝向目标） —— 
            if (driver.unitView != null)
            {
                var fromW = authoring.Layout.World(plan.truncatedPath[0], y);
                var tgtW = authoring.Layout.World(target, y);
                float keep = config ? config.keepDeg : 45f;
                float turn = config ? config.turnDeg : 135f;
                float speed = config ? config.turnSpeedDegPerSec : 720f;

                var (nf, yaw) = HexBoard.HexFacingUtil.ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, tgtW, keep, turn);
                yield return HexBoard.HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                driver.UnitRef.Facing = nf;
                _actor.Facing = nf;
            }

            // —— 逐格推进（不穿人、不踩 pit）——
            AttackEventsV2.RaiseMoveStarted(driver.UnitRef, plan.truncatedPath);

            var L = authoring.Layout;
            var unit = driver.UnitRef;
            var path = plan.truncatedPath;

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                if (_occ.IsBlocked(to, _actor)) break;
                if (env != null && env.IsPit(to)) break;

                AttackEventsV2.RaiseMoveStep(unit, from, to, i, path.Count - 1);

                var fromW = L.World(from, y);
                var toW = L.World(to, y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / Mathf.Max(0.3f, stepSeconds);
                    if (driver.unitView != null)
                        driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                _occ.TryMove(_actor, to);
                if (driver.Map != null)
                { if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to); }
                unit.Position = to;
                driver.SyncView();
            }

            AttackEventsV2.RaiseMoveFinished(driver.UnitRef, driver.UnitRef.Position);

            // —— 命中/未命中占位 —— 
            if (plan.canHit)
            {
                AttackEventsV2.RaiseHit(driver.UnitRef, target);
                Debug.Log($"[AttackV2] Hit! landing={plan.chosenLanding} used={plan.usedSeconds:0.##}s refunded={plan.refundedSeconds}");
            }
            else
            {
                AttackEventsV2.RaiseMiss(driver.UnitRef, "Out of reach.");
                Debug.Log($"[AttackV2] Miss. stopped early after {plan.usedSeconds:0.##}s");
            }

            _moving = false;
        }

        // —— 拾取 —— //
        Hex? PickHexUnderMouse()
        {
            var cam = pickCamera ? pickCamera : Camera.main;
            if (!cam) return null;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, rayMaxDistance, pickMask, QueryTriggerInteraction.Ignore))
                return authoring.Layout.HexAt(hit.point);

            var plane = new Plane(Vector3.up, new Vector3(0f, pickPlaneY, 0f));
            if (!plane.Raycast(ray, out float dist)) return null;
            return authoring.Layout.HexAt(ray.GetPoint(dist));
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
