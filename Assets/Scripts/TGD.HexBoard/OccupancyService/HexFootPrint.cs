// File: TGD.HexBoard/HexFootprint.cs
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    public static class HexFootprint
    {
        static Hex Forward(Facing4 f) => Hex.Directions[HexAreaUtil.FacingToDirIndex(f)];
        static Hex Left(Facing4 f) => f switch
        {
            Facing4.PlusQ => Hex.Directions[2], // -R£¨ÊÓ¾õÉÏ¡°ÉÏ¡±£©
            Facing4.MinusQ => Hex.Directions[5], // +R
            Facing4.PlusR => Hex.Directions[0], // +Q
            _ => Hex.Directions[3], // -Q
        };

        public static IEnumerable<Hex> Expand(Hex anchor, Facing4 facing, FootprintShape shape)
        {
            if (shape == null) { yield return anchor; yield break; }
            var F = Forward(facing); var L = Left(facing);
            foreach (var o in shape.offsets)
                yield return anchor + F * o.fwd + L * o.left;
        }
    }
}

