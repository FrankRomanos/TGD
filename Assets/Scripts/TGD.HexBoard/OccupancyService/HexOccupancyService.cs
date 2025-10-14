using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexOccupancyService : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        HexOccupancy _occ;

        public HexOccupancy Get()
        {
            if (_occ == null && authoring != null && authoring.Layout != null)
                _occ = new HexOccupancy(authoring.Layout);
            return _occ;
        }

        public bool Register(IGridActor actor, Hex anchor, Facing4 facing)
            => Get() != null && Get().TryPlace(actor, anchor, facing);

        public void Unregister(IGridActor actor) { Get()?.Remove(actor); }

        public bool CanPlaceIgnoreTempAttack(IGridActor actor, Hex anchor, Facing4 facing, IGridActor ignore = null)
        {
            var occ = Get();
            return occ != null && occ.CanPlaceIgnoreTempAttack(actor, anchor, facing, ignore);
        }

        public bool IsReservedTempAttack(Hex cell, IGridActor ignore = null)
        {
            var occ = Get();
            return occ != null && occ.IsReservedTempAttack(cell, ignore);
        }
    }
}
