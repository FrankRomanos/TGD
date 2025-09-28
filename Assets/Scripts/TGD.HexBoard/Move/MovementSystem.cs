using System;

namespace TGD.HexBoard
{
    public enum MoveCardinal { Forward, Backward, Left, Right }

    public sealed class MoveOp
    {
        public Unit Actor;
        public MoveCardinal Direction;
        public int Distance;
        public bool AllowPartial = true;

        public MoveOp(Unit actor, MoveCardinal dir, int dist)
        { Actor = actor; Direction = dir; Distance = Math.Max(0, dist); }
    }

    /// <summary>
    /// 强制位移专用（击退/拖拽/冲锋等）。
    /// Flat-Top：R 竖直（↑↓），Q 水平（←→）；±Q 采用“两步修正”保持水平直线。
    /// </summary>
    public sealed class MovementSystem
    {
        readonly HexBoardLayout layout;
        readonly HexBoardMap<Unit> map;

        public MovementSystem(HexBoardLayout layout, HexBoardMap<Unit> map)
        { this.layout = layout; this.map = map; }

        public bool ExecuteForced(Unit unit, MoveCardinal dir, int distance, bool allowPartial = true)
        {
            if (unit == null || distance <= 0) return false;
            if (layout == null || map == null) return false;

            int baseIdx = DirIndexFromFacing(unit.Facing, dir);  // 0=+Q,3=-Q,5=+R,2=-R
            Hex offset = OffsetForCardinal(baseIdx, distance);
            if (offset.Equals(Hex.Zero)) return false;

            Hex cur = unit.Position;
            Hex target = layout.ClampToBounds(cur, cur + offset);

            Hex last = cur;
            foreach (var h in Hex.Line(cur, target))
            {
                if (h.Equals(cur)) continue;
                if (!layout.Contains(h) || (map != null && !map.IsFree(h))) break;
                last = h;
            }

            if (last.Equals(cur)) return false;
            if (!map.Move(unit, last)) return false;
            unit.Position = last;
            return true;
        }

        // 兼容旧签名
        public bool Execute(MoveOp op)
            => op != null && ExecuteForced(op.Actor, op.Direction, op.Distance, op.AllowPartial);

        // Facing + 四向 -> 基础六向索引
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

        // ±Q 两步修正；±R 纯竖直
        static Hex OffsetForCardinal(int baseIdx, int distance)
        {
            if (distance <= 0) return Hex.Zero;

            if (baseIdx == 5) return new Hex(0, +distance); // +R（下）
            if (baseIdx == 2) return new Hex(0, -distance); // -R（上）

            int pairs = distance / 2;
            bool odd = (distance & 1) == 1;

            if (baseIdx == 0) // +Q：右
            {
                var off = new Hex(+2 * pairs, -1 * pairs);
                if (odd) off += new Hex(+1, 0);
                return off;
            }
            else              // -Q：左
            {
                var off = new Hex(-2 * pairs, +1 * pairs);
                if (odd) off += new Hex(-1, 0);
                return off;
            }
        }
    }
}
