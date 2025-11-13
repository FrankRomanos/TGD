using System;
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    public enum ActionCoreKind
    {
        Unknown = 0,
        Skill = 1,
        BasicAttack = 2,
        Move = 3
    }

    public readonly struct ActionContextV2
    {
        private static readonly IReadOnlyList<UnitRuntimeContext> s_emptyTargets = Array.Empty<UnitRuntimeContext>();
        private static readonly IReadOnlyList<string> s_emptyTags = Array.Empty<string>();

        public ActionContextV2(
            UnitRuntimeContext actor,
            IReadOnlyList<UnitRuntimeContext> targets,
            ActionCoreKind coreKind,
            string actionId,
            ActionKind kind,
            IReadOnlyList<string> tags)
        {
            Actor = actor ?? throw new ArgumentNullException(nameof(actor));
            Targets = targets ?? s_emptyTargets;
            CoreKind = coreKind;
            ActionId = actionId ?? string.Empty;
            Kind = kind;
            Tags = tags ?? s_emptyTags;
        }

        public UnitRuntimeContext Actor { get; }

        public IReadOnlyList<UnitRuntimeContext> Targets { get; }

        public ActionCoreKind CoreKind { get; }

        public string ActionId { get; }

        public ActionKind Kind { get; }

        public IReadOnlyList<string> Tags { get; }
    }
}
