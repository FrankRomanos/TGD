using System;
using System.Collections.Generic;
using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// Pure functions that compose runtime unit data from authoring blueprints.
    /// </summary>
    public static class UnitComposeService
    {
        /// <summary>
        /// Build a <see cref="FinalUnitConfig"/> from the provided blueprint.
        /// </summary>
        /// <param name="blueprint">Source blueprint.</param>
        /// <param name="skillIndex">Optional index used to validate skills and infer cooldown rules.</param>
        public static FinalUnitConfig Compose(UnitBlueprint blueprint, SkillIndex skillIndex = null)
        {
            if (blueprint == null)
                throw new ArgumentNullException(nameof(blueprint));

            var config = new FinalUnitConfig
            {
                unitId = blueprint.unitId,
                displayName = blueprint.displayName,
                faction = blueprint.faction,
                stats = new StatsV2(),
                avatar = blueprint.avatar,
                hitShape = blueprint.hitShape,
                professionId = NormalizeClassId(blueprint.classSpec.professionId),
                specializationId = NormalizeClassId(blueprint.classSpec.specializationId)
            };

            var sourceStats = blueprint.baseStats ?? new StatsV2();
            config.stats.ApplyInit(sourceStats);

            if (config.abilities == null)
                config.abilities = new List<FinalUnitConfig.LearnedAbility>();
            else
                config.abilities.Clear();

            if (blueprint.abilities != null)
            {
                foreach (var slot in blueprint.abilities)
                {
                    if (!slot.learned)
                        continue;

                    var normalizedId = NormalizeSkillId(slot.skillId);
                    if (string.IsNullOrEmpty(normalizedId))
                        continue;

                    if (!IsBuiltinSkill(normalizedId) && skillIndex != null && !skillIndex.Contains(normalizedId))
                    {
                        Debug.LogWarning($"[UnitCompose] SkillId not found in index: {slot.skillId}");
                        continue;
                    }

                    var ability = new FinalUnitConfig.LearnedAbility
                    {
                        skillId = normalizedId,
                        initialCooldownSeconds = Mathf.Max(0, slot.initialCooldownSeconds)
                    };

                    config.abilities.Add(ability);
                }
            }

            return config;
        }

        static string NormalizeClassId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        static string NormalizeSkillId(string skillId)
        {
            return string.IsNullOrWhiteSpace(skillId) ? null : skillId.Trim();
        }

        static bool IsBuiltinSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return false;

            return string.Equals(skillId, MoveProfileRules.DefaultSkillId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skillId, AttackProfileRules.DefaultSkillId, StringComparison.OrdinalIgnoreCase);
        }
    }

#if UNITY_EDITOR
    namespace Editor
    {
        using UnityEditor;
        using UnityEngine.Assertions;

        internal static class UnitComposeServiceEditorTests
        {
            private const string MenuPath = "TGD/DataV2/Run UnitComposeService Smoke Test";

            [MenuItem(MenuPath)]
            private static void Run()
            {
                var blueprint = ScriptableObject.CreateInstance<UnitBlueprint>();
                blueprint.unitId = "TestUnit";
                blueprint.displayName = "Test Unit";
                blueprint.classSpec.professionId = "Knight";
                blueprint.classSpec.specializationId = "CL001";
                blueprint.baseStats.MaxEnergy = 5;
                blueprint.baseStats.Energy = 12;
                blueprint.hitShape = ScriptableObject.CreateInstance<HitShape>();
                blueprint.hitShape.radius = 1;
                blueprint.abilities = new[]
                {
                    new UnitBlueprint.AbilitySlot
                    {
                        skillId = "ActionA",
                        learned = true,
                        initialCooldownSeconds = 6
                    },
                    new UnitBlueprint.AbilitySlot
                    {
                        skillId = AttackProfileRules.DefaultSkillId,
                        learned = true,
                        initialCooldownSeconds = 2
                    },
                    new UnitBlueprint.AbilitySlot
                    {
                        skillId = MoveProfileRules.DefaultSkillId,
                        learned = true,
                        initialCooldownSeconds = 0
                    },
                    new UnitBlueprint.AbilitySlot
                    {
                        skillId = "ActionB",
                        learned = false,
                        initialCooldownSeconds = 3
                    },
                    new UnitBlueprint.AbilitySlot
                    {
                        skillId = "ActionC",
                        learned = true,
                        initialCooldownSeconds = 4
                    }
                };

                var skillA = ScriptableObject.CreateInstance<SkillDefinitionV2>();
                skillA.EditorInitialize("ActionA", displayName: "Action A");

                var skillC = ScriptableObject.CreateInstance<SkillDefinitionV2>();
                skillC.EditorInitialize("ActionC", displayName: "Action C");

                var catalog = ScriptableObject.CreateInstance<SkillIndex>();
                catalog.entries.Add(new SkillIndex.Entry
                {
                    definition = skillA,
                    displayName = "Action A",
                    icon = null
                });
                catalog.entries.Add(new SkillIndex.Entry
                {
                    definition = skillC,
                    displayName = "Action C",
                    icon = null
                });

                var config = UnitComposeService.Compose(blueprint, catalog);

                Assert.AreEqual(5, config.stats.MaxEnergy);
                Assert.AreEqual(config.stats.MaxEnergy, config.stats.Energy);
                Assert.AreEqual(4, config.abilities.Count);
                Assert.AreEqual("ActionA", config.abilities[0].skillId);
                Assert.AreEqual(6, config.abilities[0].initialCooldownSeconds);
                Assert.AreEqual(AttackProfileRules.DefaultSkillId, config.abilities[1].skillId);
                Assert.AreEqual(2, config.abilities[1].initialCooldownSeconds);
                Assert.AreEqual(MoveProfileRules.DefaultSkillId, config.abilities[2].skillId);
                Assert.AreEqual(0, config.abilities[2].initialCooldownSeconds);
                Assert.AreEqual("ActionC", config.abilities[3].skillId);
                Assert.AreEqual(4, config.abilities[3].initialCooldownSeconds);
                Assert.AreEqual(blueprint.hitShape, config.hitShape);
                Assert.AreEqual("Knight", config.professionId);
                Assert.AreEqual("CL001", config.specializationId);

                Debug.Log("[UnitCompose] Smoke test passed.");

                ScriptableObject.DestroyImmediate(blueprint.hitShape);
                ScriptableObject.DestroyImmediate(blueprint);
                ScriptableObject.DestroyImmediate(catalog);
                ScriptableObject.DestroyImmediate(skillA);
                ScriptableObject.DestroyImmediate(skillC);
            }
        }
    }
#endif
}
