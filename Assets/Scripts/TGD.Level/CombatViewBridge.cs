using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.UI;       // DamageNumberManager �õ�

namespace TGD.Level
{
    /// <summary>
    /// ��ͼ�ţ��� CombatLoop �Ļغ�/Ʈ���¼���ӳ�䵽������� UnitActor��
    /// - ά�� UnitId��UnitActor ӳ��
    /// - �غϿ�ʼ/������ͳһ��/�ؽ��»���ֻ����ǰ�����ߣ�
    /// - ����Ʈ�֣����� DamageNumberManager
    /// - ֧������ʱ���°�/ˢ��
    /// </summary>
    public class CombatViewBridge : MonoBehaviour
    {
        public static CombatViewBridge Instance { get; private set; }

        [Header("References")]
        [Tooltip("��ѡ�������ջ��Զ����ҳ����е� CombatLoop")]
        [SerializeField] private CombatLoop combatLoop;

        [Header("Options")]
        [Tooltip("Awake ʱ�Զ����Ҳ��󶨳����е� UnitActor")]
        public bool autoBindSceneActors = true;

        // ���� ���� ���� //
        readonly Dictionary<string, UnitActor> _actors =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Unit> _units =
            new(StringComparer.OrdinalIgnoreCase);

        CombatLoop _combat;

        // -------------- �������� -------------- //
        void Awake()
        {
            Instance = this;

            // 1) ��λ CombatLoop������ Inspector ���ã�
            _combat = combatLoop;
            if (_combat == null)
                _combat = FindFirstObjectByTypeSafe<CombatLoop>();

            // 2) ���� CombatLoop ���� Unit �����������¼�
            BuildUnitIndex();
            SubscribeCombat(true);

            // 3) �󶨳������ UnitActor
            if (autoBindSceneActors)
            {
                foreach (var a in FindObjectsOfTypeSafe<UnitActor>(includeInactive: true))
                    TryBindActor(a);
            }

            // 4) ��ʼȷ����ȫ��
            HideAllRings();
        }

        void OnDestroy()
        {
            SubscribeCombat(false);
            if (Instance == this) Instance = null;
        }

        // -------------- ���⣺�� UnitActor ���� -------------- //
        public void RegisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId) || !actor) return;
            _actors[unitId] = actor;
            RegisterActor(actor.unitId, actor);
            actor.ShowRing(false); // ע��ʱĬ�Ϲ�
        }

        public void UnregisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return;
            if (_actors.TryGetValue(unitId, out var cur) && cur == actor)
                _actors.Remove(unitId);
        }

        /// <summary>������� CombatLoop �����򳡾�����ɾ�˵�λʱ���ֶ�����ˢ�¡�</summary>
        public void RefreshBindings()
        {
            BuildUnitIndex();
            foreach (var a in _actors.Values) TryBindActor(a);
            HideAllRings();
        }

        // -------------- Combat �¼� -------------- //
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
            // ��ȫ�أ���ֻ����ǰ
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

        // -------------- ��/���� -------------- //
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
                actor.Bind(unit); // �ڲ���˳������ TeamId ����Ⱦɫ
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

        // -------------- ���ߣ����ݲ��� -------------- //
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
                ? Resources.FindObjectsOfTypeAll<T>()   // ����δ����༭���£�
                : UnityEngine.Object.FindObjectsOfType<T>();
#endif
        }
    }
}
