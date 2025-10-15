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
    }
}
