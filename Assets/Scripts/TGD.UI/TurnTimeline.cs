using UnityEngine;
using TGD.Combat;

namespace TGD.UI
{
    /// <summary>��ʾһ����λ�Ļغ�ʱ�䣨ռλ����</summary>
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
