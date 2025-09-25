using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.UI;       // DamageNumberManager 用到

namespace TGD.Level
{
    /// <summary>
    /// 视图桥：把 CombatLoop 的回合/飘字事件，映射到场景里的 UnitActor。
    /// - 维护 UnitId→UnitActor 映射
    /// - 回合开始/结束：统一开/关脚下环（只亮当前出手者）
    /// - 请求飘字：调用 DamageNumberManager
    /// - 支持运行时重新绑定/刷新
    /// </summary>
    public class CombatViewBridge : MonoBehaviour
    {
        public static CombatViewBridge Instance { get; private set; }

        [Header("References")]
        [Tooltip("可选：若留空会自动查找场景中的 CombatLoop")]
        [SerializeField] private CombatLoop combatLoop;

        [Header("Options")]
        [Tooltip("Awake 时自动查找并绑定场景中的 UnitActor")]
        public bool autoBindSceneActors = true;

        // —— 索引 —— //
        readonly Dictionary<string, UnitActor> _actors =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Unit> _units =
            new(StringComparer.OrdinalIgnoreCase);

        CombatLoop _combat;

        // -------------- 生命周期 -------------- //
        void Awake()
        {
            Instance = this;

            // 1) 定位 CombatLoop（优先 Inspector 引用）
            _combat = combatLoop;
            if (_combat == null)
                _combat = FindFirstObjectByTypeSafe<CombatLoop>();

            // 2) 根据 CombatLoop 构建 Unit 索引，订阅事件
            BuildUnitIndex();
            SubscribeCombat(true);

            // 3) 绑定场景里的 UnitActor
            if (autoBindSceneActors)
            {
                foreach (var a in FindObjectsOfTypeSafe<UnitActor>(includeInactive: true))
                    TryBindActor(a);
            }

            // 4) 初始确保环全关
            HideAllRings();
        }

        void OnDestroy()
        {
            SubscribeCombat(false);
            if (Instance == this) Instance = null;
        }

        // -------------- 对外：供 UnitActor 调用 -------------- //
        public void RegisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId) || !actor) return;
            _actors[unitId] = actor;
            RegisterActor(actor.unitId, actor);
            actor.ShowRing(false); // 注册时默认关
        }

        public void UnregisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return;
            if (_actors.TryGetValue(unitId, out var cur) && cur == actor)
                _actors.Remove(unitId);
        }

        /// <summary>当你改了 CombatLoop 名单或场景里增删了单位时，手动调用刷新。</summary>
        public void RefreshBindings()
        {
            BuildUnitIndex();
            foreach (var a in _actors.Values) TryBindActor(a);
            HideAllRings();
        }

        // -------------- Combat 事件 -------------- //
        void SubscribeCombat(bool on)
        {
            if (_combat == null) return;
            if (on)
            {
                _combat.OnTurnBegan += HandleTurnBegan;
                _combat.OnTurnEnded += HandleTurnEnded;
                _combat.OnDamageNumberRequested += HandleDamageNumber;
            }
            else
            {
                _combat.OnTurnBegan -= HandleTurnBegan;
                _combat.OnTurnEnded -= HandleTurnEnded;
                _combat.OnDamageNumberRequested -= HandleDamageNumber;
            }
        }

        void HandleTurnBegan(Unit u)
        {
            // 先全关，再只亮当前
            HideAllRings();
            if (u != null && _actors.TryGetValue(u.UnitId, out var a) && a)
                a.ShowRing(true);
        }

        void HandleTurnEnded(Unit u)
        {
            if (u != null && _actors.TryGetValue(u.UnitId, out var a) && a)
                a.ShowRing(false);
        }

        void HandleDamageNumber(Unit target, int amount, CombatLoop.DamageHint hint)
        {
            if (target == null) return;
            if (!_actors.TryGetValue(target.UnitId, out var a) || !a) return;

            var kind = hint switch
            {
                CombatLoop.DamageHint.Crit => DamageVisualKind.Crit,
                CombatLoop.DamageHint.Heal => DamageVisualKind.Heal,
                _ => DamageVisualKind.Normal
            };

            DamageNumberManager.ShowAt(
                a.DamageWorldPos,
                amount,
                kind,
                kind == DamageVisualKind.Crit ? 1.2f : 1f
            );
        }

        // -------------- 绑定/索引 -------------- //
        void BuildUnitIndex()
        {
            _units.Clear();
            if (_combat == null) return;

            if (_combat.playerParty != null)
            {
                foreach (var u in _combat.playerParty.Where(x => x != null && !string.IsNullOrWhiteSpace(x.UnitId)))
                    _units[u.UnitId] = u;
            }
            if (_combat.enemyParty != null)
            {
                foreach (var u in _combat.enemyParty.Where(x => x != null && !string.IsNullOrWhiteSpace(x.UnitId)))
                    _units[u.UnitId] = u;
            }
        }

        void TryBindActor(UnitActor actor)
        {
            if (!actor || string.IsNullOrWhiteSpace(actor.unitId)) return;
            if (!_units.TryGetValue(actor.unitId, out var unit) || unit == null)
            {
                unit = FindUnit(actor.unitId);
                if (unit != null)
                    _units[actor.unitId] = unit;
            }

            if (unit != null)
                actor.Bind(unit); // 内部会顺带根据 TeamId 给环染色
        }
        private Unit FindUnit(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId) || _combat == null) return null;

            if (_combat.playerParty != null)
            {
                foreach (var u in _combat.playerParty)
                {
                    if (u != null && string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase))
                        return u;
                }
            }

            if (_combat.enemyParty != null)
            {
                foreach (var u in _combat.enemyParty)
                {
                    if (u != null && string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase))
                        return u;
                }
            }

            return null;
        }
        void HideAllRings()
        {
            foreach (var kv in _actors) kv.Value.ShowRing(false);
        }

        // -------------- 工具：兼容查找 -------------- //
        static T FindFirstObjectByTypeSafe<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }

        static T[] FindObjectsOfTypeSafe<T>(bool includeInactive) where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            return includeInactive
                ? Resources.FindObjectsOfTypeAll<T>()   // 包含未激活（编辑器下）
                : UnityEngine.Object.FindObjectsOfType<T>();
#endif
        }
    }
}
