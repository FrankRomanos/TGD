// File: TGD.HexBoard/HexBoardTestDriver.cs
using UnityEngine;

namespace TGD.HexBoard
{
    /// 最小运行时上下文：聚合 Unit/Layout/Map/View，供移动/攻击等系统引用
    [DisallowMultipleComponent]
    public sealed class HexBoardTestDriver : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        public Transform unitView;
        public int startQ = 9, startR = 7;
        public Facing4 startFacing = Facing4.PlusQ;
        public float y = 0.01f;

        // 给外部系统用的入口
        public Unit UnitRef => _unit;
        public HexBoardLayout Layout => _layout;
        public HexBoardMap<Unit> Map => _map;

        // 可选：把视图朝向同步回数据（默认开启，便于动画驱动朝向）
        public bool syncFacingFromView = true;

        HexBoardLayout _layout;
        HexBoardMap<Unit> _map;
        Unit _unit;
        bool _inited;

        public bool IsReady => _inited && _layout != null && _unit != null && _map != null;

        public void EnsureInit()
        {
            if (_inited) return;
            if (authoring == null || authoring.Layout == null) return;

            _layout = authoring.Layout;
            _map = new HexBoardMap<Unit>(_layout);
            _unit = new Unit("U1", new Hex(startQ, startR), startFacing);
            _map.Set(_unit, _unit.Position);

            _inited = true;
            SyncView();
        }

        void Start() => EnsureInit();

        void Update()
        {
            EnsureInit();
            if (!IsReady) return;

            if (syncFacingFromView && unitView != null)
                _unit.Facing = FacingFromYaw4(unitView.eulerAngles.y);
        }

        public void SyncView()
        {
            if (!IsReady || unitView == null) return;
            unitView.position = _layout.World(_unit.Position, y);
            // 朝向交由动画/其他系统控制，这里不强制写回
        }

        public static Facing4 FacingFromYaw4(float yawDegrees)
        {
            int a = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) / 90f) * 90;
            switch (a % 360)
            {
                case 0: return Facing4.MinusR;  // +Z
                case 90: return Facing4.PlusQ;   // +X
                case 180: return Facing4.PlusR;   // -Z
                case 270: return Facing4.MinusQ;  // -X
                default: return Facing4.MinusR;
            }
        }
    }
}
