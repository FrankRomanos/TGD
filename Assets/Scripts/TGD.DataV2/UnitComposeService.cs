using System;
using System.Collections.Generic;
using TGD.CoreV2;
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
        /// <param name="actionCatalog">Optional catalog used to validate actions and infer cooldown rules.</param>
        public static FinalUnitConfig Compose(UnitBlueprint blueprint, ActionCatalog actionCatalog = null)
        {
            if (blueprint == null)
                throw new ArgumentNullException(nameof(blueprint));

            var config = new FinalUnitConfig
            {
                unitId = blueprint.unitId,
                displayName = blueprint.displayName,
                faction = blueprint.faction,
                stats = new StatsV2(),
                avatar = blueprint.avatar
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
                    if (string.IsNullOrWhiteSpace(slot.actionId))
                        continue;

                    if (actionCatalog != null && !actionCatalog.Contains(slot.actionId))
                    {
                        Debug.LogWarning($"[UnitCompose] ActionId not found in catalog: {slot.actionId}");
                        continue;
                    }

                    var ability = new FinalUnitConfig.LearnedAbility
                    {
                        actionId = slot.actionId,
                        initialCooldownSeconds = Mathf.Max(0, slot.initialCooldownSeconds)
                    };

                    if (actionCatalog != null && actionCatalog.IsNoCooldown(slot.actionId))
                    {
                        ability.initialCooldownSeconds = 0;
                    }

                    config.abilities.Add(ability);
                }
            }

            return config;
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
                blueprint.baseStats.MaxEnergy = 5;
                blueprint.baseStats.Energy = 12;
                blueprint.abilities = new[]
                {
                    new UnitBlueprint.AbilitySlot
                    {
                        actionId = "ActionA",
                        learned = true,
                        initialCooldownSeconds = 6
                    },
                    new UnitBlueprint.AbilitySlot
                    {
                        actionId = "ActionB",
                        learned = false,
                        initialCooldownSeconds = 3
                    },
                    new UnitBlueprint.AbilitySlot
                    {
                        actionId = "ActionC",
                        learned = true,
                        initialCooldownSeconds = 4
                    }
                };

                var catalog = ScriptableObject.CreateInstance<ActionCatalog>();
                catalog.entries.Add(new ActionCatalog.Entry
                {
                    actionId = "ActionA",
                    displayName = "Action A",
                    icon = null,
                    noCooldown = false
                });
                catalog.entries.Add(new ActionCatalog.Entry
                {
                    actionId = "ActionC",
                    displayName = "Action C",
                    icon = null,
                    noCooldown = true
                });

                var config = UnitComposeService.Compose(blueprint, catalog);

                Assert.AreEqual(5, config.stats.MaxEnergy);
                Assert.AreEqual(config.stats.MaxEnergy, config.stats.Energy);
                Assert.AreEqual(2, config.abilities.Count);
                Assert.AreEqual("ActionA", config.abilities[0].actionId);
                Assert.AreEqual(6, config.abilities[0].initialCooldownSeconds);
                Assert.AreEqual("ActionC", config.abilities[1].actionId);
                Assert.AreEqual(0, config.abilities[1].initialCooldownSeconds);

                Debug.Log("[UnitCompose] Smoke test passed.");

                ScriptableObject.DestroyImmediate(blueprint);
                ScriptableObject.DestroyImmediate(catalog);
            }
        }
    }
#endif
}
