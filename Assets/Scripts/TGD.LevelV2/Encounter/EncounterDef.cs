using System;
using System.Collections.Generic;
using TGD.CoreV2;
using TGD.DataV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.LevelV2
{
    /// <summary>
    /// Scriptable definition describing the initial roster for an encounter.
    /// Lists which blueprint should spawn, at which hex coordinate, and facing direction.
    /// </summary>
    [CreateAssetMenu(menuName = "TGD/Combat/Encounter", fileName = "EncounterDef")]
    public sealed class EncounterDef : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Ordered list of spawn instructions consumed by EncounterBootstrap.")]
        List<SpawnSpec> spawnSpecs = new List<SpawnSpec>();

        public IReadOnlyList<SpawnSpec> SpawnSpecs => spawnSpecs;

        [Serializable]
        public struct SpawnSpec
        {
            [Tooltip("Blueprint to spawn via UnitFactory.")]
            public UnitBlueprint blueprint;

            [Tooltip("Axial hex coordinate (q,r) where the unit should appear.")]
            public Hex anchor;

            [Tooltip("Initial facing direction when the unit enters the board.")]
            public Facing4 facing;
        }

        [Serializable]
        public struct EnvStamp
        {
            public HazardType def;
            public Hex center;
            public int radius;
            public bool centerOnly;
            // Future: stacks, durationTurns...
        }

        public List<EnvStamp> envStamps = new();
    }
}
