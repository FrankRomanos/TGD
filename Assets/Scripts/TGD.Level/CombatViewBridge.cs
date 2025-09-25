using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.UI;   // DamageNumberManager

namespace TGD.Level
{
    /// <summary>
    /// Combat ��ͼ�ţ�ֻ����ǰ�����߽��»�����ʾ�˺����֡�
    /// ���ݣ���ʹ CombatLoop �����¼���Ҳ������ѯ���ס�
    /// </summary>
    public class CombatViewBridge : MonoBehaviour
    {
        public static CombatViewBridge Instance { get; private set; }

        [Header("References")]
        [SerializeField] CombatLoop combatLoop;  // �ɿգ��Զ���

        [Header("Options")]
        public bool autoBindSceneActors = true;
        [Tooltip("������ѯ������룩���¼�����ʱ������С")]
        public float pollInterval = 0.05f;

        // ����
        readonly Dictionary<string, UnitActor> _actors =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Unit> _units =
            new(StringComparer.OrdinalIgnoreCase);

        CombatLoop _combat;
        Unit _lastActive;
        float _pollTimer;

        void Awake()
        {
            Instance = this;

            _combat = combatLoop ? combatLoop : FindFirstObjectByTypeSafe<CombatLoop>();

            BuildUnitIndex();
            SubscribeCombat(true);

            if (autoBindSceneActors)
            {
                foreach (var a in FindObjectsOfTypeSafe<UnitActor>(includeInactive: true))
                    TryBindActor(a);
            }

            HideAllRings();   // ��ʼȫ���ر�
        }

        void OnDestroy()
        {
            SubscribeCombat(false);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // ������ѯ��ActiveUnit �ı�Ҳ�ᴥ����
            _pollTimer += Time.deltaTime;
            if (_pollTimer < pollInterval) return;
            _pollTimer = 0f;

            var cur = _combat ? _combat.GetActiveUnit() : null;
            if (!ReferenceEquals(cur, _lastActive))
            {
                _lastActive = cur;
                HideAllRings();
                if (cur != null && _actors.TryGetValue(cur.UnitId, out var a) && a)
                    a.ShowRing(true);
            }
        }

        // ���� ���⣺UnitActor ���� ���� //
        public void RegisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId) || !actor) return;
            _actors[unitId] = actor;
            actor.ShowRing(false);
            TryBindActor(actor);   // �� CombatLoop ���ݾ�˳����
        }

        public void UnregisterActor(string unitId, UnitActor actor)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return;
            if (_actors.TryGetValue(unitId, out var cur) && cur == actor)
                _actors.Remove(unitId);
        }

        public void RefreshBindings()
        {
            BuildUnitIndex();
            foreach (var a in _actors.Values) TryBindActor(a);
            HideAllRings();
            _lastActive = null; // ǿ���´���ѯ����ˢ��
        }

        // ���� Combat �¼����о��ã� ���� //
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
            HideAllRings();
            if (u != null && _actors.TryGetValue(u.UnitId, out var a) && a)
                a.ShowRing(true);
            _lastActive = u; // ����ѯ���¼�һ��
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

        // ���� �� ���� //
        void BuildUnitIndex()
        {
            _units.Clear();
            if (_combat == null) return;

            if (_combat.playerParty != null)
                foreach (var u in _combat.playerParty.Where(Ok))
                    _units[u.UnitId] = u;

            if (_combat.enemyParty != null)
                foreach (var u in _combat.enemyParty.Where(Ok))
                    _units[u.UnitId] = u;

            static bool Ok(Unit u) => u != null && !string.IsNullOrWhiteSpace(u.UnitId);
        }

        void TryBindActor(UnitActor actor)
        {
            if (!actor || string.IsNullOrWhiteSpace(actor.unitId)) return;
            if (_units.TryGetValue(actor.unitId, out var unit) && unit != null)
            {
                actor.Bind(unit);
                actor.TryTintRingByTeam(unit.TeamId);
            }
        }

        void HideAllRings()
        {
            foreach (var kv in _actors) kv.Value.ShowRing(false);
        }

        // ���� ���ҹ��ߣ����� 2023+������ //
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
                ? Resources.FindObjectsOfTypeAll<T>()
                : UnityEngine.Object.FindObjectsOfType<T>();
#endif
        }
    }
}
