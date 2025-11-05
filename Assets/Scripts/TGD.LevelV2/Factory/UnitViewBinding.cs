using UnityEngine;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.LevelV2
{
    /// <summary>
    /// Bridges spawned units to the UnitLocator so camera/controllers can resolve their transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitViewBinding : MonoBehaviour, IUnitView
    {
        [SerializeField] Transform viewTransform;
        [SerializeField] string unitId;

        Unit _unit;
        bool _registered;

        public string UnitId => _unit != null ? _unit.Id : unitId;

        public Transform ViewTransform => viewTransform != null ? viewTransform : transform;

        internal bool HasExplicitViewTransform => viewTransform != null;

        public void Bind(Unit unit)
        {
            _unit = unit;
            unitId = unit != null ? unit.Id : null;
            if (viewTransform == null)
                viewTransform = transform;
            RefreshRegistration();
        }

        public void SetViewTransform(Transform view)
        {
            viewTransform = view != null ? view : transform;
            RefreshRegistration();
        }

        void Awake()
        {
            if (viewTransform == null)
                viewTransform = transform;
        }

        void OnEnable()
        {
            RefreshRegistration();
        }

        void OnDisable()
        {
            if (_registered)
            {
                UnitLocator.Unregister(this);
                _registered = false;
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
    }
}
