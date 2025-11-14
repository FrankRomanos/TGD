using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Targeting
{
    static class TargetSelectionAreaBuilder
    {
        public static void Build(
            UnitRuntimeContext context,
            Unit owner,
            Hex? hover,
            TargetingSpec spec,
            ITargetValidator validator,
            HexBoardAuthoringLite authoring,
            List<Hex> valid,
            List<Hex> invalid)
        {
            valid?.Clear();
            invalid?.Clear();

            if (spec == null || owner == null || valid == null || invalid == null)
                return;

            var profile = spec.selection;
            profile = profile.WithDefaults();

            if (profile.shape == CastShape.SingleCell)
            {
                if (hover.HasValue)
                {
                    var result = validator != null
                        ? validator.Check(owner, hover.Value, spec)
                        : new TargetCheckResult { ok = true, hit = HitKind.None, plan = PlanKind.MoveOnly };
                    if (result.ok)
                        valid.Add(hover.Value);
                    else
                        invalid.Add(hover.Value);
                }
                return;
            }

            int resolvedRange = profile.ResolveRange(context, spec.maxRangeHexes);
            var layout = authoring != null ? authoring.Layout : null;
            var origin = owner.Position;
            var facing = ResolveFacing(origin, owner.Facing, hover);
            bool useLeft = profile.shape == CastShape.Cone60 && ShouldUseLeft(origin, hover, facing);

            var seen = new HashSet<Hex>();
            foreach (var cell in EnumerateArea(profile.shape, origin, facing, resolvedRange, layout, useLeft))
            {
                if (!seen.Add(cell))
                    continue;
                if (cell.Equals(origin))
                    continue;

                var result = validator != null
                    ? validator.Check(owner, cell, spec)
                    : new TargetCheckResult { ok = true, hit = HitKind.None, plan = PlanKind.MoveOnly };

                if (result.ok)
                    valid.Add(cell);
                else
                    invalid.Add(cell);
            }
        }

        static IEnumerable<Hex> EnumerateArea(
            CastShape shape,
            Hex origin,
            Facing4 facing,
            int range,
            HexBoardLayout layout,
            bool useLeft)
        {
            switch (shape)
            {
                case CastShape.Circle:
                {
                    if (range < 0)
                    {
                        if (layout == null)
                            yield break;
                        foreach (var cell in layout.Coordinates())
                            yield return cell;
                        yield break;
                    }

                    foreach (var cell in HexAreaUtil.Circle(origin, range, layout))
                        yield return cell;
                    break;
                }
                case CastShape.Cone60:
                {
                    if (range <= 0)
                        yield break;
                    IEnumerable<Hex> raw = useLeft
                        ? HexAreaUtil.Sector60Left(origin, facing, range, layout, true)
                        : HexAreaUtil.Sector60Right(origin, facing, range, layout, true);
                    foreach (var cell in raw)
                        yield return cell;
                    break;
                }
                case CastShape.Cone120:
                {
                    if (range <= 0)
                        yield break;
                    foreach (var cell in HexAreaUtil.Sector120Game(origin, facing, range, layout, true))
                        yield return cell;
                    break;
                }
            }
        }

        static Facing4 ResolveFacing(Hex origin, Facing4 fallback, Hex? hover)
        {
            if (!hover.HasValue || hover.Value.Equals(origin))
                return fallback;

            var delta = hover.Value - origin;
            if (Mathf.Abs(delta.q) >= Mathf.Abs(delta.r))
                return delta.q >= 0 ? Facing4.PlusQ : Facing4.MinusQ;
            return delta.r >= 0 ? Facing4.PlusR : Facing4.MinusR;
        }

        static bool ShouldUseLeft(Hex origin, Hex? hover, Facing4 facing)
        {
            if (!hover.HasValue || hover.Value.Equals(origin))
                return false;

            var delta = hover.Value - origin;
            int index = HexAreaUtil.FacingToDirIndex(facing);
            var right = Hex.Directions[(index + 1) % 6];
            var left = Hex.Directions[(index + 5) % 6];
            int dotRight = delta.q * right.q + delta.r * right.r;
            int dotLeft = delta.q * left.q + delta.r * left.r;
            if (dotLeft == dotRight)
                return dotLeft > 0 && facing == Facing4.MinusQ;
            return dotLeft > dotRight;
        }
    }
}
