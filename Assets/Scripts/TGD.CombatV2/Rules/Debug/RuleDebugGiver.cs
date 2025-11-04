using System;
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
        public bool firstCostFreeTime;
        public bool firstCostFreeEnergy;
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
        readonly List<PendingVoucherUsage> _pendingVoucherUsage = new();
        UnitRuntimeContext _ctx;
        int _timeChargesRemaining;
        int _energyChargesRemaining;

        struct PendingVoucherUsage
        {
            public string actionId;
            public bool freeTime;
            public bool freeEnergy;
        }

        void OnEnable()
        {
            CAM.ActionResolved += HandleActionResolved;
            Apply();
        }

        void OnDisable()
        {
            CAM.ActionResolved -= HandleActionResolved;
            RemoveModifiers();
            TeardownListeners();
            _pendingVoucherUsage.Clear();
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
            TeardownListeners();
            ResetCharges();

            if (_ctx == null)
                return;

            ConfigureListeners();

            var ruleSet = _ctx.Rules;
            if (ruleSet == null)
            {
                ruleSet = new UnitRuleSet();
                _ctx.Rules = ruleSet;
            }

            if ((firstCostFreeTime || firstCostFreeEnergy) && !string.IsNullOrEmpty(firstCostFilter))
            {
                var voucher = new DebugCostVoucher();
                voucher.Initialize(this, firstCostFreeTime, firstCostFreeEnergy);
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

        void ResetCharges()
        {
            _pendingVoucherUsage.Clear();
            _timeChargesRemaining = firstCostFreeTime ? NormalizeCharges(firstCostCharges) : 0;
            _energyChargesRemaining = firstCostFreeEnergy ? NormalizeCharges(firstCostCharges) : 0;
        }

        static int NormalizeCharges(int charges)
        {
            return charges > 0 ? charges : -1;
        }

        static bool ChargesAvailable(int charges)
        {
            return charges != 0;
        }

        void ConsumeCharges(bool useTime, bool useEnergy)
        {
            if (useTime && _timeChargesRemaining > 0)
                _timeChargesRemaining = Mathf.Max(0, _timeChargesRemaining - 1);
            if (useEnergy && _energyChargesRemaining > 0)
                _energyChargesRemaining = Mathf.Max(0, _energyChargesRemaining - 1);
        }

        void HandleActionResolved(UnitRuntimeContext casterCtx, string actionId)
        {
            if (!_ctx || casterCtx != _ctx)
                return;

            if (string.IsNullOrEmpty(actionId))
                actionId = string.Empty;

            for (int i = 0; i < _pendingVoucherUsage.Count; i++)
            {
                var pending = _pendingVoucherUsage[i];
                if (!string.Equals(pending.actionId, actionId, StringComparison.OrdinalIgnoreCase))
                    continue;
                ConsumeCharges(pending.freeTime, pending.freeEnergy);
                _pendingVoucherUsage.RemoveAt(i);
                break;
            }
        }

        internal bool TryRegisterVoucherUsage(string actionId, bool wantsTime, bool wantsEnergy, out bool applyTime, out bool applyEnergy)
        {
            applyTime = wantsTime && ChargesAvailable(_timeChargesRemaining);
            applyEnergy = wantsEnergy && ChargesAvailable(_energyChargesRemaining);

            if (!applyTime && !applyEnergy)
                return false;

            if (string.IsNullOrEmpty(actionId))
                actionId = string.Empty;

            _pendingVoucherUsage.RemoveAll(p => string.Equals(p.actionId, actionId, StringComparison.OrdinalIgnoreCase));
            _pendingVoucherUsage.Add(new PendingVoucherUsage
            {
                actionId = actionId,
                freeTime = applyTime,
                freeEnergy = applyEnergy
            });

            return true;
        }

        internal bool IsAttackAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return false;
            return string.Equals(actionId, AttackProfileRules.DefaultActionId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionId, "Attack", StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsMoveAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return false;
            if (_ctx != null)
            {
                var moveId = _ctx.MoveActionId;
                if (!string.IsNullOrEmpty(moveId) && string.Equals(actionId, moveId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return string.Equals(actionId, MoveProfileRules.DefaultActionId, StringComparison.OrdinalIgnoreCase);
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
            RuleDebugGiver _owner;
            bool _freeTime;
            bool _freeEnergy;

            public void Initialize(RuleDebugGiver owner, bool freeTime, bool freeEnergy)
            {
                _owner = owner;
                _freeTime = freeTime;
                _freeEnergy = freeEnergy;
            }

            public void ModifyCost(in RuleContext ctx, ref int moveSecs, ref int atkSecs, ref int energyMove, ref int energyAtk)
            {
                if (_owner == null)
                    return;

                if (!_owner.TryRegisterVoucherUsage(ctx.actionId, _freeTime, _freeEnergy, out bool applyTime, out bool applyEnergy))
                    return;

                bool isAttack = _owner.IsAttackAction(ctx.actionId);
                bool isMove = _owner.IsMoveAction(ctx.actionId);

                if (applyTime)
                {
                    if (isAttack)
                        atkSecs = 0;
                    else if (isMove)
                        moveSecs = 0;
                    else
                    {
                        moveSecs = 0;
                        atkSecs = 0;
                    }
                }

                if (applyEnergy)
                {
                    if (isAttack)
                        energyAtk = 0;
                    else if (isMove)
                        energyMove = 0;
                    else
                    {
                        energyMove = 0;
                        energyAtk = 0;
                    }
                }
            }
        }

        sealed class DebugCooldownScaler : RuleModifierBase, ICooldownPolicy
        {
            public void OnStartCooldown(in RuleContext ctx, ref int startSeconds)
            {
                int turnTime = ctx.stats != null ? Mathf.Max(0, ctx.stats.TurnTime) : StatsMathV2.BaseTurnSeconds;
                if (turnTime <= 0)
                    turnTime = StatsMathV2.BaseTurnSeconds;
                if (startSeconds <= 0)
                    return;
                float scale = turnTime / (float)StatsMathV2.BaseTurnSeconds;
                startSeconds = Mathf.Max(0, Mathf.CeilToInt(startSeconds * scale));
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
