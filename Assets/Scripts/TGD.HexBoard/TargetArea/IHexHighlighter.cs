using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// Abstraction over hex highlighting so higher level systems do not depend
    /// directly on tiler details.
    /// </summary>
    public interface IHexHighlighter
    {
        void Paint(IEnumerable<Hex> cells, Color color);
        void Clear();
    }
}
