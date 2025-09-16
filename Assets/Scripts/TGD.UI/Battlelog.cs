using System;
using UnityEngine;

namespace TGD.UI
{
    /// <summary>最小战斗日志服务（静态）。未来可换成事件总线/记录器。</summary>
    public static class BattleLog
    {
        public static event Action<string> OnLog;

        public static void Log(string msg)
        {
            Debug.Log(msg);
            OnLog?.Invoke(msg);
        }
    }
}

