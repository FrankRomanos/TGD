// File: TGD.HexBoard/FootprintShape.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 以 Facing=PlusQ 为基准的本地偏移（前/左轴）
    [System.Serializable] public struct L2 { public int fwd; public int left; public L2(int f, int l) { fwd = f; left = l; } }

    [CreateAssetMenu(menuName = "TGD/HexBoard/Footprint Shape")]
    public class FootprintShape : ScriptableObject
    {
        [Tooltip("以 Facing=PlusQ 为基准的偏移；(0,0) 必须包含自身")]
        public List<L2> offsets = new() { new L2(0, 0) };
    }
}


