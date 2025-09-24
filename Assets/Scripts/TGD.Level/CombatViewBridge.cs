using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.UI;

namespace TGD.Level
{
    /// <summary>
    /// ��ͼ�ţ��� CombatLoop �Ļغ�/Ʈ���¼���ӳ�䵽������� UnitActor��
    /// - ά�� UnitId��UnitActor ӳ��
    /// - �غϿ�ʼ/���������ƽ��»�����
    /// - ����Ʈ�֣����� DamageNumberManager
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

        // ӳ��
        private readonly Dictionary<string, UnitActor> _actors =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Unit> _units =
            new(StringComparer.OrdinalIgnoreCase);

        private CombatLoop _combat;

        private void Awake()
        {
            Instance = this;

            // 1) ��λ CombatLoop�������� Inspector �ϵ����ã�
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

            // 2) ���� Unit ���� & ���� Combat �¼�
            BuildUnitIndex();

            if (_combat != null)
            {
                _combat.OnTurnBegan += HandleTurnBegan;
                _combat.OnTurnEnded += HandleTurnEnded;
                _combat.OnDamageNumberRequested += HandleDamageNumber;
            }

            // 3) �󶨳������ UnitActor
            if (autoBindSceneActors)
            {
#if UNITY_2023_1_OR_NEWER
                var actors = UnityEngine.Object.FindObjectsByType<UnitActor>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );
#else
    // �ɰ� Unity ��ʹ���������
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

        // ���� Public���� UnitActor �� OnEnable/OnDisable ���� ���� //
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

        // ���� �ڲ����ѳ��� Actor ������ Unit �� ���� //
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

        // ���� Combat �¼����� ���� //
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
