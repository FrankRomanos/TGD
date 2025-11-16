using System;
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.CoreV2.Rules;
using TGD.CombatV2;
using TGD.PlayV2;

namespace TGD.DebugV2
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
        bool _pendingEditorApply = false;
        bool _awaitingContext;
        struct PendingVoucherUsage
        {
            public string skillId;
            public bool freeTime;
            public bool freeEnergy;
        }

        void OnEnable()
        {
            CAM.ActionResolved += HandleActionResolved;
            CAM.ActionCancelled += HandleActionCancelled;
            Apply();
        }

        void OnDisable()
        {
            CAM.ActionResolved -= HandleActionResolved;
            CAM.ActionCancelled -= HandleActionCancelled;
            RemoveModifiers();
            TeardownListeners();
            _pendingVoucherUsage.Clear();
            _awaitingContext = false;
        }

        // 2) OnValidate 不再直接 Apply/Destroy，改为延迟重建（避免 DestroyImmediate 报错）
        void OnValidate()
        {
#if UNITY_EDITOR
            if (!isActiveAndEnabled) return;
            if (_pendingEditorApply) return;
            _pendingEditorApply = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                _pendingEditorApply = false;
                if (isActiveAndEnabled) Apply();
            };
#endif
        }


        // 3) Apply 前清理 Missing（编辑器专用），然后配置监听器
        void Apply()
        {
            Apply(null);
        }

        void Apply(UnitRuntimeContext forcedContext)
        {
            var previousCtx = _ctx;
            RemoveModifiers(previousCtx);             // 只移除规则，不要在这里先把监听器全砍掉
            ResetCharges();

            _ctx = forcedContext != null ? forcedContext : FindContext();          // ✅ 三向查找

            if (_ctx == null)
            {
                _awaitingContext = true;
                TeardownListeners();
                return;
            }

            _awaitingContext = false;

            if (previousCtx != _ctx)
                TeardownListeners();

#if UNITY_EDITOR
            UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(_ctx.gameObject);
#endif

            // 👉 改为“按开关配置”，内部会 create 或 destroy
            ConfigureListeners();

            var ruleSet = _ctx.Rules;
            if (ruleSet == null) return; // 推荐 UCTX: public UnitRuleSet Rules { get; } = new UnitRuleSet();

            // === First Cost Free ===
            if ((firstCostFreeTime || firstCostFreeEnergy) && !string.IsNullOrEmpty(firstCostFilter))
            {
                var voucher = new DebugCostVoucher();
                voucher.Initialize(this, firstCostFreeTime, firstCostFreeEnergy);
                if (firstCostIsPrefix) voucher.filter.skillIdStartsWith = firstCostFilter;
                else voucher.filter.skillIdEquals = firstCostFilter;
                ruleSet.Add(voucher);
                _activeModifiers.Add(voucher);
            }

            // === 冷却随“真实回合时间”（先全局生效；以后做 per-key 再加前缀过滤） ===
            if (cooldownFollowsTurnTime)
            {
                var scaler = new DebugCooldownScaler();
                // 若 TMV2 还是“全局 tick”，不要设置 action 过滤，否则匹配不上
                // 如果已经是 per-key tick，再打开这一行：
                // scaler.filter.skillIdStartsWith = cooldownActionPrefix;
                ruleSet.Add(scaler);
                _activeModifiers.Add(scaler);
            }

            // === 忽略连击惩罚 ===
            if (ignoreComboPenalty)
            {
                var combo = new DebugComboPolicy();
                ruleSet.Add(combo);
                _activeModifiers.Add(combo);
            }
        }


        void RemoveModifiers(UnitRuntimeContext contextOverride = null)
        {
            if (_activeModifiers.Count == 0)
                return;
            var ctx = contextOverride ?? _ctx;
            if (ctx != null)
            {
                var set = ctx.Rules;
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

        void Update()
        {
            if (!_awaitingContext)
                return;
            if (!isActiveAndEnabled)
                return;
            var ctx = FindContext();
            if (ctx == null)
                return;
            Apply(ctx);
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

        void HandleActionResolved(UnitRuntimeContext casterCtx, string skillId)
        {
            if (!_ctx || casterCtx != _ctx)
                return;

            if (string.IsNullOrEmpty(skillId))
                skillId = string.Empty;

            for (int i = 0; i < _pendingVoucherUsage.Count; i++)
            {
                var pending = _pendingVoucherUsage[i];
                if (!string.Equals(pending.skillId, skillId, StringComparison.OrdinalIgnoreCase))
                    continue;
                ConsumeCharges(pending.freeTime, pending.freeEnergy);
                _pendingVoucherUsage.RemoveAt(i);
                break;
            }
        }

        void HandleActionCancelled(UnitRuntimeContext casterCtx, string skillId)
        {
            if (!_ctx || casterCtx != _ctx)
                return;

            CancelPendingLocal(skillId);
        }

        internal bool TryRegisterVoucherUsage(string skillId, bool wantsTime, bool wantsEnergy, out bool applyTime, out bool applyEnergy)
        {
            applyTime = wantsTime && ChargesAvailable(_timeChargesRemaining);
            applyEnergy = wantsEnergy && ChargesAvailable(_energyChargesRemaining);

            if (!applyTime && !applyEnergy)
                return false;

            if (string.IsNullOrEmpty(skillId))
                skillId = string.Empty;

            _pendingVoucherUsage.RemoveAll(p => string.Equals(p.skillId, skillId, StringComparison.OrdinalIgnoreCase));
            _pendingVoucherUsage.Add(new PendingVoucherUsage
            {
                skillId = skillId,
                freeTime = applyTime,
                freeEnergy = applyEnergy
            });

            return true;
        }

        internal void CancelPendingLocal(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                skillId = string.Empty;

            for (int i = _pendingVoucherUsage.Count - 1; i >= 0; --i)
            {
                var pending = _pendingVoucherUsage[i];
                if (!string.Equals(pending.skillId, skillId, StringComparison.OrdinalIgnoreCase))
                    continue;
                _pendingVoucherUsage.RemoveAt(i);
            }
        }

        internal bool IsAttackAction(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return false;
            return string.Equals(skillId, AttackProfileRules.DefaultSkillId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(skillId, "Attack", StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsMoveAction(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return false;
            if (_ctx != null)
            {
                var moveId = _ctx.MoveSkillId;
                if (!string.IsNullOrEmpty(moveId) && string.Equals(skillId, moveId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return string.Equals(skillId, MoveProfileRules.DefaultSkillId, StringComparison.OrdinalIgnoreCase);
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
                    reduceListener.triggerSkillId = reduceTriggerId;
                    reduceListener.targetSkillId = reduceTargetId;
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
                    refreshListener.triggerSkillId = refreshTriggerId;
                    refreshListener.targetSkillId = refreshTargetId;
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
            var go = _ctx.gameObject;                     // ✅ 统一挂到 UCTX 所在的 GameObject 上
            if (!cache || cache.gameObject != go)
            {
                if (!go.TryGetComponent<T>(out cache))
                    cache = go.AddComponent<T>();
            }
#if UNITY_EDITOR
            // 让 Inspector 里能看到引用
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(go);
#endif
            return cache;
        }

        void DestroyListener<T>(ref T listener) where T : Component
        {
            if (!listener) { listener = null; return; }
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(listener); listener = null; return; }
#endif
            Destroy(listener);
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

                if (!_owner.TryRegisterVoucherUsage(ctx.skillId, _freeTime, _freeEnergy, out bool applyTime, out bool applyEnergy))
                    return;

                bool isAttack = _owner.IsAttackAction(ctx.skillId);
                bool isMove = _owner.IsMoveAction(ctx.skillId);

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
        // ===== 在类里加一个工具方法 =====
        UnitRuntimeContext FindContext()
        {
            // 先找自己
            if (TryGetComponent<UnitRuntimeContext>(out var self) && self) return self;
            // 再找父层（包含隐藏/未激活）
            var up = GetComponentInParent<UnitRuntimeContext>(true);
            if (up) return up;
            // 最后找子层（包含隐藏/未激活）
            var down = GetComponentInChildren<UnitRuntimeContext>(true);
            if (down) return down;
            return null;
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
