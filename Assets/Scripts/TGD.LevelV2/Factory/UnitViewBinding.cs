using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.LevelV2
{
    /// <summary>
    /// Bridges spawned units to the UnitLocator so camera/controllers can resolve their transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitViewBinding : MonoBehaviour, IUnitView, IUnitAvatarSource
    {
        [SerializeField] Transform viewTransform;
        [SerializeField] string unitId;
        [SerializeField] Sprite avatar;

        Unit _unit;
        bool _registered;
        bool _avatarRegistered;

        public string UnitId => _unit != null ? _unit.Id : unitId;

        public Transform ViewTransform => viewTransform != null ? viewTransform : transform;

        public Sprite Avatar => avatar;

        internal bool HasExplicitViewTransform => viewTransform != null;

        public void Bind(Unit unit)
        {
            _unit = unit;
            unitId = unit != null ? unit.Id : null;
            if (viewTransform == null)
                viewTransform = transform;
            RefreshRegistration();
            RefreshAvatarRegistration();
        }

        public void SetViewTransform(Transform view)
        {
            viewTransform = view != null ? view : transform;
            RefreshRegistration();
        }

        public void SetAvatar(Sprite sprite)
        {
            avatar = sprite;
            RefreshAvatarRegistration();
        }

        void Awake()
        {
            if (viewTransform == null)
                viewTransform = transform;
        }

        void OnEnable()
        {
            RefreshRegistration();
            RefreshAvatarRegistration();
        }

        void OnDisable()
        {
            if (_registered)
            {
                UnitLocator.Unregister(this);
                _registered = false;
            }

            if (_avatarRegistered)
            {
                UnitAvatarRegistry.Unregister(this);
                _avatarRegistered = false;
            }
        }

        void RefreshRegistration()
        {
            if (!isActiveAndEnabled)
                return;

            if (_registered)
            {
                UnitLocator.Unregister(this);
                _registered = false;
            }

            var id = UnitId;
            if (string.IsNullOrEmpty(id))
                return;

            if (UnitLocator.Register(this))
                _registered = true;
        }

        void RefreshAvatarRegistration()
        {
            if (_avatarRegistered)
            {
                UnitAvatarRegistry.Unregister(this);
                _avatarRegistered = false;
            }

            if (!isActiveAndEnabled)
                return;

            if (avatar == null)
                return;

            var id = UnitId;
            if (string.IsNullOrEmpty(id))
                return;

            if (UnitAvatarRegistry.Register(this))
                _avatarRegistered = true;
        }

        Sprite IUnitAvatarSource.GetAvatarSprite() => avatar;
    }
}
