using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public static class DemoUtilities
    {
        /// <summary> �����е�λ���������ꡱ���뵽���Ը����ģ����ڻغϿ�ʼ�Զ�У������
        /// ���ﷵ��һ���б�����ÿ����λӦ�����õ����������꣨���ϲ� View ȥ���� Transform����
        /// </summary>
        public static List<(Unit unit, Vector3 world)> SnapAllToWorld(HexBoardLayout layout, IEnumerable<Unit> units, float y = 0f)
        {
            var list = new List<(Unit, Vector3)>();
            foreach (var u in units)
            {
                var w = layout.World(u.Position, y);
                list.Add((u, w));
            }
            return list;
        }

        public static IEnumerable<Vector3> WorldPoints(HexBoardLayout layout, IEnumerable<Hex> cells, float y = 0f)
        {
            foreach (var c in cells) yield return layout.World(c, y);
        }

        /// <summary> ȡ������ = r������ʾ�����񼯣���ֱ��ι����Ⱦ�߼����������ɡ�</summary>
        public static IEnumerable<Hex> Ring(Hex center, int r, HexBoardLayout layout)
        {
            foreach (var h in Hex.Ring(center, r)) if (layout.Contains(h)) yield return h;
        }

        /// <summary> ȡ������ �� r���ķ�Χ���񼯣���</summary>
        public static IEnumerable<Hex> Range(Hex center, int r, HexBoardLayout layout)
        {
            foreach (var h in Hex.Range(center, r)) if (layout.Contains(h)) yield return h;
        }
    }
}

