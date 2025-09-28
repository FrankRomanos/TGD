// File: TGD.HexBoard/FootprintShape.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// �� Facing=PlusQ Ϊ��׼�ı���ƫ�ƣ�ǰ/���ᣩ
    [System.Serializable] public struct L2 { public int fwd; public int left; public L2(int f, int l) { fwd = f; left = l; } }

    [CreateAssetMenu(menuName = "TGD/HexBoard/Footprint Shape")]
    public class FootprintShape : ScriptableObject
    {
        [Tooltip("�� Facing=PlusQ Ϊ��׼��ƫ�ƣ�(0,0) �����������")]
        public List<L2> offsets = new() { new L2(0, 0) };
    }
}


