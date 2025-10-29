using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary> 轴坐标（q,r）；立方坐标 s = -q-r 隐含。</summary>
    [Serializable]
    public struct Hex : IEquatable<Hex>
    {
        public int q; public int r;
        public Hex(int q, int r) { this.q = q; this.r = r; }
        public static readonly Hex Zero = new Hex(0, 0);


        public static Hex operator +(Hex a, Hex b) => new Hex(a.q + b.q, a.r + b.r);
        public static Hex operator -(Hex a, Hex b) => new Hex(a.q - b.q, a.r - b.r);
        public static Hex operator *(Hex a, int k) => new Hex(a.q * k, a.r * k);
        public bool Equals(Hex other) => q == other.q && r == other.r;
        public override bool Equals(object obj) => obj is Hex h && Equals(h);
        public override int GetHashCode() => (q * 397) ^ r;
        public override string ToString() => $"({q},{r})";

        // 六邻方向（标准轴：E, NE, N, W, SW, S）
        public static readonly Hex[] Directions = new Hex[6]
        {
            new Hex(+1, 0),   // 0: +Q (East)
            new Hex(+1,-1),   // 1: +Q- R (NorthEast)
            new Hex( 0,-1),   // 2: -R (North)
            new Hex(-1, 0),   // 3: -Q (West)
            new Hex(-1,+1),   // 4: -Q+R (SouthWest)
            new Hex( 0,+1)    // 5: +R (South)
        };

        public static int Distance(Hex a, Hex b)
        {
            int dq = a.q - b.q;
            int dr = a.r - b.r;
            int ds = (-a.q - a.r) - (-b.q - b.r);
            return (Mathf.Abs(dq) + Mathf.Abs(dr) + Mathf.Abs(ds)) / 2;
        }

        /// <summary> 直线（包含起点与终点）。</summary>
        public static IEnumerable<Hex> Line(Hex a, Hex b)
        {
            int N = Distance(a, b);
            if (N == 0) { yield return a; yield break; }
            for (int i = 0; i <= N; i++)
            {
                float t = (float)i / N;
                yield return Round(Lerp(a, b, t));
            }
        }

        static Vector3 Lerp(Hex a, Hex b, float t)
        {
            // 立方坐标插值
            float aq = a.q, ar = a.r, as_ = -a.q - a.r;
            float bq = b.q, br = b.r, bs_ = -b.q - b.r;
            float q = Mathf.Lerp(aq, bq, t);
            float r = Mathf.Lerp(ar, br, t);
            float s = Mathf.Lerp(as_, bs_, t);
            return new Vector3(q, r, s);
        }

        static Hex Round(Vector3 cube)
        {
            int rq = Mathf.RoundToInt(cube.x);
            int rr = Mathf.RoundToInt(cube.y);
            int rs = Mathf.RoundToInt(cube.z);
            float dq = Mathf.Abs(rq - cube.x);
            float dr = Mathf.Abs(rr - cube.y);
            float ds = Mathf.Abs(rs - cube.z);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new Hex(rq, rr);
        }

        /// <summary> 取环（恰好距离 = radius）。</summary>
        public static IEnumerable<Hex> Ring(Hex center, int radius)
        {
            if (radius == 0) { yield return center; yield break; }
            var c = center + Directions[4] * radius; // 任取起点
            for (int side = 0; side < 6; side++)
            {
                for (int i = 0; i < radius; i++)
                {
                    yield return c;
                    c += Directions[(side + 5) % 6];
                }
            }
        }

        /// <summary> 取菱形范围（<= radius）。</summary>
        public static IEnumerable<Hex> Range(Hex center, int radius)
        {
            for (int dq = -radius; dq <= radius; dq++)
            {
                int drMin = Mathf.Max(-radius, -dq - radius);
                int drMax = Mathf.Min(radius, -dq + radius);
                for (int dr = drMin; dr <= drMax; dr++)
                    yield return new Hex(center.q + dq, center.r + dr);
            }
        }
    }
}