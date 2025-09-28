// File: TGD.HexBoard/HexHazardWatcher.cs
using UnityEngine;

namespace TGD.HexBoard
{
    /// ������λ��λ�仯���������ӡ����Ѩ�赲��Ԥ���׶��� ClickMover �� Block + env.IsPit ����
    [DisallowMultipleComponent]
    public sealed class HexHazardWatcher : MonoBehaviour
    {
        public HexBoardTestDriver driver;
        public HexEnvironmentSystem env;

        Hex _last;
        bool _has;

        void OnEnable()
        {
            _has = false;
        }

        void LateUpdate()
        {
            if (driver == null || !driver.IsReady || env == null) return;

            var cur = driver.UnitRef.Position;
            if (!_has) { _last = cur; _has = true; return; }

            if (!cur.Equals(_last))
            {
                // �����¸����崥�������ظ���ÿ�ν��붼��ӡ��
                if (env.IsTrap(cur))
                {
                    Debug.Log($"[Trap] {driver.UnitRef} stepped on TRAP at {cur} �� take damage (test log)");
                }
                _last = cur;
            }
        }
    }
}
