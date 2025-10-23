using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    /// Simple Return-key listener that proxies to the turn/action managers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnHotkeyTester : MonoBehaviour
    {
        public TurnManagerV2 tm;
        public CombatActionManagerV2 cam;
        public KeyCode endTurnKey = KeyCode.Return;

        Unit _current;

        void Awake()
        {
            if (tm == null)
                tm = FindManager();
            if (cam == null)
                cam = FindCam();
        }

        void OnEnable()
        {
            if (tm == null)
                tm = FindManager();
            if (tm != null)
                tm.TurnStarted += OnTurnStarted;
        }

        void OnDisable()
        {
            if (tm != null)
                tm.TurnStarted -= OnTurnStarted;
        }

        void OnTurnStarted(Unit unit)
        {
            if (tm != null && tm.IsPlayerPhase && tm.IsPlayerUnit(unit))
                _current = unit;
            else
                _current = null;
        }

        void Update()
        {
            if (tm == null)
                tm = FindManager();
            if (cam == null)
                cam = FindCam();

            if (tm == null || cam == null)
                return;

            Unit target = null;
            if (_current != null && tm.IsPlayerPhase && tm.ActiveUnit == _current)
            {
                target = _current;
            }
            else if (cam != null && cam.CurrentBonusTurnUnit != null)
            {
                target = cam.CurrentBonusTurnUnit;
            }

            if (target == null)
                return;

            if (!Input.GetKeyDown(endTurnKey))
                return;

            cam.RequestEndTurn(target);
        }

        static TurnManagerV2 FindManager()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<TurnManagerV2>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<TurnManagerV2>();
#endif
        }

        static CombatActionManagerV2 FindCam()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<CombatActionManagerV2>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<CombatActionManagerV2>();
#endif
        }
    }
}
