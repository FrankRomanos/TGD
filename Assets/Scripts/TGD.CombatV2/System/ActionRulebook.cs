using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CombatV2
{
    /// <summary>
    /// ScriptableObject 形式的规则书，提供 CAM 所需的动作规则查询。
    /// </summary>
    [CreateAssetMenu(menuName = "TGD/Rules/ActionRulebook")]
    public sealed class ActionRulebook : ScriptableObject, IActionRules
    {
        [Header("Idle 可裸用")]
        public bool allowStandardAtIdle = true;
        public bool allowReactionAtIdle = true;
        public bool allowFullRoundAtIdle = true;
        public bool allowSustainedAtIdle = true;
        public bool allowFreeAtIdle = false;
        public bool allowDerivedAtIdle = false;

        [Header("PhaseStartFree 首层动作")]
        public bool phaseStartAllowFree = true;

        [Header("反应约束")]
        public bool reactionWithinBaseTime = true;

        [Header("友军跨回合插入")]
        public bool allowFriendlyInsertions = false;

        [Serializable]
        public struct ChainMatrixRow
        {
            public ActionKind baseKind;
            public bool allowReaction;
            public bool allowFree;
            public bool allowDerived;
        }

        [Serializable]
        public struct RecursionRow
        {
            public ActionKind chosenKind;
            public bool allowReactionNext;
            public bool allowFreeNext;
            public bool allowDerivedNext;
        }

        public ChainMatrixRow[] firstLayerMatrix = new[]
        {
            new ChainMatrixRow { baseKind = ActionKind.Standard,  allowReaction = true, allowFree = true, allowDerived = true },
            new ChainMatrixRow { baseKind = ActionKind.Reaction,  allowReaction = false, allowFree = true, allowDerived = true },
            new ChainMatrixRow { baseKind = ActionKind.FullRound, allowReaction = true, allowFree = true, allowDerived = false },
            new ChainMatrixRow { baseKind = ActionKind.Sustained, allowReaction = true, allowFree = true, allowDerived = false },
            new ChainMatrixRow { baseKind = ActionKind.Free,      allowReaction = true, allowFree = true, allowDerived = false },
            new ChainMatrixRow { baseKind = ActionKind.Derived,   allowReaction = false, allowFree = true, allowDerived = false },
        };

        public RecursionRow[] recursionMatrix = new[]
        {
            new RecursionRow { chosenKind = ActionKind.Reaction, allowReactionNext = false, allowFreeNext = true,  allowDerivedNext = false },
            new RecursionRow { chosenKind = ActionKind.Derived,  allowReactionNext = true,  allowFreeNext = true,  allowDerivedNext = false },
        };

        static readonly ActionKind[] s_freeOnly = { ActionKind.Free };
        static readonly ActionKind[] s_empty = Array.Empty<ActionKind>();
        static readonly List<ActionKind> s_scratch = new();

        public bool CanActivateAtIdle(ActionKind kind)
        {
            return (kind == ActionKind.Standard && allowStandardAtIdle)
                || (kind == ActionKind.Reaction && allowReactionAtIdle)
                || (kind == ActionKind.FullRound && allowFullRoundAtIdle)
                || (kind == ActionKind.Sustained && allowSustainedAtIdle)
                || (kind == ActionKind.Free && allowFreeAtIdle)
                || (kind == ActionKind.Derived && allowDerivedAtIdle);
        }

        public IReadOnlyList<ActionKind> AllowedAtPhaseStartFree()
        {
            return phaseStartAllowFree ? s_freeOnly : s_empty;
        }

        public IReadOnlyList<ActionKind> AllowedChainFirstLayer(ActionKind baseKind, bool isEnemyPhase)
        {
            _ = isEnemyPhase;
            int index = Array.FindIndex(firstLayerMatrix, r => r.baseKind == baseKind);
            if (index < 0)
                return s_empty;

            var row = firstLayerMatrix[index];
            s_scratch.Clear();
            if (row.allowReaction)
                s_scratch.Add(ActionKind.Reaction);
            if (row.allowFree)
                s_scratch.Add(ActionKind.Free);
            if (row.allowDerived)
                s_scratch.Add(ActionKind.Derived);
            return s_scratch.Count > 0 ? new List<ActionKind>(s_scratch) : s_empty;
        }

        public IReadOnlyList<ActionKind> AllowedChainNextLayer(ActionKind chosenKind)
        {
            int index = Array.FindIndex(recursionMatrix, r => r.chosenKind == chosenKind);
            if (index < 0)
                return s_empty;

            var row = recursionMatrix[index];
            s_scratch.Clear();
            if (row.allowReactionNext)
                s_scratch.Add(ActionKind.Reaction);
            if (row.allowFreeNext)
                s_scratch.Add(ActionKind.Free);
            if (row.allowDerivedNext)
                s_scratch.Add(ActionKind.Derived);
            return s_scratch.Count > 0 ? new List<ActionKind>(s_scratch) : s_empty;
        }

        public bool ReactionMustBeWithinBaseTime() => reactionWithinBaseTime;

        public bool AllowFriendlyInsertions() => allowFriendlyInsertions;

        static IActionRules s_default;

        /// <summary>
        /// 在未指定 ScriptableObject 时使用的默认规则实现。
        /// </summary>
        public static IActionRules Default
        {
            get
            {
                if (s_default == null)
                {
                    var instance = CreateInstance<ActionRulebook>();
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    s_default = instance;
                }
                return s_default;
            }
        }
    }
}
