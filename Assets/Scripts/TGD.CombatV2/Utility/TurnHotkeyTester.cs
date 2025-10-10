// File: TGD.CombatV2/Utility/TurnHotkeyTester.cs
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    /// �����ã����� TurnStarted�����ȼ�������ǰ��λ�غϡ�
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnHotkeyTester : MonoBehaviour
    {
        public TurnManagerV2 tm;
        public KeyCode endTurnKey = KeyCode.Return;

        Unit _current;

        void OnEnable()
        {
            if (tm != null) tm.TurnStarted += OnTurnStarted;
        }

        void OnDisable()
        {
            if (tm != null) tm.TurnStarted -= OnTurnStarted;
        }

        void OnTurnStarted(Unit u) => _current = u;

        void Update()
        {
            if (tm != null && _current != null && Input.GetKeyDown(endTurnKey))
                tm.EndTurn(_current);
        }
    }
}
