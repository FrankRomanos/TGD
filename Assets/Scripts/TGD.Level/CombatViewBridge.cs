using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.Grid;


namespace TGD.Level
{
    /// <summary>
    /// Combat 视图桥：只亮当前出手者脚下环；显示伤害数字。
    /// 兼容：即使 CombatLoop 不抛事件，也会用轮询兜底。
    /// </summary>
    public class CombatViewBridge : MonoBehaviour, ICombatViewProbe
    {
        public static CombatViewBridge Instance { get; private set; }

        [Header("References")]
        [SerializeField] CombatLoop combatLoop;  // 可空，自动找

        [Header("Options")]
        public bool autoBindSceneActors = true;
        [Tooltip("兜底轮询间隔（秒），事件正常时开销极小")]
        public float pollInterval = 0.05f;

        // 索引
        readonly Dictionary<string, UnitActor> _actors =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Unit> _units =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Unit, CachedCoordinate> _coordCache = new();

        readonly struct CachedCoordinate
        {
            public CachedCoordinate(HexCoord coord, HexGridLayout layout)
            {
                Coord = coord;
                Layout = layout;
            }

            public HexCoord Coord { get; }
            public HexGridLayout Layout { get; }
        }

        CombatLoop _combat;
        ICombatEventBus _bus;
        Unit _lastActive;
        float _pollTimer;

        void Awake()
        {
            Instance = this;
            CombatViewServices.SceneProbe = this;
            _combat = combatLoop ? combatLoop : FindFirstObjectByTypeSafe<CombatLoop>();
            RefreshEventBus();

            BuildUnitIndex();
            SubscribeCombat(true);

            if (autoBindSceneActors)
            {
                foreach (var a in FindObjectsOfTypeSafe<UnitActor>(includeInactive: true))
                    TryBindActor(a);
            }

            HideAllRings();   // 初始全部关闭
        }

        void OnDestroy()
        {
            SubscribeCombat(false);
            if (_bus != null)
            {
                _bus.OnUnitPositionChanged -= HandleUnitMoved;
                _bus = null;
            }
            if (Instance == this) Instance = null;
            if (ReferenceEquals(CombatViewServices.SceneProbe, this))
                CombatViewServices.SceneProbe = null;
        }

        void Update()
        {
            RefreshEventBus();
            // 兜底轮询（ActiveUnit 改变也会触发）
            _pollTimer += Time.deltaTime;
            if (_pollTimer < pollInterval) return;
            _pollTimer = 0f;

            var cur = _combat ? _combat.GetActiveUnit() : null;
            if (!ReferenceEquals(cur, _lastActive))
            {
                _lastActive = cur;
                HideAllRings();
                if (cur != null && _actors.TryGetValue(cur.UnitId, out var a) && a)
                    a.ShowRing(true);
            }
        }

        // —— 对外：UnitActor 调用 —— //
        public void RegisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId) || !actor) return;
            _actors[unitId] = actor;
            actor.ShowRing(false);
            TryBindActor(actor);
            if (_lastActive != null && string.Equals(_lastActive.UnitId, unitId, StringComparison.OrdinalIgnoreCase))
                actor.ShowRing(true);  
        }

        public void UnregisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return;
            if (_actors.TryGetValue(unitId, out var cur) && cur == actor)
                _actors.Remove(unitId);
        }

        public void RefreshBindings()
        {
            BuildUnitIndex();
            foreach (var a in _actors.Values) TryBindActor(a);
            HideAllRings();
            _lastActive = null; // 强制下次轮询立即刷新
        }

        // —— Combat 事件（有就用） —— //
        void SubscribeCombat(bool on)
        {
            if (_combat == null) return;
            if (on)
            {
                _combat.OnTurnBegan += HandleTurnBegan;
                _combat.OnTurnEnded += HandleTurnEnded;
                _combat.OnDamageNumberRequested += HandleDamageNumber;
            }
            else
            {
                _combat.OnTurnBegan -= HandleTurnBegan;
                _combat.OnTurnEnded -= HandleTurnEnded;
                _combat.OnDamageNumberRequested -= HandleDamageNumber;
            }
        }
        void RefreshEventBus()
        {
            var next = _combat?.EventBus;
            if (ReferenceEquals(next, _bus))
                return;

            if (_bus != null)
                _bus.OnUnitPositionChanged -= HandleUnitMoved;

            _bus = next;
            if (_bus != null)
                _bus.OnUnitPositionChanged += HandleUnitMoved;
        }
        void HandleTurnBegan(Unit u)
        {
            HideAllRings();
            if (u != null && _actors.TryGetValue(u.UnitId, out var a) && a)
                a.ShowRing(true);
            _lastActive = u; // 让轮询和事件一致
        }

        void HandleTurnEnded(Unit u)
        {
            if (u != null && _actors.TryGetValue(u.UnitId, out var a) && a)
                a.ShowRing(false);
        }

        void HandleDamageNumber(Unit target, int amount, CombatLoop.DamageHint hint)
        {
            if (target == null) return;
            if (!_actors.TryGetValue(target.UnitId, out var a) || !a) return;

            var kind = hint switch
            {
                CombatLoop.DamageHint.Crit => DamageVisualKind.Crit,
                CombatLoop.DamageHint.Heal => DamageVisualKind.Heal,
                _ => DamageVisualKind.Normal
            };

            DamageNumberManager.ShowAt(
                a.DamageWorldPos,
                amount,
                kind,
                kind == DamageVisualKind.Crit ? 1.2f : 1f
            );
        }
        void HandleUnitMoved(Unit unit, HexCoord from, HexCoord to)
        {
            if (unit == null)
                return;

            CacheCoordinate(unit, to, _combat?.GridMap?.Layout);

            if (!_actors.TryGetValue(unit.UnitId, out var actor) || !actor)
                return;

            actor.ApplyCoordinate(to);
        }

        // —— 绑定 —— //
        void BuildUnitIndex()
        {
            _units.Clear();
            _coordCache.Clear();
            if (_combat == null) return;

            if (_combat.playerParty != null)
                foreach (var u in _combat.playerParty.Where(Ok))
                    _units[u.UnitId] = u;

            if (_combat.enemyParty != null)
                foreach (var u in _combat.enemyParty.Where(Ok))
                    _units[u.UnitId] = u;

            static bool Ok(Unit u) => u != null && !string.IsNullOrWhiteSpace(u.UnitId);
        }

        void TryBindActor(UnitActor actor)
        {
            if (!actor || string.IsNullOrWhiteSpace(actor.unitId)) return;
            if (_units.TryGetValue(actor.unitId, out var unit) && unit != null)
            {
                actor.Bind(unit);
                actor.TryTintRingByTeam(unit.TeamId);
                var preferredLayout = actor.ResolveGrid()?.Layout;
                if (TryGetCoordinate(unit, preferredLayout, out var coord))
                {
                    actor.ApplyCoordinate(coord);
                }
                else if (actor.TryResolveCoordinate(out var fallback))
                {
                    unit.Position = fallback;
                    CacheCoordinate(unit, fallback, preferredLayout);
                }
            }
        }

        void HideAllRings()
        {
            foreach (var kv in _actors) kv.Value.ShowRing(false);
        }

        public bool TryGetActor(Unit unit, out UnitActor actor)
        {
            actor = null;
            if (unit == null || string.IsNullOrWhiteSpace(unit.UnitId))
                return false;

            if (_actors.TryGetValue(unit.UnitId, out var found) && found)
            {
                actor = found;
                return true;
            }

            return false;
        }

        public IEnumerable<UnitActor> EnumerateActors()
        {
            foreach (var entry in _actors.Values)
            {
                if (entry)
                    yield return entry;
            }
        }
        IEnumerable<HexGridAuthoring> ICombatViewProbe.EnumerateKnownGrids()
        {
            foreach (var actor in _actors.Values)
            {
                var grid = actor?.ResolveGrid();
                if (grid && grid.Layout != null)
                    yield return grid;
            }
        }

        bool ICombatViewProbe.TryResolveUnitCoordinate(Unit unit, HexGridLayout referenceLayout, out HexCoord coord)
            => TryGetCoordinate(unit, referenceLayout, out coord);

        public bool TryGetCoordinate(Unit unit, out HexCoord coord)
            => TryGetCoordinate(unit, null, out coord);

        public bool TryGetCoordinate(Unit unit, HexGridLayout preferredLayout, out HexCoord coord)
        {
            coord = default;
            if (unit == null)
                return false;

            var map = _combat?.GridMap;
            if (map != null && map.TryGetPosition(unit, out var mapped))
            {
                CacheCoordinate(unit, mapped, map.Layout);
                coord = ConvertCoordinate(mapped, map.Layout, preferredLayout);
                return true;
            }

            if (_coordCache.TryGetValue(unit, out var cached))
            {
                coord = ConvertCoordinate(cached.Coord, cached.Layout, preferredLayout);
                return true;
            }

            if (TryResolveActorCoordinate(unit, out var actorCoord, out var actorLayout))
            {
                CacheCoordinate(unit, actorCoord, actorLayout);
                coord = ConvertCoordinate(actorCoord, actorLayout, preferredLayout);
                return true;
            }

            return false;
        }

        void CacheCoordinate(Unit unit, HexCoord coord, HexGridLayout layout)
        {
            if (unit == null)
                return;
            _coordCache[unit] = new CachedCoordinate(coord, layout);
        }

        bool TryResolveActorCoordinate(Unit unit, out HexCoord coord, out HexGridLayout layout)
        {
            coord = default;
            layout = null;
            if (unit == null)
                return false;

            if (!_actors.TryGetValue(unit.UnitId, out var actor) || !actor)
                return false;

            var grid = actor.ResolveGrid();
            layout = grid?.Layout;
            if (layout == null)
                return false;

            coord = layout.GetCoordinate(actor.transform.position);
            return true;
        }

        static HexCoord ConvertCoordinate(HexCoord coord, HexGridLayout sourceLayout, HexGridLayout targetLayout)
        {
            if (targetLayout == null)
                return coord;

            if (ReferenceEquals(sourceLayout, targetLayout) || sourceLayout == null)
            {
                if (targetLayout.Contains(coord))
                    return coord;
                return targetLayout.ClampToBounds(HexCoord.Zero, coord);
            }

            var world = sourceLayout.GetWorldPosition(coord);
            var converted = targetLayout.GetCoordinate(world);
            if (!targetLayout.Contains(converted))
                converted = targetLayout.ClampToBounds(HexCoord.Zero, converted);
            return converted;
        }


        // —— 查找工具（兼容 2023+）—— //
        static T FindFirstObjectByTypeSafe<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }

        static T[] FindObjectsOfTypeSafe<T>(bool includeInactive) where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            return includeInactive
                ? Resources.FindObjectsOfTypeAll<T>()
                : UnityEngine.Object.FindObjectsOfType<T>();
#endif
        }
    }
}
