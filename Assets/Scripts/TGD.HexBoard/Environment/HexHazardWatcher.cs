// File: TGD.HexBoard/HexHazardWatcher.cs
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 监听单位格位变化：踩陷阱打印；落穴阻挡在预览阶段由 ClickMover 的 Block + env.IsPit 负责
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
                // 进入新格：陷阱触发（可重复，每次进入都打印）
                if (env.IsTrap(cur))
                {
                    Debug.Log($"[Trap] {driver.UnitRef} stepped on TRAP at {cur} → take damage (test log)");
                }
                _last = cur;
            }
        }
    }
}
