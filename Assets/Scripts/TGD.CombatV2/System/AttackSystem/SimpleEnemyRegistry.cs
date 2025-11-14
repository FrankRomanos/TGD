// File: TGD.CombatV2/System/AttackSystem/SimpleEnemyRegistry.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
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
            public HitShape hitShape;
        }

        public HexOccupancyService occupancyService;
        public List<EnemyEntry> enemies = new();
        public bool autoPlaceOnStart = true;
        public bool debugLog;

        readonly Dictionary<Hex, EnemyActor> _actors = new();
        readonly HashSet<IGridActor> _actorSet = new();
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
                    _actorSet.Add(actor);
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
            if (_occ == null) { _actors.Clear(); _actorSet.Clear(); return; }
            foreach (var kv in _actors)
                _occ.Remove(kv.Value);
            _actors.Clear();
            _actorSet.Clear();
        }

        public bool IsEnemy(Hex hex) => IsEnemyAt(hex, _occ);

        public bool IsEnemyActor(IGridActor actor) => actor != null && _actorSet.Contains(actor);

        public bool IsEnemyAt(Hex hex, HexOccupancy occ)
        {
            if (occ != null && occ.TryGetActor(hex, out var actor) && actor != null)
                return IsEnemyActor(actor);
            return _actors.ContainsKey(hex);
        }

        public IEnumerable<Hex> AllEnemies => _actors.Keys;

        sealed class EnemyActor : IGridActor
        {
            readonly EnemyEntry _entry;
            readonly HitShape _hitShape;

            public EnemyActor(EnemyEntry entry)
            {
                _entry = entry;
                _hitShape = entry.hitShape != null ? entry.hitShape : CreateSingle();
                Anchor = entry.position;
                Facing = entry.facing;
            }

            public string Id => string.IsNullOrEmpty(_entry?.id) ? "Enemy" : _entry.id;
            public Hex Anchor { get; set; }
            public Facing4 Facing { get; set; }
            public HitShape HitShape => _hitShape;

            static HitShape CreateSingle()
            {
                var shape = ScriptableObject.CreateInstance<HitShape>();
                shape.name = "EnemyHitShape_Single_Runtime";
                shape.offsets = new() { new L2(0, 0) };
                return shape;
            }
        }
    }
}
