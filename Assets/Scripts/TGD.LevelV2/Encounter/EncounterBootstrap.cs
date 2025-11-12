using System.Collections.Generic;
using TGD.CoreV2;
using TGD.DataV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.LevelV2
{
    /// <summary>
    /// Encounter runtime bootstrapper. Reads an EncounterDef, spawns all units through UnitFactory,
    /// and kicks off the battle loop via TurnManagerV2.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EncounterBootstrap : MonoBehaviour
    {
        static readonly Color FriendlyColor = new Color(0.2f, 0.55f, 1f, 0.85f);
        static readonly Color EnemyColor = new Color(1f, 0.35f, 0.35f, 0.85f);

        [Header("Data")]
        public EncounterDef encounter;

        [Header("Factory")]
        public UnitFactory unitFactory;

        [Header("Board (optional overrides)")]
        [Tooltip("Explicit occupancy service reference. Left empty to auto-discover the first HexOccupancyService in scene.")]
        public HexOccupancyService occupancyService;

        [SerializeField]
        [Tooltip("Gizmo radius for spawn previews when the bootstrap is selected.")]
        float gizmoRadius = 0.25f;

        readonly HashSet<Hex> _validationScratch = new HashSet<Hex>();

        void Reset()
        {
            if (unitFactory == null)
                unitFactory = FindOne<UnitFactory>();
            if (occupancyService == null)
                occupancyService = FindOne<HexOccupancyService>();
        }

        void Start()
        {
            if (!Validate())
                return;

            if (encounter == null || unitFactory == null)
            {
                Debug.LogError("[Encounter] Missing encounter data or unit factory.", this);
                return;
            }

            var specs = encounter.SpawnSpecs;
            if (specs == null || specs.Count == 0)
            {
                Debug.LogWarning("[Encounter] EncounterDef has no spawn specs.", encounter);
                return;
            }

            bool originalAutoStart = unitFactory.autoStartBattle;
            unitFactory.autoStartBattle = false;

            int spawned = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                if (spec.blueprint == null)
                    continue;

                var faction = spec.blueprint.faction;
                var unit = unitFactory.Spawn(spec.blueprint, faction, spec.anchor, spec.facing);
                if (unit == null)
                    continue;

                ApplyFacing(unit, spec.facing);
                spawned++;
            }

            if (spawned > 0)
                unitFactory.StartBattle();
            else
                Debug.LogWarning("[Encounter] Encounter spawned zero units; StartBattle skipped.", this);

            unitFactory.autoStartBattle = originalAutoStart;
        }

        public bool Validate()
        {
            bool ok = true;

            if (encounter == null)
            {
                Debug.LogError("[Encounter] EncounterBootstrap requires an EncounterDef.", this);
                return false;
            }

            if (unitFactory == null)
            {
                unitFactory = FindOne<UnitFactory>();
                if (unitFactory == null)
                {
                    Debug.LogError("[Encounter] UnitFactory not assigned and auto-discovery failed.", this);
                    ok = false;
                }
            }

            if (unitFactory != null && unitFactory.skillIndex != null && unitFactory.actionCooldownCatalog == null)
            {
                Debug.LogWarning("[Encounter] UnitFactory has SkillIndex but no ActionCooldownCatalog; cooldown data may be stale.", unitFactory);
            }

            var specs = encounter.SpawnSpecs;
            _validationScratch.Clear();

            if (specs != null)
            {
                if (!TryResolveBoard(out var layout, out _) && specs.Count > 0)
                    Debug.LogWarning("[Encounter] Unable to resolve board layout; out-of-bounds validation skipped.", this);

                for (int i = 0; i < specs.Count; i++)
                {
                    var spec = specs[i];
                    if (spec.blueprint == null)
                    {
                        Debug.LogError($"[Encounter] Spawn spec {i} missing blueprint.", encounter);
                        ok = false;
                    }

                    if (!_validationScratch.Add(spec.anchor))
                    {
                        Debug.LogError($"[Encounter] Duplicate spawn anchor {spec.anchor} detected.", encounter);
                        ok = false;
                    }

                    if (layout != null && !layout.Contains(spec.anchor))
                    {
                        Debug.LogError($"[Encounter] Spawn anchor {spec.anchor} is outside the board bounds.", encounter);
                        ok = false;
                    }
                }
            }

            var occ = ResolveOccupancyService();
            if (occ != null)
                OccDiagnostics.AssertSingleStore(occ, "EncounterBootstrap.Validate");
            else
                Debug.LogWarning("[Encounter] No IOccupancyService found during validation.", this);

            return ok;
        }

        void ApplyFacing(Unit unit, Facing4 facing)
        {
            if (unit == null)
                return;

            unit.Facing = facing;

            if (UnitLocator.TryGetTransform(unit.Id, out var transform) && transform != null)
            {
                float yaw = HexFacingUtil.YawFromFacing(facing);
                var euler = transform.eulerAngles;
                transform.rotation = Quaternion.Euler(euler.x, yaw, euler.z);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (encounter == null)
                return;

            var specs = encounter.SpawnSpecs;
            if (specs == null || specs.Count == 0)
                return;

            if (!TryResolveBoard(out var layout, out var boardY))
                boardY = 0f;

            for (int i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                if (spec.blueprint == null)
                    continue;

                Vector3 world = layout != null
                    ? layout.World(spec.anchor, boardY)
                    : new Vector3(spec.anchor.q, boardY, spec.anchor.r);

                Gizmos.color = ResolveFactionColor(spec.blueprint.faction);
                Gizmos.DrawSphere(world, gizmoRadius);
            }
        }

        Color ResolveFactionColor(UnitFaction faction)
        {
            return faction == UnitFaction.Friendly ? FriendlyColor : EnemyColor;
        }

        bool TryResolveBoard(out HexBoardLayout layout, out float boardY)
        {
            layout = null;
            boardY = 0f;

            var occ = ResolveOccupancyService();
            if (occ != null && occ.authoring != null)
            {
                occ.authoring.Rebuild();
                layout = occ.authoring.Layout;
                boardY = occ.authoring.y;
                if (layout != null)
                    return true;
            }

            var space = HexSpace.Instance;
            if (space != null && space.Layout != null)
            {
                layout = space.Layout;
                boardY = space.DefaultY;
                return true;
            }

            return false;
        }

        HexOccupancyService ResolveOccupancyService()
        {
            if (occupancyService != null)
                return occupancyService;

            occupancyService = FindOne<HexOccupancyService>();
            return occupancyService;
        }

        static T FindOne<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<T>(true);
#endif
        }
    }
}
