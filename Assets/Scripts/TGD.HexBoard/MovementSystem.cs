using System;
using UnityEngine;

namespace TGD.HexBoard
{
    public enum MoveCardinal { Forward, Backward, Left, Right }

    public sealed class MoveOp
    {
        public Unit Actor;
        public MoveCardinal Direction;
        public int Distance;             // 格数
        public bool AllowPartial = true; // 遇阻是否部分前进
        public MoveOp(Unit actor, MoveCardinal dir, int dist)
        { Actor = actor; Direction = dir; Distance = Math.Max(0, dist); }
    }

    /// <summary>
    /// 极简、按格移动系统：
    /// - Flat-Top；R 轴为“视觉上下”
    /// - 命中 ±Q 时，采用两步修正以保持“水平直线”
    /// - 『点击移动』不要调这里，走你那套 BFS 路径；这里主要用于『强制位移类技能』
    /// </summary>
    public sealed class MovementSystem
    {
        readonly HexBoardLayout layout;
        readonly HexBoardMap<Unit> map; // 单占位

        public MovementSystem(HexBoardLayout layout, HexBoardMap<Unit> map)
        { this.layout = layout; this.map = map; }

        // === 强制位移（技能）：保持 ±Q 水平直线两步修正 ===
        public bool ExecuteForced(Unit unit, MoveCardinal dir, int distance, bool allowPartial = true)
        {
            if (unit == null || distance <= 0) return false;
            if (layout == null || map == null) return false;

            int baseIdx = DirIndexFromFacing(unit.Facing, dir); // 0=+Q,3=-Q,5=+R,2=-R
            Hex offset = OffsetForCardinal(baseIdx, distance);  // ★ 两步修正封装在这里
            if (offset.Equals(Hex.Zero)) return false;

            Hex cur = unit.Position;
            Hex target = cur + offset;

            // 边界裁剪到最后在界内的格
            target = layout.ClampToBounds(cur, target);

            // 逐格推进，遇阻停（按格安全）
            Hex lastFree = cur;
            foreach (var h in Hex.Line(cur, target))
            {
                if (h.Equals(cur)) continue;
                if (map.IsFree(h)) lastFree = h; else break;
            }

            if (lastFree.Equals(cur)) return false; // 原地不动

            // 提交
            if (!map.Move(unit, lastFree)) return false;
            unit.Position = lastFree;
            return true;
        }

        // 兼容旧调用（如果哪里还在用 MoveOp）
        public bool Execute(MoveOp op) =>
            op != null && ExecuteForced(op.Actor, op.Direction, op.Distance, op.AllowPartial);

        // --- 工具：四向 -> 基础六向索引 ---
        static int DirIndexFromFacing(Facing4 facing, MoveCardinal dir)
        {
            int forward = facing switch
            {
                Facing4.PlusQ => 0,
                Facing4.MinusQ => 3,
                Facing4.PlusR => 5,
                _ => 2 // Facing4.MinusR
            };

            return dir switch
            {
                MoveCardinal.Forward => forward,
                MoveCardinal.Backward => (forward + 3) % 6,
                MoveCardinal.Left => facing switch
                {
                    Facing4.PlusQ => 2, // -R
                    Facing4.MinusQ => 5, // +R
                    Facing4.PlusR => 0, // +Q
                    _ => 3, // -Q
                },
                MoveCardinal.Right => facing switch
                {
                    Facing4.PlusQ => 5, // +R
                    Facing4.MinusQ => 2, // -R
                    Facing4.PlusR => 3, // -Q
                    _ => 0, // +Q
                },
                _ => forward
            };
        }

        /// <summary>
        /// Flat-Top：R 为竖直轴。若 baseIdx 命中 ±Q，则用两步修正保证“水平直线”；
        /// 命中 ±R 则返回纯竖直位移。
        /// baseIdx: 0=+Q, 3=-Q, 5=+R, 2=-R
        /// </summary>
        static Hex OffsetForCardinal(int baseIdx, int distance)
        {
            if (distance <= 0) return Hex.Zero;

            // 竖直：±R
            if (baseIdx == 5) return new Hex(0, +distance); // +R（向下）
            if (baseIdx == 2) return new Hex(0, -distance); // -R（向上）

            // 水平：±Q —— 两步修正（E 与 NE 或 W 与 SW 组合）
            int pairs = distance / 2;
            bool odd = (distance & 1) == 1;

            if (baseIdx == 0) // +Q：右向水平线
            {
                var off = new Hex(+2 * pairs, -1 * pairs);
                if (odd) off += new Hex(+1, 0);
                return off;
            }
            else // baseIdx == 3  // -Q：左向水平线
            {
                var off = new Hex(-2 * pairs, +1 * pairs);
                if (odd) off += new Hex(-1, 0);
                return off;
            }
        }
    }
}
