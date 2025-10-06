// File: TGD.CombatV2/System/AttackSystem/SimpleEnemyRegistry.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class SimpleEnemyRegistry : MonoBehaviour, IEnemyLocator
    {
        [System.Serializable]
        public sealed class EnemyEntry
        {
            public string id = "Enemy";
            public Hex position = Hex.Zero;
            public Facing4 facing = Facing4.PlusQ;
            public FootprintShape footprint;
        }

        public HexOccupancyService occupancyService;
        public List<EnemyEntry> enemies = new();
        public bool autoPlaceOnStart = true;
        public bool debugLog;

        readonly Dictionary<Hex, EnemyActor> _actors = new();
        HexOccupancy _occ;

        void Awake()
        {
            _occ = occupancyService ? occupancyService.Get() : null;
        }

        void Start()
        {
            if (autoPlaceOnStart)
                EnsurePlaced();
        }

        void OnEnable()
        {
            if (autoPlaceOnStart)
                EnsurePlaced();
        }

        void OnDisable()
        {
            Clear();
        }

        void EnsurePlaced()
        {
            if (_occ == null) return;
            foreach (var entry in enemies)
            {
                if (entry == null) continue;
                if (_actors.ContainsKey(entry.position)) continue;
                var actor = new EnemyActor(entry);
                if (_occ.TryPlace(actor, entry.position, entry.facing))
                {
                    _actors[entry.position] = actor;
                    if (debugLog)
                        Debug.Log($"[EnemyRegistry] Placed enemy {entry.id} at {entry.position}", this);
                }
                else if (debugLog)
                {
                    Debug.LogWarning($"[EnemyRegistry] Failed to place enemy {entry.id} at {entry.position}", this);
                }
            }
        }

        void Clear()
        {
            if (_occ == null) { _actors.Clear(); return; }
            foreach (var kv in _actors)
                _occ.Remove(kv.Value);
            _actors.Clear();
        }

        public bool IsEnemy(Hex hex) => _actors.ContainsKey(hex);

        public IEnumerable<Hex> AllEnemies => _actors.Keys;

        sealed class EnemyActor : IGridActor
        {
            readonly EnemyEntry _entry;
            readonly FootprintShape _footprint;

            public EnemyActor(EnemyEntry entry)
            {
                _entry = entry;
                _footprint = entry.footprint != null ? entry.footprint : CreateSingle();
                Anchor = entry.position;
                Facing = entry.facing;
            }

            public string Id => string.IsNullOrEmpty(_entry?.id) ? "Enemy" : _entry.id;
            public Hex Anchor { get; set; }
            public Facing4 Facing { get; set; }
            public FootprintShape Footprint => _footprint;

            static FootprintShape CreateSingle()
            {
                var shape = ScriptableObject.CreateInstance<FootprintShape>();
                shape.name = "EnemyFootprint_Single_Runtime";
                shape.offsets = new() { new L2(0, 0) };
                return shape;
            }
        }
    }
}
