// File: TGD.CombatV2/FuzzyMoveRunner.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public static class FuzzyMoveRunner
    {
        public sealed class Args
        {
            // ��Ҫ����
            public HexBoardLayout layout;
            public HexEnvironmentSystem env;
            public HexBoardTestDriver driver;
            public HexOccupancy occ;
            public IGridActor actor;
            public UnitRuntimeContext ctx;

            // �ɱ�/����
            public MoveActionConfig moveConfig;
            public IMoveCostService moveCost;

            // ·�����Ӿ�
            public List<Hex> rawPath;            // ����Ŀ������·������������
            public float stepSeconds = 0.12f;
            public float y = 0.01f;

            // ʱ���뷵��
            public bool simulateTurnTime = true;
            public System.Func<int> getTimeLeft;   // �� AttackController ˽�� _turnSecondsLeft
            public System.Action<int> setTimeLeft; // ��д AttackController ˽�� _turnSecondsLeft
            public float refundThresholdSeconds = 0.8f;

            // ����
            public bool debug = false;
        }

        static int EffectiveBaseMR(UnitRuntimeContext ctx)
        {
            if (ctx == null) return 3;
            int baseR = Mathf.Max(1, ctx.BaseMoveRate);
            float buffMult = 1f + Mathf.Max(-0.99f, ctx.MoveRatePctAdd);
            int flat = ctx.MoveRateFlatAdd;
            return StatsMathV2.EffectiveMoveRateFromBase(baseR, new[] { buffMult }, flat);
        }

        static bool CanAffordSeconds(IMoveCostService svc, MoveActionConfig cfg, MoveCostServiceV2Adapter moveAdapter, int seconds)
        {
            if (cfg == null || seconds <= 0) return true;
            if (moveAdapter != null && moveAdapter.stats != null)
            {
                int need = seconds * Mathf.Max(0, cfg.energyCost);
                return moveAdapter.stats.Energy >= need;
            }
            // ����ޣ�����һ��ɸ�
            return svc != null;
        }

        static void PaySeconds(IMoveCostService svc, MoveActionConfig cfg, MoveCostServiceV2Adapter moveAdapter, TGD.HexBoard.Unit unit, int seconds)
        {
            if (cfg == null || seconds <= 0) return;
            if (moveAdapter != null && moveAdapter.stats != null)
            {
                int need = seconds * Mathf.Max(0, cfg.energyCost);
                int before = moveAdapter.stats.Energy;
                moveAdapter.stats.Energy = Mathf.Clamp(before - need, 0, moveAdapter.stats.MaxEnergy);
                Debug.Log($"[FuzzyMove] PayMoveSeconds: cost={need}  energy {before}->{moveAdapter.stats.Energy}");
            }
            else if (svc != null)
            {
                for (int i = 0; i < seconds; i++) svc.Pay(unit, cfg);
            }
        }
        public static List<Hex> BuildShortestPath(
    HexBoardLayout layout,
    HexOccupancy occ,
    IGridActor actor,
    Hex start,
    Hex dest,
    System.Func<Hex, bool> isPit)
        {
            if (layout == null) return null;
            if (!layout.Contains(start) || !layout.Contains(dest)) return null;

            var q = new Queue<Hex>();
            var came = new Dictionary<Hex, Hex>();
            q.Enqueue(start); came[start] = start;

            bool Block(Hex cell)
            {
                if (!layout.Contains(cell)) return true;
                if (isPit != null && isPit(cell)) return true;
                // �����ˣ��������/�յ���λ��
                if (occ != null && occ.IsBlocked(cell, actor) && !cell.Equals(start) && !cell.Equals(dest))
                    return true;
                return false;
            }

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(dest)) break;

                foreach (var nb in new[]{
            new Hex(cur.q+1,cur.r+0), new Hex(cur.q+1,cur.r-1), new Hex(cur.q+0,cur.r-1),
            new Hex(cur.q-1,cur.r+0), new Hex(cur.q-1,cur.r+1), new Hex(cur.q+0,cur.r+1)})
                {
                    if (came.ContainsKey(nb)) continue;
                    if (Block(nb)) continue;
                    came[nb] = cur; q.Enqueue(nb);
                }
            }

            if (!came.ContainsKey(dest)) return null;
            var path = new List<Hex> { dest };
            var c = dest;
            while (!c.Equals(start)) { c = came[c]; path.Add(c); }
            path.Reverse();
            return path;
        }

        public static IEnumerator Run(Args a)
        {
            // ����У��
            if (a == null || a.layout == null || a.driver == null || a.occ == null || a.actor == null) yield break;
            var unit = a.driver.UnitRef;
            if (unit == null || a.rawPath == null || a.rawPath.Count < 2) yield break;

            // ���� �ԡ����˲��� MR_click���������Σ���������Ҫ������ ���� //
            int steps = Mathf.Max(0, a.rawPath.Count - 1);
            int baseMR = (a.ctx != null) ? Mathf.Max(1, a.ctx.BaseMoveRate) : 3;
            float buff = 1f + (a.ctx != null ? Mathf.Max(-0.99f, a.ctx.MoveRatePctAdd) : 0f);
            int flat = (a.ctx != null) ? a.ctx.MoveRateFlatAdd : 0;

            var start = a.rawPath[0];
            float startMult = (a.env != null) ? Mathf.Clamp(a.env.GetSpeedMult(start), 0.1f, 5f) : 1f;

            int mrClick = StatsMathV2.EffectiveMoveRateFromBase(baseMR, new[] { buff, startMult }, flat);
            mrClick = Mathf.Max(1, mrClick);

            int requiredSec = Mathf.CeilToInt(steps / (float)mrClick); // �� ֻ�ڡ�Ԥ�۽׶Ρ�����㱶��
            int timeLeft = a.simulateTurnTime && a.getTimeLeft != null ? Mathf.Max(0, a.getTimeLeft()) : requiredSec;
            int timeBudget = a.simulateTurnTime ? Mathf.Min(timeLeft, requiredSec) : requiredSec;

            if (a.debug)
                Debug.Log($"[FuzzyMove] clickMR={mrClick} steps={steps} need={requiredSec}s  timeLeft={timeLeft}s  budget={timeBudget}s");


            if (timeBudget <= 0)
            {
                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                if (a.debug) Debug.Log("[FuzzyMove] No time.");
                yield break;
            }

            var moveAdapter = a.moveCost as MoveCostServiceV2Adapter; // ֻ��Ϊ��ȡ stats�������� MoveCostServiceV2Adapter���뻻���Ǹ�����
            if (!CanAffordSeconds(a.moveCost, a.moveConfig, moveAdapter, timeBudget))
            {
                AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "Not enough energy for move.");
                yield break;
            }

            // �ȸ��������������������ˣ�
            PaySeconds(a.moveCost, a.moveConfig, moveAdapter, unit, timeBudget);

            // 2) ִ����ģ�⣨����������/������
            float EnvMult(Hex h) => a.env ? a.env.GetSpeedMult(h) : 1f;
            var sim = MoveSimulator.Run(a.rawPath, baseMR, timeBudget, EnvMult,
                                        a.refundThresholdSeconds, a.debug);
            var reached = sim.ReachedPath;
            int refunded = Mathf.Max(0, sim.RefundedSeconds);
            int spentSec = Mathf.Max(0, timeBudget - refunded);

            if (reached == null || reached.Count < 2)
            {
                // һ�����߲��ˣ������˻�
                a.moveCost?.RefundSeconds(unit, a.moveConfig, timeBudget);
                if (a.simulateTurnTime && a.setTimeLeft != null) a.setTimeLeft(Mathf.Max(0, timeLeft)); // ����
                HexMoveEvents.RaiseTimeRefunded(unit, timeBudget);
                if (a.debug) Debug.Log($"[FuzzyMove] No step. Refund ALL {timeBudget}s.");
                yield break;
            }

            // 3) ����ת�򵽡����յ���㡱
            if (a.driver.unitView != null)
            {
                var fromW = a.layout.World(reached[0], a.y);
                var toW = a.layout.World(reached[^1], a.y);
                var (nf, yaw) = HexBoard.HexFacingUtil.ChooseFacingByAngle45(unit.Facing, fromW, toW, 45f, 135f);
                yield return HexBoard.HexFacingUtil.RotateToYaw(a.driver.unitView, yaw, 720f);
                unit.Facing = nf; a.actor.Facing = nf;
            }

            AttackEventsV2.RaiseMoveStarted(unit, reached);

            // 4) ��� Tween���ȱ��̶ֹ� stepSeconds��������Ҫ�Ļ��ٽӡ���ʵ����/�������١���
            for (int i = 1; i < reached.Count; i++)
            {
                var from = reached[i - 1];
                var to = reached[i];

                if (a.occ.IsBlocked(to, a.actor)) break;
                if (a.env != null && a.env.IsPit(to)) break;

                AttackEventsV2.RaiseMoveStep(unit, from, to, i, reached.Count - 1);

                var fromW = a.layout.World(from, a.y);
                var toW = a.layout.World(to, a.y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / Mathf.Max(0.06f, a.stepSeconds);
                    if (a.driver.unitView != null)
                        a.driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                a.occ.TryMove(a.actor, to);
                if (a.driver.Map != null) { if (!a.driver.Map.Move(unit, to)) a.driver.Map.Set(unit, to); }
                unit.Position = to;
                a.driver.SyncView();
            }

            AttackEventsV2.RaiseMoveFinished(unit, unit.Position);

            // 5) ʱ����뷵��
            if (a.simulateTurnTime && a.setTimeLeft != null)
                a.setTimeLeft(Mathf.Max(0, timeLeft - spentSec));

            if (refunded > 0 && a.moveCost != null && a.moveConfig != null)
            {
                a.moveCost.RefundSeconds(unit, a.moveConfig, refunded);
                HexMoveEvents.RaiseTimeRefunded(unit, refunded);
            }

            bool truncatedByBudget = (reached.Count < a.rawPath.Count);
            if (truncatedByBudget) HexMoveEvents.RaiseNoMoreTime(unit);

            if (a.debug) Debug.Log($"[FuzzyMove] spent={spentSec}s refunded={refunded}s  timeLeft={(a.getTimeLeft != null ? a.getTimeLeft() : -1)}");
        }
    }
}
