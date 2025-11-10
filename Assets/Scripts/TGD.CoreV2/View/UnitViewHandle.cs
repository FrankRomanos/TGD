using System;
using UnityEngine;

namespace TGD.CoreV2
{
    [DisallowMultipleComponent]
    public sealed class UnitViewHandle : MonoBehaviour, IUnitView
    {
        [Tooltip("可不填；为空时优先用 ctx.boundUnit.Id，其次用物体名")]
        public string unitIdOverride;

        [Tooltip("模型根；为空则使用自身 transform")]
        public Transform viewRoot;

        public UnitRuntimeContext ctx;

        public static event Action<IUnitView> ViewEnabled;
        public static event Action<IUnitView> ViewDisabled;

        public string UnitId
        {
            get
            {
                if (!string.IsNullOrEmpty(unitIdOverride)) return unitIdOverride;
                if (ctx != null && ctx.boundUnit != null && !string.IsNullOrEmpty(ctx.boundUnit.Id))
                    return ctx.boundUnit.Id;
                return gameObject.name;
            }
        }

        public Transform ViewTransform => viewRoot != null ? viewRoot : transform;

        void Awake()
        {
            if (ctx == null)
                ctx = GetComponent<UnitRuntimeContext>();
            if (viewRoot == null)
                viewRoot = transform;
        }

        void Reset()
        {
            if (ctx == null) ctx = GetComponent<UnitRuntimeContext>();
            if (viewRoot == null) viewRoot = transform;
        }

        void OnEnable()
        {
            if (ctx == null)
                ctx = GetComponent<UnitRuntimeContext>();
            ViewEnabled?.Invoke(this);
        }

        void OnDisable()
        {
            ViewDisabled?.Invoke(this);
        }

        void OnDestroy()
        {
            ViewDisabled?.Invoke(this);
        }
    }
}
