using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.CoreV2.Rules;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class RuleDebugGiver : MonoBehaviour
    {
        [Header("First Cost Free")]
        public bool firstCostFree;
        public string firstCostFilter = "MOVE";
        public bool firstCostIsPrefix = true;
        public int firstCostCharges = 1;

        [Header("Cooldown Scaling")]
        public bool cooldownFollowsTurnTime;
        public string cooldownActionPrefix = "SK_";

        [Header("Linked Cooldowns")]
        public bool autoReduceCooldown;
        public string reduceTriggerId = "SK_A";
        public string reduceTargetId = "SK_B";
        public int reduceSeconds = 6;

        public bool autoRefreshCooldown;
        public string refreshTriggerId = "SK_A";
        public string refreshTargetId = "SK_B";

        [Header("Combo")]
        public bool ignoreComboPenalty;

        [Header("Attached Listeners (optional)")]
        public ReduceCooldownOnCast reduceListener;
        public RefreshCooldownOnCast refreshListener;

        readonly List<IRuleModifier> _activeModifiers = new();
        UnitRuntimeContext _ctx;

        void OnEnable()
        {
            Apply();
        }

        void OnDisable()
        {
            RemoveModifiers();
            TeardownListeners();
        }

        void OnValidate()
        {
            if (isActiveAndEnabled)
                Apply();
        }

        void Apply()
        {
            _ctx = GetComponentInParent<UnitRuntimeContext>(true);
            RemoveModifiers();
            ConfigureListeners();

            if (_ctx == null)
                return;

            var ruleSet = _ctx.Rules;
            if (ruleSet == null)
            {
                ruleSet = new UnitRuleSet();
                _ctx.Rules = ruleSet;
            }

            if (firstCostFree && !string.IsNullOrEmpty(firstCostFilter))
            {
                var voucher = new DebugCostVoucher();
                voucher.SetCharges(firstCostCharges);
                if (firstCostIsPrefix)
                    voucher.filter.actionIdStartsWith = firstCostFilter;
                else
                    voucher.filter.actionIdEquals = firstCostFilter;
                ruleSet.Add(voucher);
                _activeModifiers.Add(voucher);
            }

            if (cooldownFollowsTurnTime && !string.IsNullOrEmpty(cooldownActionPrefix))
            {
                var scaler = new DebugCooldownScaler();
                scaler.filter.actionIdStartsWith = cooldownActionPrefix;
                ruleSet.Add(scaler);
                _activeModifiers.Add(scaler);
            }

            if (ignoreComboPenalty)
            {
                var combo = new DebugComboPolicy();
                ruleSet.Add(combo);
                _activeModifiers.Add(combo);
            }
        }

        void RemoveModifiers()
        {
            if (_activeModifiers.Count == 0)
                return;
            if (_ctx != null)
            {
                var set = _ctx.Rules;
                if (set != null)
                {
                    for (int i = 0; i < _activeModifiers.Count; i++)
                    {
                        var mod = _activeModifiers[i];
                        if (mod != null)
                            set.Remove(mod);
                    }
                }
            }
            _activeModifiers.Clear();
        }

        void ConfigureListeners()
        {
            if (_ctx == null)
            {
                TeardownListeners();
                return;
            }

            ConfigureReduceListener();
            ConfigureRefreshListener();
        }

        void ConfigureReduceListener()
        {
            if (autoReduceCooldown)
            {
                if (reduceListener == null)
                    reduceListener = EnsureListener<ReduceCooldownOnCast>();
                if (reduceListener != null)
                {
                    reduceListener.enabled = true;
                    reduceListener.triggerActionId = reduceTriggerId;
                    reduceListener.targetActionId = reduceTargetId;
                    reduceListener.reduceSeconds = reduceSeconds;
                }
            }
            else
            {
                DestroyListener(ref reduceListener);
            }
        }

        void ConfigureRefreshListener()
        {
            if (autoRefreshCooldown)
            {
                if (refreshListener == null)
                    refreshListener = EnsureListener<RefreshCooldownOnCast>();
                if (refreshListener != null)
                {
                    refreshListener.enabled = true;
                    refreshListener.triggerActionId = refreshTriggerId;
                    refreshListener.targetActionId = refreshTargetId;
                }
            }
            else
            {
                DestroyListener(ref refreshListener);
            }
        }

        void TeardownListeners()
        {
            DestroyListener(ref reduceListener);
            DestroyListener(ref refreshListener);
        }

        T EnsureListener<T>() where T : Component
        {
            if (_ctx == null)
                return null;

            var existing = _ctx.GetComponent<T>();
            if (existing != null)
                return existing;

            return _ctx.gameObject.AddComponent<T>();
        }

        void DestroyListener<T>(ref T listener) where T : Component
        {
            if (listener == null)
                return;

            if (Application.isPlaying)
                Destroy(listener);
            else
                DestroyImmediate(listener);
            listener = null;
        }

        sealed class DebugCostVoucher : RuleModifierBase, ICostModifier
        {
            public int charges = 1;
            int _remaining;

            public DebugCostVoucher()
            {
                SetCharges(charges);
            }

            public void SetCharges(int value)
            {
                charges = value;
                _remaining = charges > 0 ? charges : int.MaxValue;
            }

            public override bool Matches(in RuleContext ctx)
            {
                if (_remaining == 0)
                    return false;
                return base.Matches(ctx);
            }

            public void ModifyCost(in RuleContext ctx, ref int secs, ref int energyMove, ref int energyAtk)
            {
                if (_remaining == 0)
                    return;

                secs = 0;
                energyMove = 0;
                energyAtk = 0;

                if (_remaining != int.MaxValue)
                    _remaining = Mathf.Max(0, _remaining - 1);
            }
        }

        sealed class DebugCooldownScaler : RuleModifierBase, ICooldownPolicy
        {
            public void OnStartCooldown(in RuleContext ctx, ref int startSeconds)
            {
            }

            public void OnTickCooldown(in RuleContext ctx, ref int tickDelta)
            {
                int turnTime = ctx.stats != null ? Mathf.Max(0, ctx.stats.TurnTime) : StatsMathV2.BaseTurnSeconds;
                tickDelta = -Mathf.Max(0, turnTime);
            }
        }

        sealed class DebugComboPolicy : RuleModifierBase, IComboPolicy
        {
            public void ModifyComboFactor(in RuleContext ctx, ref float factor)
            {
                factor = 1f;
            }
        }
    }
}
