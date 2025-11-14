// File: TGD.HexBoard/HitShape.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// Facing=PlusQ 为基准的偏移量。
    [System.Serializable]
    public struct L2
    {
        public int fwd;
        public int left;
        public L2(int f, int l) { fwd = f; left = l; }
    }

    [CreateAssetMenu(menuName = "TGD/HexBoard/Hit Shape")]
    public class HitShape : ScriptableObject
    {
        [Tooltip("Facing=PlusQ 为基准的偏移量列表，默认仅包含自身格子 (0,0)")]
        public List<L2> offsets = new() { new L2(0, 0) };
    }
}
