// File: TGD.HexBoard/HexAreaUtil.cs
using System.Collections.Generic;
using UnityEngine; // 为默认阻挡构造器使用到 Physics/LayerMask

namespace TGD.HexBoard
{
    /// 六边形范围计算 + 统一过滤 + 默认阻挡构造器
    /// 方向索引与 Hex.Directions 一致：0=+Q, 1=+NE, 2=-R, 3=-Q, 4=+SW, 5=+R
    public static class HexAreaUtil
    {
        static readonly Hex[] DIR = Hex.Directions;

        // ========== 基础工具 ==========
        public static int FacingToDirIndex(Facing4 f) => f switch
        {
            Facing4.PlusQ => 0,
            Facing4.MinusQ => 3,
            Facing4.PlusR => 5,
            _ => 2, // Facing4.MinusR
        };

        // ========== 几何（不含过滤） ==========

        /// 右 60° 扇形（含边界，不含原点）
        public static IEnumerable<Hex> Sector60Right(Hex origin, Facing4 facing, int radius,
                                                     HexBoardLayout layout = null, bool clampToBounds = true)
        {
            if (radius <= 0) yield break;
            int i = FacingToDirIndex(facing);
            Hex u = DIR[i], v = DIR[(i + 1) % 6];
            // 第 s 圈：a+b=s，a,b>=0，s=1..R
            for (int s = 1; s <= radius; s++)
                for (int a = 0; a <= s; a++)
                {
                    int b = s - a;
                    Hex h = origin + u * a + v * b;
                    if (layout != null && !layout.Contains(h)) { if (clampToBounds) continue; }
                    yield return h;
                }
        }

        /// 左 60° 扇形（含边界，不含原点）
        public static IEnumerable<Hex> Sector60Left(Hex origin, Facing4 facing, int radius,
                                                    HexBoardLayout layout = null, bool clampToBounds = true)
        {
            if (radius <= 0) yield break;
            int i = FacingToDirIndex(facing);
            Hex u = DIR[i], v = DIR[(i + 5) % 6]; // 左邻
            for (int s = 1; s <= radius; s++)
                for (int a = 0; a <= s; a++)
                {
                    int b = s - a;
                    Hex h = origin + u * a + v * b;
                    if (layout != null && !layout.Contains(h)) { if (clampToBounds) continue; }
                    yield return h;
                }
        }

        /// 120°（游戏版）：R± = 居中120°（左60 ∪ 右60），Q± = “上半边”列生成
        public static IEnumerable<Hex> Sector120Game(Hex origin, Facing4 facing, int radius,
                                                     HexBoardLayout layout = null, bool clampToBounds = true)
        {
            if (radius <= 0) yield break;

            if (facing == Facing4.PlusR || facing == Facing4.MinusR)
            {
                // 居中120°：左60 ∪ 右60（去重）
                var seen = new HashSet<Hex>();
                foreach (var h in Sector60Left(origin, facing, radius, layout, clampToBounds))
                    if (seen.Add(h)) yield return h;
                foreach (var h in Sector60Right(origin, facing, radius, layout, clampToBounds))
                    if (seen.Add(h)) yield return h;
                yield break;
            }

            // Q±：显式“整列”规则
            int q0 = origin.q, r0 = origin.r;
            if (facing == Facing4.PlusQ)
            {
                for (int s = 1; s <= radius; s++)
                {
                    int q = q0 + s;
                    int rMin = r0 - (s + 1);
                    int rMax = r0 + s;
                    for (int r = rMin; r <= rMax; r++)
                    {
                        var h = new Hex(q, r);
                        if (layout != null && !layout.Contains(h)) { if (clampToBounds) continue; }
                        yield return h;
                    }
                }
            }
            else // Facing4.MinusQ
            {
                for (int s = 1; s <= radius; s++)
                {
                    int q = q0 - s;
                    int rMin = r0 - s;
                    int rMax = r0 + (s + 1);
                    for (int r = rMin; r <= rMax; r++)
                    {
                        var h = new Hex(q, r);
                        if (layout != null && !layout.Contains(h)) { if (clampToBounds) continue; }
                        yield return h;
                    }
                }
            }
        }

        /// 圆形（含原点与边界）
        public static IEnumerable<Hex> Circle(Hex origin, int radius, HexBoardLayout layout = null)
        {
            if (radius < 0) yield break;
            foreach (var h in Hex.Range(origin, radius))
                if (layout == null || layout.Contains(h)) yield return h;
        }

        // 计数（可选）
        public static int SectorCount60(int r) => (r <= 0) ? 0 : r * (r + 3) / 2;
        public static int SectorCount120_Game(Facing4 f, int r) => (r <= 0) ? 0 : ((f == Facing4.PlusQ || f == Facing4.MinusQ) ? r * (r + 3) : r * (r + 2));

        // ========== 统一过滤 ==========
        public sealed class AreaFilterResult
        {
            public readonly List<Hex> Valid = new();
            public readonly List<Hex> Blocked = new();
        }

        public static AreaFilterResult Filter(IEnumerable<Hex> raw, HexBoardLayout layout,
                                              System.Func<Hex, bool> isBlocked = null, bool excludeOOB = true)
        {
            var res = new AreaFilterResult();
            if (raw == null) return res;
            foreach (var h in raw)
            {
                if (excludeOOB && layout != null && !layout.Contains(h)) { res.Blocked.Add(h); continue; }
                if (isBlocked != null && isBlocked(h)) { res.Blocked.Add(h); continue; }
                res.Valid.Add(h);
            }
            return res;
        }

        // 带过滤的便捷封装
        public static AreaFilterResult CircleFiltered(Hex o, int r, HexBoardLayout L, System.Func<Hex, bool> blk = null, bool exOOB = true)
            => Filter(Circle(o, r, L), L, blk, exOOB);

        public static AreaFilterResult Sector60RightFiltered(Hex o, Facing4 f, int r, HexBoardLayout L, System.Func<Hex, bool> blk = null, bool exOOB = true)
            => Filter(Sector60Right(o, f, r, L, true), L, blk, exOOB);

        public static AreaFilterResult Sector60LeftFiltered(Hex o, Facing4 f, int r, HexBoardLayout L, System.Func<Hex, bool> blk = null, bool exOOB = true)
            => Filter(Sector60Left(o, f, r, L, true), L, blk, exOOB);

        public static AreaFilterResult Sector120GameFiltered(Hex o, Facing4 f, int r, HexBoardLayout L, System.Func<Hex, bool> blk = null, bool exOOB = true)
            => Filter(Sector120Game(o, f, r, L, true), L, blk, exOOB);

        // ========== 默认阻挡构造器（供 Target/范围统一使用） ==========
        public static System.Func<Hex, bool> MakeDefaultBlocker(
            HexBoardAuthoringLite authoring,
            HexBoardMap<Unit> map,
            Hex origin,
            bool blockByUnits,
            bool blockByPhysics,
            LayerMask obstacleMask,
            float physicsRadiusScale,
            float physicsProbeHeight,
            bool includeTriggerColliders,
            float y = 0.01f)
        {
            var L = authoring?.Layout;
            float rin = (authoring != null) ? authoring.cellSize * 0.8660254f * physicsRadiusScale : 0.5f;
            var qti = includeTriggerColliders ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

            return (Hex cell) =>
            {
                if (cell.Equals(origin)) return false; // 起点永不阻挡
                if (blockByUnits && map != null && !map.IsFree(cell)) return true;

                if (blockByPhysics && obstacleMask.value != 0 && L != null)
                {
                    Vector3 c = L.World(cell, y);
                    if (Physics.CheckSphere(c + Vector3.up * 0.5f, rin, obstacleMask, qti)) return true;
                    Vector3 p1 = c + Vector3.up * 0.1f, p2 = c + Vector3.up * physicsProbeHeight;
                    if (Physics.CheckCapsule(p1, p2, rin, obstacleMask, qti)) return true;
                }
                return false;
            };
        }
    }
}
