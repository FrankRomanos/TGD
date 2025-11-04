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
        [SerializeField, HideInInspector] ReduceCooldownOnCast reduceListener;
        [SerializeField, HideInInspector] RefreshCooldownOnCast refreshListener;

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

        // 2) OnValidate 不再直接 Apply/Destroy，改为延迟重建（避免 DestroyImmediate 报错）
        void OnValidate()
        {
#if UNITY_EDITOR
            if (!isActiveAndEnabled) return;
            UnityEditor.EditorApplication.delayCall += RebuildInEditor;
#endif
        }

#if UNITY_EDITOR
        void RebuildInEditor()
        {
            if (this == null) return;                  // 组件已被删除
            UnityEditor.EditorApplication.delayCall -= RebuildInEditor;
            if (!isActiveAndEnabled) return;
            Apply();
        }
#endif

        // 3) Apply 前清理 Missing（编辑器专用），然后配置监听器
        void Apply()
        {
            _ctx = GetComponentInParent<UnitRuntimeContext>(true);
            RemoveModifiers();
            TeardownListeners();
            ResetCharges();

            if (_ctx == null) return;

#if UNITY_EDITOR
            UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(_ctx.gameObject);
#endif

            ConfigureListeners();

            var ruleSet = _ctx.Rules;
            if (ruleSet == null) return;  // 由 UCTX 持有，不在这里 new / 赋值

            // ……voucher / scaler / combo 按你现有逻辑继续 Add 到 ruleSet
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
                reduceListener = EnsureListener(ref reduceListener);
                if (reduceListener)
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
                refreshListener = EnsureListener(ref refreshListener);
                if (refreshListener)
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

        // 4) 统一“确保/销毁”逻辑：始终用 Destroy（排队销毁），不要 DestroyImmediate
        T EnsureListener<T>(ref T cache) where T : Component
        {
            if (!_ctx) return null;
            if (!cache || cache.gameObject != _ctx.gameObject)
            {
                if (!_ctx.TryGetComponent<T>(out cache))
                    cache = _ctx.gameObject.AddComponent<T>();
            }
            return cache;
        }

        void DestroyListener<T>(ref T listener) where T : Component
        {
            if (!listener) { listener = null; return; }
            Destroy(listener);      // ✅ 始终排队销毁，避免“立即销毁”错误
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
            //  起始冷却不要缩放 —— 保持目录值
            public void OnStartCooldown(in RuleContext ctx, ref int startSeconds)
            {
                // 留空即可；如需个别技能特例，做单独 Policy
            }

            //  每回合 Tick = -(6 + Speed)，Speed 为“秒”
            public void OnTickCooldown(in RuleContext ctx, ref int tickDelta)
            {
                int sp = (ctx.stats != null) ? ctx.stats.Speed : 0;
                tickDelta = -(StatsMathV2.BaseTurnSeconds + sp); // 例如 - (6+3) = -9
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
