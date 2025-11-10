using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    [System.Obsolete("Replaced by IOcc (shim only). Do not use in new code.", false)]
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class PlayerOccupancyBridge : MonoBehaviour, IActorOccupancyBridge
    {
        [SerializeField]
        UnitRuntimeContext _ctx;

        [SerializeField]
        UnitGridAdapter _actor;

        int _anchorVersion;

        public event System.Action<Hex, int> AnchorChanged;

        void Awake()
        {
            if (_ctx == null)
                _ctx = GetComponent<UnitRuntimeContext>();

            if (_actor == null)
                _actor = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);
        }

        void OnEnable()
        {
            Awake();
            EnsurePlacedNow();
        }

        void OnDisable()
        {
            var service = _ctx?.occService;
            if (service == null)
                return;

            service.Remove(_ctx, out _);
        }

        IOccupancyService Service => _ctx?.occService;

        public bool IsReady => Service != null && _actor != null;

        public object Actor => _actor;

        public Hex CurrentAnchor => _actor != null ? _actor.Anchor : Hex.Zero;

        public int AnchorVersion => _anchorVersion;

        public bool EnsurePlacedNow()
        {
            var service = Service;
            if (service == null || _actor == null)
                return false;

            var anchor = _actor.Anchor;
            var facing = _actor.Facing;

            if (service.TryPlace(_ctx, anchor, facing, out _, out var reason))
            {
                NotifyAnchorChanged(anchor);
                return true;
            }

            if (reason == OccFailReason.AlreadyPlaced || !service.IsFreeFor(_ctx, anchor, facing))
                return true;

            return false;
        }

        public bool MoveCommit(Hex newAnchor, Facing4 newFacing, OccToken token = default)
        {
            var service = Service;
            if (service == null || _actor == null)
                return false;

            bool success;
            if (token.IsValid)
            {
                success = service.Commit(_ctx, token, newAnchor, newFacing, out _, out var reason);
            }
            else
            {
                success = service.TryMove(_ctx, newAnchor, newFacing, out _, out var reason);
            }

            if (success)
            {
                NotifyAnchorChanged(newAnchor);
                return true;
            }

            return false;
        }

        void NotifyAnchorChanged(Hex anchor)
        {
            _anchorVersion++;
            AnchorChanged?.Invoke(anchor, _anchorVersion);
        }
    }
}
