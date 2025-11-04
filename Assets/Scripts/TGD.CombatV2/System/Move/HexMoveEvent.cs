using System;
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// 移动被拒绝的原因（可按需再扩）
    public enum MoveBlockReason
    {
        None,
        NotReady,           // 组件未就绪
        Busy,               // 正在移动中
        NoConfig,           // 未配置 MoveActionConfig
        Entangled,          // 被缠绕/禁足
        OnCooldown,         // 冷却中
        NotEnoughResource,  // 资源不足
        NoSteps,            // 本次可走步数为 0
        PathBlocked,        // 选中格不可达/被拦
        NoBudget
    }

    /// 点击移动相关的事件。你在别处订阅这些事件，绘制自己的 UI 即可
    public static class HexMoveEvents
    {
        // 显示 / 隐藏 可达范围
        public static event Action<Unit, IEnumerable<Hex>> RangeShown;
        public static event Action RangeHidden;

        // 移动过程
        public static event Action<Unit, List<Hex>> MoveStarted;
        public static event Action<Unit, Hex, Hex, int, int> MoveStep; // (unit, from, to, stepIndex(1~n), total)
        public static event Action<Unit, Hex> MoveFinished;
        // ★ 新增：时间返还（比如加速累计达到阈值，+N 秒）
        public static event Action<Unit, int> TimeRefunded;

        // ★ 新增：没有更多时间（预算耗尽）
        public static event Action<Unit> NoMoreTime;
        // 新增：每步视觉速度提示（解耦，Attack/Move 都能发）
        public static event Action<Unit, float, float> StepSpeedHint;
        // 被拒绝
        public static event Action<Unit, MoveBlockReason, string> MoveRejected;

        // —— 供 HexClickMover 调用的封装 —— 
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

    }
}
