using System;
using System.Collections.Generic;

namespace TGD.Grid
{
    /// <summary>
    /// Tracks entity occupancy on a hex grid layout.
    /// </summary>
    public sealed class HexGridMap<T>
    {
        readonly Dictionary<HexCoord, HexGridCell<T>> _cells;
        readonly Dictionary<T, HexCoord> _positions;

        public HexGridMap(HexGridLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _cells = new Dictionary<HexCoord, HexGridCell<T>>(layout.Coordinates.Count);
            _positions = new Dictionary<T, HexCoord>();

            foreach (var coord in layout.Coordinates)
                _cells[coord] = new HexGridCell<T>(coord);
        }

        public HexGridLayout Layout { get; }

        public bool TryGetPosition(T entity, out HexCoord coord)
        {
            return _positions.TryGetValue(entity, out coord);
        }

        public bool SetPosition(T entity, HexCoord coord)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (!Layout.Contains(coord))
                return false;

            if (_positions.TryGetValue(entity, out var existing))
            {
                if (existing == coord)
                    return true;
                RemoveFromCell(entity, existing);
            }

            var cell = GetOrCreateCell(coord);
            cell.Add(entity);
            _positions[entity] = coord;
            return true;
        }

        public bool Move(T entity, HexCoord coord) => SetPosition(entity, coord);

        public bool Remove(T entity)
        {
            if (entity == null)
                return false;
            if (!_positions.TryGetValue(entity, out var coord))
                return false;

            RemoveFromCell(entity, coord);
            _positions.Remove(entity);
            return true;
        }

        public IReadOnlyCollection<T> GetEntities(HexCoord coord)
        {
            return _cells.TryGetValue(coord, out var cell)
                ? cell.Entities
                : Array.Empty<T>();
        }

        public bool HasAny(HexCoord coord)
        {
            return _cells.TryGetValue(coord, out var cell) && cell.Count > 0;
        }

        public T GetFirst(HexCoord coord)
        {
            if (_cells.TryGetValue(coord, out var cell))
                return cell.FirstOrDefault();
            return default;
        }

        public IEnumerable<KeyValuePair<T, HexCoord>> GetAllPositions() => _positions;

        public void Clear()
        {
            foreach (var cell in _cells.Values)
                cell.Clear();
            _positions.Clear();
        }

        HexGridCell<T> GetOrCreateCell(HexCoord coord)
        {
            if (_cells.TryGetValue(coord, out var existing))
                return existing;
            var cell = new HexGridCell<T>(coord);
            _cells[coord] = cell;
            return cell;
        }

        void RemoveFromCell(T entity, HexCoord coord)
        {
            if (_cells.TryGetValue(coord, out var cell))
                cell.Remove(entity);
        }
    }

    public sealed class HexGridCell<T>
    {
        readonly HashSet<T> _entities = new HashSet<T>();

        public HexGridCell(HexCoord coord)
        {
            Coordinate = coord;
        }

        public HexCoord Coordinate { get; }
        public IReadOnlyCollection<T> Entities => _entities;
        public int Count => _entities.Count;

        public bool Add(T entity) => _entities.Add(entity);
        public bool Remove(T entity) => _entities.Remove(entity);
        public void Clear() => _entities.Clear();

        public T FirstOrDefault()
        {
            foreach (var entity in _entities)
                return entity;
            return default;
        }
    }
}