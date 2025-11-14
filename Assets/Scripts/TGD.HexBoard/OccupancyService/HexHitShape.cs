// File: TGD.HexBoard/HexHitShape.cs
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    public static class HexHitShape
    {
        static Hex Forward(Facing4 facing) => Hex.Directions[HexAreaUtil.FacingToDirIndex(facing)];

        static Hex Left(Facing4 facing) => facing switch
        {
            Facing4.PlusQ => Hex.Directions[2],
            Facing4.MinusQ => Hex.Directions[5],
            Facing4.PlusR => Hex.Directions[0],
            _ => Hex.Directions[3],
        };

        public static IEnumerable<Hex> Expand(Hex anchor, Facing4 facing, HitShape shape)
        {
            if (shape == null)
            {
                yield return anchor;
                yield break;
            }

            var offsets = shape.offsets;
            if (offsets == null || offsets.Count == 0)
            {
                yield return anchor;
                yield break;
            }

            var forward = Forward(facing);
            var left = Left(facing);
            foreach (var offset in offsets)
                yield return anchor + forward * offset.fwd + left * offset.left;
        }
    }
}
