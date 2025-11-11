// File: TGD.CombatV2/System/KnockbackAndHookV2.cs
using TGD.CoreV2;
using UnityEngine;

namespace TGD.CombatV2
{
    /// 水平击退/拉钩的最小辅助（R 轴竖直视角）
    /// 规则：Left步进 (dq,dr)=(-2,+1)，Right步进(+2,-1)，看起来更“水平”。
    public static class KnockbackAndHookV2
    {

        // 轻量距离（按你的 Hex 实现可替换为现成方法）
        static int HexDistance(Hex a, Hex b)
        {
            // 典型 axial 到 cube 再取曼哈顿/2；若你已有 Hex.Distance 直接用
            int aq = a.q, ar = a.r;
            int bq = b.q, br = b.r;
            int ax = aq; int az = ar; int ay = -ax - az;
            int bx = bq; int bz = br; int by = -bx - bz;
            return (Mathf.Abs(ax - bx) + Mathf.Abs(ay - by) + Mathf.Abs(az - bz)) / 2;
        }
    }
}
