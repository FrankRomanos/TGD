using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// <summary> 单占位网格：一个格最多 1 个实体。 </summary>
    public sealed class HexBoardMap<T>
    {
        readonly HexBoardLayout layout;
        readonly Dictionary<Hex, T> cells = new();
        readonly Dictionary<T, Hex> positions = new();

        public HexBoardMap(HexBoardLayout layout)
        {
            this.layout = layout;
        }

        public bool TryGetAt(Hex h, out T entity) => cells.TryGetValue(h, out entity);
        public bool IsFree(Hex h) => !cells.ContainsKey(h);
        public bool TryGetPosition(T e, out Hex h) => positions.TryGetValue(e, out h);

        public bool Set(T e, Hex h)
        {
            if (!layout.Contains(h)) return false;
            if (cells.ContainsKey(h)) return false; // 单占位

            if (positions.TryGetValue(e, out var old))
                cells.Remove(old);

            positions[e] = h;
            cells[h] = e;
            return true;
        }

        public bool Move(T e, Hex to)
        {
            if (!positions.ContainsKey(e)) return false;
            return Set(e, to);
        }

        public bool Remove(T e)
        {
            if (!positions.TryGetValue(e, out var h)) return false;
            positions.Remove(e);
            cells.Remove(h);
            return true;
        }
    }
}
