using System;
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public static class HitResolver
    {
        static readonly IReadOnlyList<UnitRuntimeContext> s_emptyTargets = Array.Empty<UnitRuntimeContext>();

        public static IReadOnlyList<UnitRuntimeContext> Resolve(
            ImpactProfile profile,
            UnitRuntimeContext actor,
            Hex clickedHex,
            UnitRuntimeContext clickedUnitOrNull,
            HexOccupancy occupancy,
            HexBoardLayout layout,
            TurnManagerV2 turnManager = null)
        {
            profile = profile.WithDefaults();
            if (occupancy == null)
                return s_emptyTargets;

            var anchor = ResolveAnchor(profile.anchor, actor, clickedHex, clickedUnitOrNull, occupancy);
            if (profile.anchor == ImpactAnchor.TargetUnitCell && clickedUnitOrNull == null)
                return s_emptyTargets;

            var orientationCtx = profile.anchor == ImpactAnchor.TargetUnitCell ? clickedUnitOrNull : actor;
            var facing = ResolveFacing(orientationCtx, occupancy);

            var cells = EnumerateCells(profile, anchor, facing, layout);
            if (cells.Count == 0)
                return s_emptyTargets;

            var buffer = new List<UnitRuntimeContext>(cells.Count);
            var seen = new HashSet<UnitRuntimeContext>();
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (!TryResolveContextForCell(cell, occupancy, out var candidate))
                    continue;
                if (candidate == null)
                    continue;
                if (!seen.Add(candidate))
                    continue;
                if (!PassTeamFilter(profile.teamFilter, actor, candidate, turnManager))
                    continue;
                buffer.Add(candidate);
            }

            if (buffer.Count == 0)
                return s_emptyTargets;

            TrimByCountMode(profile, buffer, anchor, occupancy);
            return buffer;
        }

        static List<Hex> EnumerateCells(ImpactProfile profile, Hex anchor, Facing4 facing, HexBoardLayout layout)
        {
            var cells = new List<Hex>();
            switch (profile.shape)
            {
                case ImpactShape.Single:
                    cells.Add(anchor);
                    break;
                case ImpactShape.Circle:
                {
                    int radius = Mathf.Max(0, profile.radius);
                    foreach (var cell in HexAreaUtil.Circle(anchor, radius, layout))
                        cells.Add(cell);
                    break;
                }
                case ImpactShape.Sector60:
                {
                    int radius = Mathf.Max(0, profile.radius);
                    foreach (var cell in HexAreaUtil.Sector60Right(anchor, facing, radius, layout, true))
                        cells.Add(cell);
                    break;
                }
                case ImpactShape.Sector120:
                {
                    int radius = Mathf.Max(0, profile.radius);
                    foreach (var cell in HexAreaUtil.Sector120Game(anchor, facing, radius, layout, true))
                        cells.Add(cell);
                    break;
                }
                case ImpactShape.Line:
                {
                    int radius = Mathf.Max(0, profile.radius);
                    foreach (var cell in HexLineShape.EnumerateIdealLine(anchor, facing, radius))
                    {
                        if (layout != null && !layout.Contains(cell))
                            continue;
                        cells.Add(cell);
                    }
                    break;
                }
            }
            return cells;
        }

        static Hex ResolveAnchor(
            ImpactAnchor anchorMode,
            UnitRuntimeContext actor,
            Hex clickedHex,
            UnitRuntimeContext clickedUnit,
            HexOccupancy occupancy)
        {
            switch (anchorMode)
            {
                case ImpactAnchor.SelfCell:
                    return ResolveAnchorForContext(actor, occupancy, clickedHex);
                case ImpactAnchor.TargetCell:
                    return clickedHex;
                case ImpactAnchor.TargetUnitCell:
                    return ResolveAnchorForContext(clickedUnit, occupancy, clickedHex);
                default:
                    return clickedHex;
            }
        }

        static Hex ResolveAnchorForContext(UnitRuntimeContext context, HexOccupancy occupancy, Hex fallback)
        {
            if (context != null)
            {
                if (context.boundUnit != null)
                    return context.boundUnit.Position;
                if (TryGetAdapter(context, out var adapter) && adapter != null)
                    return adapter.Anchor;
            }

            if (context != null && context.boundUnit != null && occupancy != null
                && occupancy.TryGetActor(context.boundUnit.Position, out var occActor) && occActor != null)
            {
                return occActor.Anchor;
            }

            return fallback;
        }

        static Facing4 ResolveFacing(UnitRuntimeContext context, HexOccupancy occupancy)
        {
            if (context != null)
            {
                if (context.boundUnit != null)
                    return context.boundUnit.Facing;
                if (TryGetAdapter(context, out var adapter) && adapter != null)
                    return adapter.Facing;
            }

            if (context != null && context.boundUnit != null && occupancy != null
                && occupancy.TryGetActor(context.boundUnit.Position, out var occActor) && occActor != null)
            {
                return occActor.Facing;
            }

            return Facing4.PlusQ;
        }

        static bool TryGetAdapter(UnitRuntimeContext context, out UnitGridAdapter adapter)
        {
            adapter = null;
            if (context == null)
                return false;
            adapter = context.GetComponent<UnitGridAdapter>()
                ?? context.GetComponentInChildren<UnitGridAdapter>(true)
                ?? context.GetComponentInParent<UnitGridAdapter>(true);
            return adapter != null;
        }

        static bool TryResolveContextForCell(Hex cell, HexOccupancy occupancy, out UnitRuntimeContext context)
        {
            context = null;
            if (!occupancy.TryFindCoveringActor(cell, out var actor, out _))
                return false;

            if (actor is Component component)
            {
                context = component.GetComponentInParent<UnitRuntimeContext>(true);
                if (context != null)
                    return true;
            }

            return false;
        }

        static void TrimByCountMode(ImpactProfile profile, List<UnitRuntimeContext> buffer, Hex anchor, HexOccupancy occupancy)
        {
            if (profile.countMode == ImpactCountMode.All)
                return;

            int cap = profile.maxTargets;
            if (cap <= 0)
                return;

            if (buffer.Count <= cap)
                return;

            if (profile.countMode == ImpactCountMode.FirstN)
            {
                buffer.Sort((a, b) =>
                {
                    var ha = ResolveAnchorForContext(a, occupancy, anchor);
                    var hb = ResolveAnchorForContext(b, occupancy, anchor);
                    return Hex.Distance(anchor, ha).CompareTo(Hex.Distance(anchor, hb));
                });
                if (buffer.Count > cap)
                    buffer.RemoveRange(cap, buffer.Count - cap);
                return;
            }

            if (profile.countMode == ImpactCountMode.RandomN)
            {
                for (int i = buffer.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
                }
                if (buffer.Count > cap)
                    buffer.RemoveRange(cap, buffer.Count - cap);
            }
        }

        static bool PassTeamFilter(
            ImpactTeamFilter filter,
            UnitRuntimeContext actor,
            UnitRuntimeContext candidate,
            TurnManagerV2 turnManager)
        {
            if (candidate == null)
                return false;

            bool isSelf = ReferenceEquals(actor, candidate);
            switch (filter)
            {
                case ImpactTeamFilter.SelfOnly:
                    return isSelf;
                case ImpactTeamFilter.Allies:
                    return !isSelf && IsSameTeam(actor, candidate, turnManager);
                case ImpactTeamFilter.AlliesAndSelf:
                    return isSelf || IsSameTeam(actor, candidate, turnManager);
                case ImpactTeamFilter.Enemies:
                    return !isSelf && !IsSameTeam(actor, candidate, turnManager);
                case ImpactTeamFilter.AllUnits:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsSameTeam(UnitRuntimeContext a, UnitRuntimeContext b, TurnManagerV2 turnManager)
        {
            if (a == null || b == null)
                return false;

            if (ReferenceEquals(a, b))
                return true;

            if (turnManager == null)
                return false;

            var unitA = a.boundUnit;
            var unitB = b.boundUnit;
            if (unitA == null || unitB == null)
                return false;

            bool aIsPlayer = turnManager.IsPlayerUnit(unitA);
            bool bIsPlayer = turnManager.IsPlayerUnit(unitB);
            return aIsPlayer == bIsPlayer;
        }
    }
}
