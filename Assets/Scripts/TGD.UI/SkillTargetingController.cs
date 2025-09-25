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
        Unit _activeUnit;
        SkillDefinition _pendingSkill;
        readonly HashSet<HexCoord> _validCoords = new();
        readonly Dictionary<HexCoord, Unit> _unitsByCoord = new();
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

            if (!TryGetCoordinate(caster, out _casterCoord))
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
            if (!TryEnsureGrid())
                return;

            if (_bridge == null)
                return;

            foreach (var actor in _bridge.EnumerateActors())
            {
                if (actor == null) continue;
                var unit = actor.Model;
                if (unit == null) continue;

                var coord = grid.Layout.GetCoordinate(actor.transform.position);
                _unitsByCoord[coord] = unit;
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

            if (grid.Layout.Contains(unit.Position))
            {
                coord = unit.Position;
                return true;
            }

            if (_bridge != null && _bridge.TryGetActor(unit, out var actor) && actor)
            {
                coord = grid.Layout.GetCoordinate(actor.transform.position);
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

            if (grid && grid.Layout != null)
                return true;

            if (_bridge != null)
            {
                foreach (var actor in _bridge.EnumerateActors())
                {
                    if (actor?.ResolveGrid() != null)
                    {
                        grid = actor.ResolveGrid();
                        break;
                    }
                }
            }

            return grid && grid.Layout != null;
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
