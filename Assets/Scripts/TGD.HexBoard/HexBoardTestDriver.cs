// File: TGD.HexBoard/HexBoardTestDriver.cs
using UnityEngine;
using TGD.UI;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class HexBoardTestDriver : MonoBehaviour, IUnitView
    {
        public HexBoardAuthoringLite authoring;
        public Transform unitView;
        public string unitId = "P1";                 // ★ 新增：每个驱动唯一 Id
        [Tooltip("Optional temporary portrait for UI previews (timeline, etc.)")]
        public Sprite temporaryAvatar;
        public int startQ = 9, startR = 7;
        public Facing4 startFacing = Facing4.PlusQ;
        public float y = 0.01f;

        public Unit UnitRef => _unit;
        public HexBoardLayout Layout => _layout;
        public HexBoardMap<Unit> Map => _map;

        public bool syncFacingFromView = true;

        HexBoardLayout _layout;
        HexBoardMap<Unit> _map;
        Unit _unit;
        bool _inited;
        bool _registered;

        public bool IsReady => _inited && _layout != null && _unit != null && _map != null;

        public string UnitId => unitId;

        public Transform ViewTransform => unitView != null ? unitView : transform;

        public void EnsureInit()
        {
            if (_inited) return;
            if (authoring == null || authoring.Layout == null) return;

            _layout = authoring.Layout;
            _map = new HexBoardMap<Unit>(_layout);
            if (string.IsNullOrEmpty(unitId)) unitId = gameObject.name;   // ★ 兜底
            _unit = new Unit(unitId, new Hex(startQ, startR), startFacing); // ★ 用唯一 Id
            _map.Set(_unit, _unit.Position);

            _inited = true;
            SyncView();
            TryRegisterView();
            RegisterAvatar();
        }

        void OnEnable()
        {
            EnsureInit();
            TryRegisterView();
            RegisterAvatar();
        }


        void OnValidate()
        {
            if (!Application.isPlaying)
                RegisterAvatar();
        }

        void Start() => EnsureInit();

        void Update()
        {
            EnsureInit();
            if (!IsReady) return;

            if (syncFacingFromView && unitView != null)
                _unit.Facing = FacingFromYaw4(unitView.eulerAngles.y);
        }

        void OnDisable()
        {
            UnregisterAvatar();
            if (_registered)
            {
                UnitLocator.Unregister(this);
                _registered = false;
            }
        }

        public void SyncView()
        {
            if (!IsReady || unitView == null) return;
            var space = HexSpace.Instance;
            if (space == null)
            {
                Debug.LogWarning("[HexBoardTestDriver] HexSpace instance is missing; cannot sync view.", this);
                return;
            }
            unitView.position = space.HexToWorld(_unit.Position, y);
        }

        public static Facing4 FacingFromYaw4(float yawDegrees)
        {
            int a = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) / 90f) * 90;
            switch (a % 360)
            {
                case 0: return Facing4.MinusR;
                case 90: return Facing4.PlusQ;
                case 180: return Facing4.PlusR;
                case 270: return Facing4.MinusQ;
                default: return Facing4.MinusR;
            }
        }

        void TryRegisterView()
        {
            if (_registered || !IsReady) return;
            if (UnitLocator.Register(this))
                _registered = true;
        }
        void RegisterAvatar()
        {
            if (_unit == null || string.IsNullOrEmpty(unitId))
                return;
            TurnTimelineAvatarRegistry.SetAvatar(unitId, temporaryAvatar);
        }

        void UnregisterAvatar()
        {
            if (string.IsNullOrEmpty(unitId))
                return;
            TurnTimelineAvatarRegistry.RemoveAvatar(unitId, temporaryAvatar);
        }
    }
}
