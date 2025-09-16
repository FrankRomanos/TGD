using UnityEngine;
using TGD.Combat;

namespace TGD.UI
{
    /// <summary>显示一个单位的回合时间（占位）。</summary>
    public class TurnTimeline : MonoBehaviour
    {
        public Unit unit;

        private void OnGUI()
        {
            if (unit == null) return;
            GUILayout.Label($"TurnTime={unit.TurnTime}s  Remaining={unit.RemainingTime}s  Prepaid={unit.PrepaidTime}s");
        }
    }
}
