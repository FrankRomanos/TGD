using System.Collections.Generic;
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
            var facing = owner.Facing;
            bool useLeft = false;

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
                case CastShape.Line:
                {
                    if (range <= 0)
                        yield break;
                    foreach (var cell in HexLineShape.EnumerateIdealLine(origin, facing, range))
                    {
                        if (layout != null && !layout.Contains(cell))
                            continue;
                        yield return cell;
                    }
                    break;
                }
            }
        }
    }
}
