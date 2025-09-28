using System;
using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// �ƶ����ܾ���ԭ�򣨿ɰ���������
    public enum MoveBlockReason
    {
        None,
        NotReady,           // ���δ����
        Busy,               // �����ƶ���
        NoConfig,           // δ���� MoveActionConfig
        Entangled,          // ������/����
        OnCooldown,         // ��ȴ��
        NotEnoughResource,  // ��Դ����
        NoSteps,            // ���ο��߲���Ϊ 0
        PathBlocked,        // ѡ�и񲻿ɴ�/����
    }

    /// ����ƶ���ص��¼������ڱ𴦶�����Щ�¼��������Լ��� UI ����
    public static class HexMoveEvents
    {
        // ��ʾ / ���� �ɴﷶΧ
        public static event Action<Unit, IEnumerable<Hex>> RangeShown;
        public static event Action RangeHidden;

        // �ƶ�����
        public static event Action<Unit, List<Hex>> MoveStarted;
        public static event Action<Unit, Hex, Hex, int, int> MoveStep; // (unit, from, to, stepIndex(1~n), total)
        public static event Action<Unit, Hex> MoveFinished;

        // ���ܾ�
        public static event Action<Unit, MoveBlockReason, string> MoveRejected;

        // ���� �� HexClickMover ���õķ�װ ���� 
        internal static void RaiseRangeShown(Unit u, IEnumerable<Hex> cells) => RangeShown?.Invoke(u, cells);
        internal static void RaiseRangeHidden() => RangeHidden?.Invoke();
        internal static void RaiseMoveStarted(Unit u, List<Hex> path) => MoveStarted?.Invoke(u, path);
        internal static void RaiseMoveStep(Unit u, Hex from, Hex to, int i, int n) => MoveStep?.Invoke(u, from, to, i, n);
        internal static void RaiseMoveFinished(Unit u, Hex end) => MoveFinished?.Invoke(u, end);
        internal static void RaiseRejected(Unit u, MoveBlockReason r, string msg) => MoveRejected?.Invoke(u, r, msg);
    }
}
