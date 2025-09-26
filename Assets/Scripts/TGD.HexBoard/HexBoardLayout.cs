using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public enum HexOrient { FlatTop, PointyTop }

    /// <summary>
    /// 轴坐标 q,r ↔ 世界坐标 的标准六边形映射（蜂窝排布）。
    /// 不含 Yaw；严格使用数学公式，确保与你教程图3一致（整数坐标、行列交错）。
    /// cellSize = 从中心到顶点的距离（常称“半径”）。
    /// </summary>
    public sealed class HexBoardLayout
    {
        public readonly int width;   // q 方向数量
        public readonly int height;  // r 方向数量
        public readonly int minQ;
        public readonly int minR;
        public readonly Vector3 origin;
        public readonly float cellSize;   // 中心->顶点 的长度
        public readonly HexOrient orient;

        // 预计算矩阵（世界<->网格）
        readonly float f0, f1, f2, f3;   // axial->world
        readonly float b0, b1, b2, b3;   // world->axial

        public HexBoardLayout(int width, int height, float cellSize = 1f, Vector3? origin = null, int minQ = 0, int minR = 0, HexOrient orient = HexOrient.FlatTop)
        {
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);
            this.cellSize = Mathf.Max(1e-6f, cellSize);
            this.origin = origin ?? Vector3.zero;
            this.minQ = minQ; this.minR = minR;
            this.orient = orient;

            if (orient == HexOrient.FlatTop)
            {
                // x = s*(3/2*q)
                // z = s*(√3*(r + q/2))
                f0 = 3f / 2f; f1 = 0f; f2 = Mathf.Sqrt(3f) / 2f; f3 = Mathf.Sqrt(3f);
                // 逆矩阵（除以 s）
                b0 = 2f / 3f; b1 = 0f; b2 = -1f / 3f; b3 = 1f / Mathf.Sqrt(3f);
            }
            else // PointyTop
            {
                // x = s*(√3*(q + r/2))
                // z = s*(3/2*r)
                f0 = Mathf.Sqrt(3f); f1 = Mathf.Sqrt(3f) / 2f; f2 = 0f; f3 = 3f / 2f;
                b0 = 1f / Mathf.Sqrt(3f); b1 = -1f / 3f; b2 = 0f; b3 = 2f / 3f;
            }
        }

        public bool Contains(Hex h)
        {
            return h.q >= minQ && h.q < minQ + width && h.r >= minR && h.r < minR + height;
        }

        public IEnumerable<Hex> Coordinates()
        {
            for (int r = minR; r < minR + height; r++)
                for (int q = minQ; q < minQ + width; q++)
                    yield return new Hex(q, r);
        }

        public Vector3 World(Hex h, float y = 0f)
        {
            float x = (f0 * h.q + f1 * h.r) * cellSize;
            float z = (f2 * h.q + f3 * h.r) * cellSize;
            return new Vector3(origin.x + x, origin.y + y, origin.z + z);
        }

        public Hex HexAt(Vector3 world)
        {
            // 转回局部
            float lx = (world.x - origin.x) / cellSize;
            float lz = (world.z - origin.z) / cellSize;
            // 线性反解得到 axial 浮点
            float qf = b0 * lx + b1 * lz;
            float rf = b2 * lx + b3 * lz;
            // cube round
            return RoundAxial(qf, rf);
        }

        static Hex RoundAxial(float qf, float rf)
        {
            float sf = -qf - rf;
            int rq = Mathf.RoundToInt(qf);
            int rr = Mathf.RoundToInt(rf);
            int rs = Mathf.RoundToInt(sf);
            float dq = Mathf.Abs(rq - qf);
            float dr = Mathf.Abs(rr - rf);
            float ds = Mathf.Abs(rs - sf);
            if (dq > dr && dq > ds) rq = -rr - rs; else if (dr > ds) rr = -rq - rs;
            return new Hex(rq, rr);
        }

        public Hex ClampToBounds(Hex start, Hex candidate)
        {
            if (Contains(candidate)) return candidate;
            Hex last = start;
            foreach (var h in Hex.Line(start, candidate))
            {
                if (Contains(h)) last = h; else break;
            }
            return last;
        }

        // 便捷：相邻中心距离（世界）
        public float NeighborCenterDistance()
        {
            // 任取两个邻居做差
            var a = World(new Hex(0, 0));
            var b = World(Hex.Directions[0]);
            return Vector3.Distance(a, b);
        }
    }
}