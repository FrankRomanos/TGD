using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Grid
{
    /// <summary>
    /// Describes the world-space layout of a bounded hex grid and provides
    /// cached coordinate conversions. Supports a global yaw (Y-rotation).
    /// </summary>
    public sealed class HexGridLayout
    {
        readonly Dictionary<HexCoord, Vector3> _worldPositions;
        readonly List<HexCoord> _coordinates;

        public int Width { get; }
        public int Height { get; }
        public float HexRadius { get; }
        public HexOrientation Orientation { get; }
        public HexOffsetMode OffsetMode { get; }
        public Vector3 Origin { get; }
        public float YawDegrees { get; }                // ★ 全局朝向（绕 Y）
        readonly Quaternion _yaw;                      // ★ 正旋
        readonly Quaternion _yawInv;                   // ★ 逆旋

        public IReadOnlyList<HexCoord> Coordinates => _coordinates;

        /// <param name="yawDegrees">global yaw (degrees) around Y-axis</param>
        public HexGridLayout(
            int width, int height, float hexRadius,
            HexOrientation orientation = HexOrientation.FlatTop,
            HexOffsetMode offsetMode = HexOffsetMode.OddRow,
            Vector3 origin = default,
            float yawDegrees = 0f)
        {
            Width = width;
            Height = height;
            HexRadius = hexRadius;
            Orientation = orientation;
            OffsetMode = offsetMode;
            Origin = origin;

            YawDegrees = yawDegrees;
            _yaw = Quaternion.Euler(0f, YawDegrees, 0f);
            _yawInv = Quaternion.Inverse(_yaw);

            _worldPositions = new Dictionary<HexCoord, Vector3>(width * height);
            _coordinates = new List<HexCoord>(width * height);

            // 预生成所有格子的世界坐标（带 Yaw）
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    var c = HexCoord.FromOffset(x, z, offsetMode);
                    if (_worldPositions.ContainsKey(c)) continue;

                    // 先算“网格本地”坐标，再施加 Yaw + Origin
                    var local = c.ToWorldPosition(hexRadius, orientation, Vector3.zero);
                    _worldPositions[c] = Origin + _yaw * local;
                    _coordinates.Add(c);
                }
            }
        }

        public bool Contains(HexCoord coord)
        {
            if (_worldPositions.ContainsKey(coord)) return true;
            var o = coord.ToOffset(OffsetMode);
            return o.x >= 0 && o.x < Width && o.y >= 0 && o.y < Height;
        }

        /// <summary>Hex -> World（带 Yaw）</summary>
        public Vector3 GetWorldPosition(HexCoord coord, float heightOffset = 0f)
        {
            if (_worldPositions.TryGetValue(coord, out var p))
            {
                p.y += heightOffset;
                return p;
            }
            var local = coord.ToWorldPosition(HexRadius, Orientation, Vector3.zero);
            var world = Origin + _yaw * local;
            world.y += heightOffset;
            return world;
        }

        /// <summary>World -> Hex（先逆旋再解算）</summary>
        public HexCoord GetCoordinate(Vector3 world)
        {
            var local = _yawInv * (world - Origin);
            return HexCoord.FromWorldPosition(local, HexRadius, Orientation, Vector3.zero);
        }

        public HexCoord ClampToBounds(HexCoord start, HexCoord candidate)
        {
            if (Contains(candidate)) return candidate;
            HexCoord last = start;
            foreach (var step in HexCoord.GetLine(start, candidate))
            {
                if (!Contains(step)) break;
                last = step;
            }
            return last;
        }

        public IEnumerable<HexCoord> GetNeighbors(HexCoord c)
        {
            foreach (var d in HexCoord.Directions)
            {
                var n = c + d;
                if (Contains(n)) yield return n;
            }
        }

        public IEnumerable<HexCoord> GetRange(HexCoord center, int radius)
        {
            if (radius < 0) yield break;
            for (int dq = -radius; dq <= radius; dq++)
            {
                for (int dr = Math.Max(-radius, -dq - radius); dr <= Math.Min(radius, -dq + radius); dr++)
                {
                    var c = new HexCoord(center.Q + dq, center.R + dr);
                    if (Contains(c)) yield return c;
                }
            }
        }
    }
}
