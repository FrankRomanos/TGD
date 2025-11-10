// File: TGD.HexBoard/Occ/OccActorResolver.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    public sealed class OccActorResolver : MonoBehaviour
    {
        private readonly Dictionary<UnitRuntimeContext, UnitGridAdapter> _cache = new Dictionary<UnitRuntimeContext, UnitGridAdapter>();

        public UnitGridAdapter GetOrBind(UnitRuntimeContext ctx)
        {
            UnitGridAdapter a;
            if (_cache.TryGetValue(ctx, out a)) return a;

            a = ctx.GetComponent<UnitGridAdapter>() ?? ctx.gameObject.AddComponent<UnitGridAdapter>();
            if (a.Unit == null && ctx.boundUnit != null) a.Unit = ctx.boundUnit;
            _cache[ctx] = a;
            return a;
        }

        public bool TryFind(UnitRuntimeContext ctx, out UnitGridAdapter a) => _cache.TryGetValue(ctx, out a);
    }
}
