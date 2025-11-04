using System;
using System.Collections.Generic;

namespace TGD.CoreV2.Rules
{
    public struct RuleCostApplication
    {
        public string actionId;
        public int chainDepth;
        public int originalMoveSecs;
        public int originalAtkSecs;
        public int originalMoveEnergy;
        public int originalAtkEnergy;
        public int finalMoveSecs;
        public int finalAtkSecs;
        public int finalMoveEnergy;
        public int finalAtkEnergy;

        public bool HasChanges =>
            originalMoveSecs != finalMoveSecs ||
            originalAtkSecs != finalAtkSecs ||
            originalMoveEnergy != finalMoveEnergy ||
            originalAtkEnergy != finalAtkEnergy;
    }

    public sealed class RuleRuntimeLedger
    {
        readonly List<RuleCostApplication> _costApplications = new();

        public void RecordCost(in RuleCostApplication application)
        {
            if (!application.HasChanges)
                return;
            _costApplications.Add(application);
        }

        public bool TryConsumeCost(string actionId, int chainDepth, out RuleCostApplication application)
        {
            for (int i = 0; i < _costApplications.Count; i++)
            {
                var entry = _costApplications[i];
                if (Matches(entry, actionId, chainDepth))
                {
                    application = entry;
                    _costApplications.RemoveAt(i);
                    return true;
                }
            }

            application = default;
            return false;
        }

        public bool TryDiscardCost(string actionId, int chainDepth)
        {
            for (int i = 0; i < _costApplications.Count; i++)
            {
                if (Matches(_costApplications[i], actionId, chainDepth))
                {
                    _costApplications.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void Clear()
        {
            _costApplications.Clear();
        }

        static bool Matches(in RuleCostApplication entry, string actionId, int chainDepth)
        {
            if (!string.Equals(entry.actionId, actionId, StringComparison.Ordinal))
                return false;
            return entry.chainDepth == chainDepth;
        }
    }
}
