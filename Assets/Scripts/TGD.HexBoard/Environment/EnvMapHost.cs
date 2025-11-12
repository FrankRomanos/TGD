using System;
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class EnvMapHost : MonoBehaviour
    {
        static readonly IReadOnlyList<CellEffect> EmptyEffects = Array.Empty<CellEffect>();

        readonly Dictionary<Hex, List<CellEffect>> _map = new();

        public void Clear()
        {
            foreach (var kvp in _map)
                kvp.Value.Clear();
            _map.Clear();
        }

        public void Stamp(HazardType def, Hex center, int radius, bool centerOnly)
        {
            if (def == null)
                return;

            int clampedRadius = Mathf.Max(0, radius);
            if (centerOnly || clampedRadius == 0)
            {
                StampCell(center, def);
                return;
            }

            foreach (var h in Hex.Range(center, clampedRadius))
                StampCell(h, def);
        }

        public bool HasKind(Hex h, HazardKind kind)
        {
            if (_map.TryGetValue(h, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].HasHazard && list[i].Kind == kind)
                        return true;
                }
            }

            return false;
        }

        public IReadOnlyList<CellEffect> Get(Hex h)
        {
            return _map.TryGetValue(h, out var list) ? list : EmptyEffects;
        }

        void StampCell(Hex h, HazardType def)
        {
            if (!_map.TryGetValue(h, out var list))
            {
                list = new List<CellEffect>();
                _map[h] = list;
            }

            list.Add(new CellEffect(def));
        }

        public readonly struct CellEffect
        {
            public readonly HazardType hazard;

            public CellEffect(HazardType hazard)
            {
                this.hazard = hazard;
            }

            public bool HasHazard => hazard != null;
            public HazardType Hazard => hazard;
            public HazardKind Kind => hazard != null ? hazard.kind : default;
        }
    }
}
