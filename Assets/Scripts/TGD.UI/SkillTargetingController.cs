using System.Collections.Generic;
using UnityEngine;
using TGD.Combat;
using TGD.Data;
using TGD.Grid;
using TGD.Level;

namespace TGD.UI 
{ 
    public sealed class SkillTargetingController : BaseTurnUiBehaviour
    {
        [Header("Grid & Visuals")]
        public HexGridAuthoring grid;
        public HexRangeIndicator rangeIndicator;

        [Header("Camera")]
        public Camera worldCamera;
        public bool usePhysicsRaycast = false;
        public LayerMask groundMask = ~0;

        [Header("Input")]
        public KeyCode cancelKey = KeyCode.Escape;

        CombatViewBridge _bridge;
        ICombatEventBus _eventBus;
        Unit _activeUnit;
        SkillDefinition _pendingSkill;
        readonly HashSet<HexCoord> _validCoords = new();
        readonly Dictionary<HexCoord, Unit> _unitsByCoord = new();
        readonly Dictionary<Unit, HexCoord> _coordsByUnit = new();
        HexCoord _casterCoord;
        bool _selecting;

        public bool IsSelecting => _selecting;

        protected override void Awake()
        {
            base.Awake();
            if (!worldCamera)
                worldCamera = Camera.main;
            _bridge = CombatViewBridge.Instance ?? FindFirstObjectByTypeSafe<CombatViewBridge>();
            if (!grid && rangeIndicator)
                grid = rangeIndicator.grid;
            RefreshEventBus();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshEventBus();
        }

        protected override void OnDisable()
        {
            SubscribeEventBus(false);
            base.OnDisable();
        }

        protected override void HandleTurnBegan(Unit u)
        {
            _activeUnit = u;
            CancelSelection();
        }

        protected override void HandleTurnEnded(Unit u)
        {
            if (ReferenceEquals(_activeUnit, u))
            {
                CancelSelection();
                _activeUnit = null;
            }
        }

        public bool BeginSkillSelection(Unit caster, SkillDefinition skill)
        {
            if (skill == null || caster == null || combat == null)
                return false;

            if (!ReferenceEquals(_activeUnit, caster))
                _activeUnit = caster;

            if (!RequiresTarget(skill))
            {
                combat.ExecuteSkill(caster, skill, caster);
                return true;
            }

            if (!TryEnsureGrid())
                return false;

            if (_selecting && ReferenceEquals(_pendingSkill, skill))
            {
                CancelSelection();
                return false;
            }

            _pendingSkill = skill;
            BuildCoordinateMap();

            if (!_coordsByUnit.TryGetValue(caster, out _casterCoord) && !TryGetCoordinate(caster, out _casterCoord))
            {
                CancelSelection();
                return false;
            }

            BuildValidCoordinates(skill.range);
            if (_validCoords.Count == 0)
            {
                CancelSelection();
                return false;
            }

            rangeIndicator?.Show(_validCoords);
            _selecting = true;
            return true;
        }

        public void CancelSelection()
        {
            if (!_selecting)
            {
                rangeIndicator?.HideAll();
                return;
            }

            _selecting = false;
            _pendingSkill = null;
            _validCoords.Clear();
            rangeIndicator?.HideAll();
        }

        void Update()
        {
            RefreshEventBus();
            if (!_selecting)
                return;

            if ((cancelKey != KeyCode.None && Input.GetKeyDown(cancelKey)) || Input.GetMouseButtonDown(1))
            {
                CancelSelection();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (TryPickCoordinate(out var coord) &&
                    _validCoords.Contains(coord) &&
                    _unitsByCoord.TryGetValue(coord, out var target))
                {
                    if (combat.ExecuteSkill(_activeUnit, _pendingSkill, target))
                        CancelSelection();
                }
            }
        }

        void BuildCoordinateMap()
        {
            _unitsByCoord.Clear();
            _coordsByUnit.Clear();
            if (!TryEnsureGrid())
                return;

            var layout = grid.Layout;

            if (combat != null && combat.GridMap != null)
            {
                foreach (var kv in combat.GridMap.GetAllPositions())
                    RegisterUnitAt(kv.Key, kv.Value);
            }
            if (combat != null)
            {
                AddPartyUnits(combat.playerParty);
                AddPartyUnits(combat.enemyParty);
            }

            if (_bridge != null)
            {
                foreach (var actor in _bridge.EnumerateActors())
                {
                    if (actor == null)
                        continue;

                    var unit = actor.Model;
                    if (unit == null)
                        continue;

                    if (TryResolveActorCoordinate(unit, actor, layout, out var coord))
                        RegisterUnitAt(unit, coord);
                }
            }

            void AddPartyUnits(IEnumerable<Unit> party)
            {
                if (party == null)
                    return;

                foreach (var unit in party)
                {
                    if (unit == null)
                        continue;

                    if (_coordsByUnit.ContainsKey(unit))
                        continue;

                    var coord = NormalizeToLayout(unit.Position, layout);
                    if (!layout.Contains(coord) && TryResolveActorCoordinate(unit, null, layout, out var actorCoord))
                        coord = actorCoord;

                    RegisterUnitAt(unit, coord);
                }
            }
        }

        void BuildValidCoordinates(int range)
        {
            _validCoords.Clear();
            if (!TryEnsureGrid())
                return;

            range = Mathf.Max(0, range);
            foreach (var coord in grid.Layout.GetRange(_casterCoord, range))
                _validCoords.Add(coord);
        }

        bool TryPickCoordinate(out HexCoord coord)
        {
            coord = default;
            if (!TryEnsureGrid() || worldCamera == null)
                return false;

            Vector3 point;
            var ray = worldCamera.ScreenPointToRay(Input.mousePosition);
            if (usePhysicsRaycast)
            {
                if (!Physics.Raycast(ray, out var hit, 500f, groundMask, QueryTriggerInteraction.Collide))
                    return false;
                point = hit.point;
            }
            else
            {
                float y = grid.origin ? grid.origin.position.y : 0f;
                var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
                if (!plane.Raycast(ray, out float distance))
                    return false;
                point = ray.origin + ray.direction * distance;
            }

            coord = grid.Layout.GetCoordinate(point);
            return grid.Layout.Contains(coord);
        }

        bool TryGetCoordinate(Unit unit, out HexCoord coord)
        {
            coord = default;
            if (unit == null)
                return false;

            if (!TryEnsureGrid())
                return false;
            if (_coordsByUnit.TryGetValue(unit, out coord))
                return true;

            if (combat != null && combat.GridMap != null && combat.GridMap.TryGetPosition(unit, out coord))
            {
                RegisterUnitAt(unit, coord);
                return true;
            }

            if (grid?.Layout == null)
                return false;

            if (grid.Layout.Contains(unit.Position))
            {
                coord = unit.Position;
                RegisterUnitAt(unit, coord);
                return true;
            }

            if (_bridge != null && _bridge.TryGetActor(unit, out var actor) && actor &&
              TryResolveActorCoordinate(unit, actor, grid.Layout, out coord))
            {
                RegisterUnitAt(unit, coord);
                return true;
            }

            return false;
        }

        bool TryEnsureGrid()
        {
            if (grid && grid.Layout != null)
                return true;

            if (rangeIndicator && rangeIndicator.grid)
                grid = rangeIndicator.grid;

            if (!grid && combat != null && combat.ActiveGrid)
                grid = combat.ActiveGrid;

            if (grid && grid.Layout == null)
                grid.Rebuild();


            if (grid && grid.Layout != null)
                return true;

            if (_bridge != null)
            {
                foreach (var actor in _bridge.EnumerateActors())
                {
                    var resolved = actor?.ResolveGrid();
                    if (!resolved)
                        continue;

                    grid = resolved;
                    if (grid && grid.Layout == null)
                        grid.Rebuild();
                    if (grid && grid.Layout != null)
                        break;
                    
                }
            }

            return grid && grid.Layout != null;
        }
        void RegisterUnitAt(Unit unit, HexCoord coord)
        {
            if (unit == null)
                return;

            if (!TryEnsureGrid())
                return;

            var layout = grid.Layout;
            coord = NormalizeToLayout(coord, layout);

            if (_coordsByUnit.TryGetValue(unit, out var previous) && previous != coord)
            {
                if (_unitsByCoord.TryGetValue(previous, out var existing) && ReferenceEquals(existing, unit))
                    _unitsByCoord.Remove(previous);
            }

            _coordsByUnit[unit] = coord;
            _unitsByCoord[coord] = unit;
        }

        static HexCoord NormalizeToLayout(HexCoord coord, HexGridLayout layout)
        {
            if (layout == null)
                return coord;

            if (layout.Contains(coord))
                return coord;

            return layout.ClampToBounds(HexCoord.Zero, coord);
        }

        bool TryResolveActorCoordinate(Unit unit, UnitActor actor, HexGridLayout layout, out HexCoord coord)
        {
            coord = default;
            actor ??= (_bridge != null && unit != null && _bridge.TryGetActor(unit, out var found)) ? found : null;
            if (actor == null)
                return false;

            var gridForActor = actor.ResolveGrid();
            var targetLayout = gridForActor?.Layout ?? layout;
            if (targetLayout == null)
                return false;

            coord = targetLayout.GetCoordinate(actor.transform.position);
            if (layout != null && !ReferenceEquals(layout, targetLayout))
                coord = NormalizeToLayout(coord, layout);
            return true;
        }

        void RefreshEventBus()
        {
            SubscribeEventBus(true);
        }

        void SubscribeEventBus(bool on)
        {
            var next = combat?.EventBus;
            if (!on)
            {
                if (_eventBus != null)
                    _eventBus.OnUnitPositionChanged -= HandleUnitMoved;
                _eventBus = null;
                return;
            }

            if (ReferenceEquals(next, _eventBus))
                return;

            if (_eventBus != null)
                _eventBus.OnUnitPositionChanged -= HandleUnitMoved;

            _eventBus = next;
            if (_eventBus != null)
                _eventBus.OnUnitPositionChanged += HandleUnitMoved;
        }

        void HandleUnitMoved(Unit unit, HexCoord from, HexCoord to)
        {
            if (unit == null)
                return;

            RegisterUnitAt(unit, to);
        }

        static bool RequiresTarget(SkillDefinition skill)
        {
            return skill.targetType switch
            {
                SkillTargetType.None => false,
                SkillTargetType.Self => false,
                _ => true
            };
        }
    }
}
