using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.UI;

namespace TGD.Level
{
    /// <summary>
    /// 视图桥：把 CombatLoop 的回合/飘字事件，映射到场景里的 UnitActor。
    /// - 维护 UnitId→UnitActor 映射
    /// - 回合开始/结束：控制脚下环显隐
    /// - 请求飘字：调用 DamageNumberManager
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

        // 映射
        private readonly Dictionary<string, UnitActor> _actors =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Unit> _units =
            new(StringComparer.OrdinalIgnoreCase);

        private CombatLoop _combat;

        private void Awake()
        {
            Instance = this;

            // 1) 定位 CombatLoop（优先用 Inspector 拖的引用）
            _combat = combatLoop;
            if (_combat == null)
            {
#if UNITY_2023_1_OR_NEWER
                _combat = CombatLoop.Instance ??
                          UnityEngine.Object.FindFirstObjectByType<CombatLoop>(FindObjectsInactive.Include);
#else
                _combat = CombatLoop.Instance ??
                          UnityEngine.Object.FindObjectOfType<CombatLoop>();
#endif
            }

            // 2) 构建 Unit 索引 & 订阅 Combat 事件
            BuildUnitIndex();

            if (_combat != null)
            {
                _combat.OnTurnBegan += HandleTurnBegan;
                _combat.OnTurnEnded += HandleTurnEnded;
                _combat.OnDamageNumberRequested += HandleDamageNumber;
            }

            // 3) 绑定场景里的 UnitActor
            if (autoBindSceneActors)
            {
#if UNITY_2023_1_OR_NEWER
                var actors = UnityEngine.Object.FindObjectsByType<UnitActor>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );
#else
    // 旧版 Unity 仍使用这个重载
    var actors = UnityEngine.Object.FindObjectsOfType<UnitActor>(true);
#endif

                foreach (var actor in actors)
                    TryBindActor(actor);
            }
        }
            

        private void OnDestroy()
        {
            if (_combat != null)
            {
                _combat.OnTurnBegan -= HandleTurnBegan;
                _combat.OnTurnEnded -= HandleTurnEnded;
                _combat.OnDamageNumberRequested -= HandleDamageNumber;
            }

            if (Instance == this) Instance = null;
        }

        // —— Public：供 UnitActor 在 OnEnable/OnDisable 调用 —— //
        public void RegisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId) || actor == null) return;
            _actors[unitId] = actor;
            TryBindActor(actor);
        }

        public void UnregisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId) || actor == null) return;
            if (_actors.TryGetValue(unitId, out var a) && a == actor)
                _actors.Remove(unitId);
        }

        // —— 内部：把场景 Actor 与数据 Unit 绑定 —— //
        private void BuildUnitIndex()
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

        private void TryBindActor(UnitActor actor)
        {
            if (actor == null || string.IsNullOrWhiteSpace(actor.unitId)) return;
            if (_units.TryGetValue(actor.unitId, out var unit))
                actor.Bind(unit);
        }

        // —— Combat 事件处理 —— //
        private void HandleTurnBegan(Unit u)
        {
            if (u == null) return;
            if (_actors.TryGetValue(u.UnitId, out var a) && a != null)
                a.selectVisual?.SetVisible(true);
        }

        private void HandleTurnEnded(Unit u)
        {
            if (u == null) return;
            if (_actors.TryGetValue(u.UnitId, out var a) && a != null)
                a.selectVisual?.SetVisible(false);
        }

        private void HandleDamageNumber(Unit target, int amount, CombatLoop.DamageHint hint)
        {
            if (target == null) return;
            if (!_actors.TryGetValue(target.UnitId, out var a) || a == null) return;

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
    }
}
