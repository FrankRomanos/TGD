using UnityEngine;
using System.Collections.Generic;

namespace TGD.HexBoard
{

    public sealed class HexBoardTestDriver : MonoBehaviour
    {
        public HexBoardMap<Unit> Map => map;

        public HexBoardAuthoringLite authoring;
        public Transform unitView;
        public int startQ = 9, startR = 7;
        public Facing4 startFacing = Facing4.PlusQ;
        public float y = 0.01f;

        public Unit UnitRef => unit;
        public MovementSystem Movement => mover;
        public HexBoardLayout Layout => layout;

        HexBoardLayout layout;
        HexBoardMap<Unit> map;
        MovementSystem mover;
        Unit unit;

        bool _inited = false;
        public bool IsReady => _inited && layout != null && unit != null && mover != null;
        public bool syncFacingFromView = true; // Inspector 可勾选
        public void EnsureInit()
        {
            if (_inited) return;
            if (authoring == null || authoring.Layout == null) return;

            layout = authoring.Layout;
            map = new HexBoardMap<Unit>(layout);
            unit = new Unit("U1", new Hex(startQ, startR), startFacing);
            map.Set(unit, unit.Position);
            mover = new MovementSystem(layout, map);
            _inited = true;
            SyncView();
        }

        void Start() { EnsureInit(); }
        void Update()
        {
            EnsureInit();
            if (!IsReady) return;
            if (syncFacingFromView && unitView != null)
            {
                unit.Facing = FacingFromYaw4(unitView.eulerAngles.y);
            }

            if (Input.GetKeyDown(KeyCode.UpArrow)) { mover.Execute(new MoveOp(unit, MoveCardinal.Forward, 1)); SyncView(); }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { mover.Execute(new MoveOp(unit, MoveCardinal.Backward, 1)); SyncView(); }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { mover.Execute(new MoveOp(unit, MoveCardinal.Left, 1)); SyncView(); }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { mover.Execute(new MoveOp(unit, MoveCardinal.Right, 1)); SyncView(); }
            if (Input.GetKeyDown(KeyCode.A)) { unit.Facing = RotateLeft(unit.Facing); SyncView(); }
            if (Input.GetKeyDown(KeyCode.D)) { unit.Facing = RotateRight(unit.Facing); SyncView(); }
            if (Input.GetKeyDown(KeyCode.Space)) { SyncView(); }
        }

        Facing4 RotateLeft(Facing4 f) => f switch { Facing4.PlusQ => Facing4.PlusR, Facing4.PlusR => Facing4.MinusQ, Facing4.MinusQ => Facing4.MinusR, _ => Facing4.PlusQ };
        Facing4 RotateRight(Facing4 f) => f switch { Facing4.PlusQ => Facing4.MinusR, Facing4.MinusR => Facing4.MinusQ, Facing4.MinusQ => Facing4.PlusR, _ => Facing4.PlusQ };

        public void SyncView()
        {
            if (!IsReady) return;
            if (unitView != null)
                unitView.position = layout.World(unit.Position, y);
        }

        void OnGUI()
        {
            EnsureInit();
            if (!IsReady) return;

            var w = layout.World(unit.Position, y);
            GUI.Label(new Rect(10, 10, 780, 22),
              $"Unit {unit.Id}  Hex=({unit.Position.q},{unit.Position.r})  World=({w.x:F2},{w.z:F2})  Facing={unit.Facing}  [↑↓←→ move, A/D face, Space snap]");
        }
        public static Facing4 FacingFromYaw4(float yawDegrees)
        {
            // 量化到 0/90/180/270
            int a = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) / 90f) * 90;
            switch (a % 360)
            {
                case 0: return Facing4.MinusR; // 朝上
                case 90: return Facing4.PlusQ;  // 朝右
                case 180: return Facing4.PlusR;  // 朝下
                case 270: return Facing4.MinusQ; // 朝左
                default: return Facing4.MinusR; // 理论不会到
            }
        }
    }

}
