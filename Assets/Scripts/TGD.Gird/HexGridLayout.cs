using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Grid
{
    /// <summary>
    /// Describes the world-space layout of a bounded hex grid and provides
    /// cached coordinate conversions.
    /// </summary>
    public sealed class HexGridLayout
    {
        readonly Dictionary<HexCoord, Vector3> _worldPositions;
        readonly List<HexCoord> _coordinates;

        public HexGridLayout(int width, int height, float hexRadius, HexOrientation orientation = HexOrientation.FlatTop, HexOffsetMode offsetMode = HexOffsetMode.OddRow, Vector3 origin = default)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (hexRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(hexRadius));

            Width = width;
            Height = height;
            HexRadius = hexRadius;
            Orientation = orientation;
            OffsetMode = offsetMode;
            Origin = origin;

            _worldPositions = new Dictionary<HexCoord, Vector3>(width * height);
            _coordinates = new List<HexCoord>(width * height);

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    var coord = HexCoord.FromOffset(x, z, offsetMode);
                    if (_worldPositions.ContainsKey(coord))
                        continue;
                    _worldPositions[coord] = coord.ToWorldPosition(hexRadius, orientation, origin);
                    _coordinates.Add(coord);
                }
            }
        }

        public int Width { get; }
        public int Height { get; }
        public float HexRadius { get; }
        public HexOrientation Orientation { get; }
        public HexOffsetMode OffsetMode { get; }
        public Vector3 Origin { get; }

        public IReadOnlyList<HexCoord> Coordinates => _coordinates;

        public bool Contains(HexCoord coord)
        {
            if (_worldPositions.ContainsKey(coord))
                return true;
            var offset = coord.ToOffset(OffsetMode);
            return offset.x >= 0 && offset.x < Width && offset.y >= 0 && offset.y < Height;
        }

        public Vector3 GetWorldPosition(HexCoord coord, float heightOffset = 0f)
        {
            if (_worldPositions.TryGetValue(coord, out var pos))
            {
                pos.y += heightOffset;
                return pos;
            }

            var computed = coord.ToWorldPosition(HexRadius, Orientation, Origin);
            computed.y += heightOffset;
            return computed;
        }

        public HexCoord GetCoordinate(Vector3 world)
        {
            return HexCoord.FromWorldPosition(world, HexRadius, Orientation, Origin);
        }

        public HexCoord ClampToBounds(HexCoord start, HexCoord candidate)
        {
            if (Contains(candidate))
                return candidate;

            HexCoord lastValid = start;
            foreach (var step in HexCoord.GetLine(start, candidate))
            {
                if (!Contains(step))
                    break;
                lastValid = step;
            }
            return lastValid;
        }

        public IEnumerable<HexCoord> GetNeighbors(HexCoord coord)
        {
            foreach (var direction in HexCoord.Directions)
            {
                var neighbor = coord + direction;
                if (Contains(neighbor))
                    yield return neighbor;
            }
        }

        public IEnumerable<HexCoord> GetRange(HexCoord center, int radius)
        {
            if (radius < 0)
                yield break;

            for (int dq = -radius; dq <= radius; dq++)
            {
                for (int dr = Math.Max(-radius, -dq - radius); dr <= Math.Min(radius, -dq + radius); dr++)
                {
                    int q = center.Q + dq;
                    int r = center.R + dr;
                    var coord = new HexCoord(q, r);
                    if (Contains(coord))
                        yield return coord;
                }
            }
        }
    }
}