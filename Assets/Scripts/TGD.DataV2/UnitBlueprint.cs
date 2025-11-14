using System;
using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// Scriptable blueprint describing the static data required to spawn a unit.
    /// </summary>
    [CreateAssetMenu(menuName = "TGD/Units/UnitBlueprint", fileName = "UnitBlueprint")]
    public sealed class UnitBlueprint : ScriptableObject
    {
        [Header("Identity")]
        public string unitId;
        public string displayName;
        public UnitFaction faction = UnitFaction.Friendly;

        [Header("Class / Specialization")]
        [Tooltip("Profession that governs the available specializations for this unit.")]
        public ClassSpec classSpec = new ClassSpec();

        [Header("Stats")]
        [Tooltip("Base StatsV2 snapshot copied to runtime on spawn.")]
        public StatsV2 baseStats = new StatsV2();

        [Header("Visual / Prefab")]
        [Tooltip("必须：预制体中应已包含 Unit、UnitRuntimeContext、CooldownHubV2 等外壳组件。")]
        public GameObject basePrefab;

        [Header("Avatar")]
        [Tooltip("Optional portrait sprite exposed to combat UI layers.")]
        public Sprite avatar;

        [Header("Hit Shape")]
        [Tooltip("Optional hit shape used when spawning this unit. Null uses factory fallback.")]
        public HitShape hitShape;

        [Header("Abilities")]
        [Tooltip("Initial ability loadout for this unit.")]
        public AbilitySlot[] abilities = new AbilitySlot[0];

        [Serializable]
        public struct ClassSpec
        {
            [Tooltip("Profession identifier (e.g. Knight, Samurai).")]
            public string professionId;

            [Tooltip("Specialization identifier constrained by the selected profession (e.g. CL001).")]
            public string specializationId;
        }

        [Serializable]
        public struct AbilitySlot
        {
            [SkillIdReference]
            public string skillId;
            public bool learned;
            [Min(0)]
            public int initialCooldownSeconds;
        }
    }
}
