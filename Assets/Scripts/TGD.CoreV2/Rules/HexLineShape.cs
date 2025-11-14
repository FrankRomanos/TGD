using System;
using System.Collections.Generic;

namespace TGD.CoreV2
{
    /// <summary>
    /// Helpers for constructing the "ideal line" used by line-shaped skills.
    /// </summary>
    public static class HexLineShape
    {
        static readonly IReadOnlyList<Hex> EmptySegment = Array.Empty<Hex>();

        /// <summary>
        /// Builds the ideal line as a sequence of segments. Segment indices start at 1.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<Hex>> BuildIdealLineSegments(Hex origin, Facing4 direction, int segmentCount)
        {
            if (segmentCount <= 0)
                return Array.Empty<IReadOnlyList<Hex>>();

            var segments = new List<IReadOnlyList<Hex>>(segmentCount);
            int q0 = origin.q;
            int r0 = origin.r;

            switch (direction)
            {
                case Facing4.PlusR:
                {
                    for (int k = 1; k <= segmentCount; k++)
                        segments.Add(new[] { new Hex(q0, r0 + k) });
                    break;
                }
                case Facing4.MinusR:
                {
                    for (int k = 1; k <= segmentCount; k++)
                        segments.Add(new[] { new Hex(q0, r0 - k) });
                    break;
                }
                case Facing4.PlusQ:
                {
                    for (int k = 1; k <= segmentCount; k++)
                    {
                        int qk = q0 + k;
                        int rc = r0 - (k / 2);
                        if ((k & 1) == 0)
                            segments.Add(new[] { new Hex(qk, rc) });
                        else
                            segments.Add(new[] { new Hex(qk, rc), new Hex(qk, rc - 1) });
                    }
                    break;
                }
                case Facing4.MinusQ:
                {
                    for (int k = 1; k <= segmentCount; k++)
                    {
                        int qk = q0 - k;
                        int rc = r0 + (k / 2);
                        if ((k & 1) == 0)
                            segments.Add(new[] { new Hex(qk, rc) });
                        else
                            segments.Add(new[] { new Hex(qk, rc), new Hex(qk, rc + 1) });
                    }
                    break;
                }
                default:
                    return Array.Empty<IReadOnlyList<Hex>>();
            }

            return segments;
        }

        /// <summary>
        /// Flattens the ideal line segments into a single sequence of hexes.
        /// </summary>
        public static IEnumerable<Hex> EnumerateIdealLine(Hex origin, Facing4 direction, int segmentCount)
        {
            var segments = BuildIdealLineSegments(origin, direction, segmentCount);
            if (segments.Count == 0)
                yield break;

            foreach (var segment in segments)
            {
                if (segment == null || segment.Count == 0)
                    continue;

                for (int i = 0; i < segment.Count; i++)
                    yield return segment[i];
            }
        }

        /// <summary>
        /// Returns the final segment (Segment(L)) of the ideal line. Empty when <paramref name="segmentCount" /> &lt;= 0.
        /// </summary>
        public static IReadOnlyList<Hex> GetTerminalSegment(Hex origin, Facing4 direction, int segmentCount)
        {
            var segments = BuildIdealLineSegments(origin, direction, segmentCount);
            if (segments.Count == 0)
                return EmptySegment;
            return segments[segments.Count - 1] ?? EmptySegment;
        }
    }
}
