using System;
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// ƶܾԭ򣨿ɰ
    public enum MoveBlockReason
    {
        None,
        NotReady,           // δ
        Busy,               // ƶ
        NoConfig,           // δ MoveActionConfig
        Entangled,          // /
        OnCooldown,         // ȴ
        NotEnoughResource,  // Դ
        NoSteps,            // ο߲Ϊ 0
        PathBlocked,        // ѡи񲻿ɴ/
        NoBudget
    }

    /// ƶص¼ڱ𴦶Щ¼Լ UI 
    public static class HexMoveEvents
    {
        // ʾ /  ɴﷶΧ
        public static event Action<Unit, IEnumerable<Hex>> RangeShown;
        public static event Action RangeHidden;

        // ƶ
        public static event Action<Unit, List<Hex>> MoveStarted;
        public static event Action<Unit, Hex, Hex, int, int> MoveStep; // (unit, from, to, stepIndex(1~n), total)
        public static event Action<Unit, Hex> MoveFinished;
        //  ʱ䷵ۼƴﵽֵ+N 룩
        public static event Action<Unit, int> TimeRefunded;

        //  ûиʱ䣨Ԥľ
        public static event Action<Unit> NoMoreTime;
        // ÿӾٶʾAttack/Move ܷ
        public static event Action<Unit, float, float> StepSpeedHint;
        // ܾ
        public static event Action<Unit, MoveBlockReason, string> MoveRejected;

        //   HexClickMover õķװ  
        internal static void RaiseRangeShown(Unit u, IEnumerable<Hex> cells) => RangeShown?.Invoke(u, cells);
        internal static void RaiseRangeHidden() => RangeHidden?.Invoke();
        internal static void RaiseMoveStarted(Unit u, List<Hex> path) => MoveStarted?.Invoke(u, path);
        internal static void RaiseMoveStep(Unit u, Hex from, Hex to, int i, int n) => MoveStep?.Invoke(u, from, to, i, n);
        internal static void RaiseMoveFinished(Unit u, Hex end) => MoveFinished?.Invoke(u, end);
        internal static void RaiseRejected(Unit u, MoveBlockReason r, string msg) => MoveRejected?.Invoke(u, r, msg);
        internal static void RaiseTimeRefunded(Unit u, int seconds)
            => TimeRefunded?.Invoke(u, seconds);

        internal static void RaiseNoMoreTime(Unit u)
            => NoMoreTime?.Invoke(u);
        internal static void RaiseStepSpeed(Unit u, float effMR, float baseMR) => StepSpeedHint?.Invoke(u, effMR, baseMR);

        public static void Reset()
        {
            RangeShown = null;
            RangeHidden = null;
            MoveStarted = null;
            MoveStep = null;
            MoveFinished = null;
            TimeRefunded = null;
            NoMoreTime = null;
            StepSpeedHint = null;
            MoveRejected = null;
        }

    }
}
