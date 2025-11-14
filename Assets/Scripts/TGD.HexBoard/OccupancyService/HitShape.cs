// File: TGD.HexBoard/HitShape.cs
using UnityEngine;

namespace TGD.HexBoard
{
    [CreateAssetMenu(menuName = "TGD/HexBoard/Hit Shape")]
    public class HitShape : ScriptableObject
    {
        [Min(0)]
        [Tooltip("半径：0 表示仅锚点；半径 1/2/... 表示锚点周围的环形区域。")]
        public int radius = 1;
    }
}
