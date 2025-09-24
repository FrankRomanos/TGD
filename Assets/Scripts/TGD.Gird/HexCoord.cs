using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Grid
{
    /// <summary>
    /// Represents an axial hex coordinate (q, r) using a flat-top orientation.
    /// </summary>
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public static readonly HexCoord Zero = new HexCoord(0, 0);

        // Axial coordinates (q, r). The third cube coordinate is s = -q - r.
        public int Q { get; }
        public int R { get; }
        public int S => -Q - R;

        public static IReadOnlyList<HexCoord> Directions { get; } = new[]
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int Length => Distance(Zero, this);

        public static HexCoord FromAxial(int q, int r) => new HexCoord(q, r);
        public static HexCoord FromAxial(Vector2Int axial) => new HexCoord(axial.x, axial.y);

        public Vector2Int ToAxialVector2Int() => new Vector2Int(Q, R);

        public static HexCoord FromOffset(int x, int z, HexOffsetMode mode = HexOffsetMode.OddRow)
        {
            int q = mode switch
            {
                HexOffsetMode.EvenRow => x - (z + (z & 1)) / 2,
                _ => x - (z - (z & 1)) / 2
            };
            return new HexCoord(q, z);
        }

        public Vector2Int ToOffset(HexOffsetMode mode = HexOffsetMode.OddRow)
        {
            int x = mode switch
            {
                HexOffsetMode.EvenRow => Q + (R + (R & 1)) / 2,
                _ => Q + (R - (R & 1)) / 2
            };
            return new Vector2Int(x, R);
        }

        public static int Distance(HexCoord a, HexCoord b)
        {
            return (Math.Abs(a.Q - b.Q) + Math.Abs(a.R - b.R) + Math.Abs(a.S - b.S)) / 2;
        }

        public static HexCoord StepToward(HexCoord from, HexCoord to)
        {
            if (from == to)
                return Zero;

            HexCoord bestDirection = Zero;
            int bestDistance = int.MaxValue;
            foreach (var direction in Directions)
            {
                var candidate = from + direction;
                int distance = Distance(candidate, to);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestDirection = direction;
            }
            return bestDirection;
        }

        public static HexCoord MoveTowards(HexCoord start, HexCoord end, int maxDistance)
        {
            if (maxDistance <= 0)
                return start;

            int distance = Distance(start, end);
            if (distance <= maxDistance)
                return end;

            int index = 0;
            foreach (var step in GetLine(start, end))
            {
                if (index == maxDistance)
                    return step;
                index++;
            }

            return start;
        }

        public static IEnumerable<HexCoord> GetLine(HexCoord start, HexCoord end)
        {
            int distance = Distance(start, end);
            if (distance == 0)
            {
                yield return start;
                yield break;
            }

            for (int i = 0; i <= distance; i++)
            {
                float t = distance == 0 ? 0f : i / (float)distance;
                yield return Lerp(start, end, t);
            }
        }

        public static HexCoord Lerp(HexCoord a, HexCoord b, float t)
        {
            float q = Mathf.Lerp(a.Q, b.Q, t);
            float r = Mathf.Lerp(a.R, b.R, t);
            return Round(q, r);
        }

        public Vector3 ToWorldPosition(float radius, HexOrientation orientation = HexOrientation.FlatTop, Vector3 origin = default)
        {
            float x;
            float z;
            switch (orientation)
            {
                case HexOrientation.PointyTop:
                    x = radius * (Mathf.Sqrt(3f) * (Q + R / 2f));
                    z = radius * (1.5f * R);
                    break;
                default:
                    x = radius * (1.5f * Q);
                    z = radius * (Mathf.Sqrt(3f) * (R + Q / 2f));
                    break;
            }
            return new Vector3(x, 0f, z) + origin;
        }

        public static HexCoord FromWorldPosition(Vector3 world, float radius, HexOrientation orientation = HexOrientation.FlatTop, Vector3 origin = default)
        {
            var local = world - origin;
            float q;
            float r;
            switch (orientation)
            {
                case HexOrientation.PointyTop:
                    q = (Mathf.Sqrt(3f) / 3f * local.x - 1f / 3f * local.z) / radius;
                    r = (2f / 3f * local.z) / radius;
                    break;
                default:
                    q = (2f / 3f * local.x) / radius;
                    r = (-1f / 3f * local.x + Mathf.Sqrt(3f) / 3f * local.z) / radius;
                    break;
            }

            var axial = Round(q, r);
            return axial;
        }

        public static HexCoord Round(float q, float r)
        {
            float s = -q - r;
            int rq = Mathf.RoundToInt(q);
            int rr = Mathf.RoundToInt(r);
            int rs = Mathf.RoundToInt(s);

            float qDiff = Mathf.Abs(rq - q);
            float rDiff = Mathf.Abs(rr - r);
            float sDiff = Mathf.Abs(rs - s);

            if (qDiff > rDiff && qDiff > sDiff)
                rq = -rr - rs;
            else if (rDiff > sDiff)
                rr = -rq - rs;
            else
                rs = -rq - rr;

            return new HexCoord(rq, rr);
        }

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.Q + b.Q, a.R + b.R);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.Q - b.Q, a.R - b.R);
        public static HexCoord operator -(HexCoord a) => new HexCoord(-a.Q, -a.R);
        public static HexCoord operator *(HexCoord a, int scale) => new HexCoord(a.Q * scale, a.R * scale);
        public static HexCoord operator *(int scale, HexCoord a) => a * scale;

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Q, R);
        public static bool operator ==(HexCoord left, HexCoord right) => left.Equals(right);
        public static bool operator !=(HexCoord left, HexCoord right) => !left.Equals(right);

        public override string ToString() => $"({Q}, {R})";
    }

    public enum HexOrientation
    {
        FlatTop,
        PointyTop
    }

    public enum HexOffsetMode
    {
        OddRow,
        EvenRow
    }
}