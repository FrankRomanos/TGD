using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2
{
    /// Facing=PlusQ 为准的偏移（前/左）
    [System.Serializable]
    public struct L2
    {
        public int fwd;
        public int left;

        public L2(int f, int l)
        {
            fwd = f;
            left = l;
        }
    }

    [CreateAssetMenu(menuName = "TGD/HexBoard/Footprint Shape")]
    public class FootprintShape : ScriptableObject
    {
        [Tooltip(" Facing=PlusQ 为准的偏移，(0,0) 代表站在 anchor 上 ")]
        public List<L2> offsets = new() { new L2(0, 0) };
    }
}
