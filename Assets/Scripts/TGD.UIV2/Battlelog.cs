using System;
using UnityEngine;

namespace TGD.UI
{
    /// <summary>��Сս����־���񣨾�̬����δ���ɻ����¼�����/��¼����</summary>
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

