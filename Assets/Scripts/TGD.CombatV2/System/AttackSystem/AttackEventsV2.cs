// File: TGD.CombatV2/AttackEventsV2.cs
using System;
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    public enum AttackRejectReasonV2
    {
        NotReady,
        Busy,
        OnCooldown,
        NotEnoughResource,
        NoPath,
        CantMove
    }

    public static class AttackEventsV2
    {
        public static event Action<Unit, IEnumerable<Hex>> AimShown;
        public static event Action AimHidden;

        public static event Action<Unit, List<Hex>> AttackMoveStarted;
        public static event Action<Unit, Hex, Hex, int, int> AttackMoveStep;
        public static event Action<Unit, Hex> AttackMoveFinished;

        public static event Action<Unit, Hex> AttackHit;
        public static event Action<Unit, int> AttackAnimationRequested;
        public static event Action<Unit, int> AttackStrikeFired;
        public static event Action<Unit, int> AttackAnimationEnded;
        public static event Action<Unit, string> AttackMiss; // msg

        public static event Action<Unit, AttackRejectReasonV2, string> AttackRejected;

        public static void Reset()
        {
            AimShown = null;
            AimHidden = null;
            AttackMoveStarted = null;
            AttackMoveStep = null;
            AttackMoveFinished = null;
            AttackHit = null;
            AttackAnimationRequested = null;
            AttackStrikeFired = null;
            AttackAnimationEnded = null;
            AttackMiss = null;
            AttackRejected = null;
        }

        internal static void RaiseAimShown(Unit u, IEnumerable<Hex> cells) => AimShown?.Invoke(u, cells);
        internal static void RaiseAimHidden() => AimHidden?.Invoke();

        internal static void RaiseAttackMoveStarted(Unit u, List<Hex> path) => AttackMoveStarted?.Invoke(u, path);
        internal static void RaiseAttackMoveStep(Unit u, Hex from, Hex to, int stepIndex, int stepCount)
            => AttackMoveStep?.Invoke(u, from, to, stepIndex, stepCount);
        internal static void RaiseAttackMoveFinished(Unit u, Hex end) => AttackMoveFinished?.Invoke(u, end);

        internal static void RaiseHit(Unit u, Hex target) => AttackHit?.Invoke(u, target);
        internal static void RaiseAttackAnimation(Unit u, int comboIndex) => AttackAnimationRequested?.Invoke(u, comboIndex);
        internal static void RaiseAttackStrike(Unit u, int comboIndex) => AttackStrikeFired?.Invoke(u, comboIndex);
        internal static void RaiseAttackAnimEnded(Unit u, int comboIndex) => AttackAnimationEnded?.Invoke(u, comboIndex);
        internal static void RaiseMiss(Unit u, string msg) => AttackMiss?.Invoke(u, msg);

        internal static void RaiseRejected(Unit u, AttackRejectReasonV2 r, string msg) => AttackRejected?.Invoke(u, r, msg);
    }
}
