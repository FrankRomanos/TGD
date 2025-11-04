using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 0° = r+（+Z），90° = q+（+X），180° = r-，270° = q-
    public static class HexFacingUtil
    {
        public static float YawFromFacing(Facing4 f) => f switch
        {
            Facing4.PlusR => 0f,
            Facing4.PlusQ => 90f,
            Facing4.MinusR => 180f,
            _ => 270f,
        };

        public static Facing4 FacingFromYaw4(float yawDegrees)
        {
            int a = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) / 90f) * 90;
            return (a % 360) switch
            {
                0 => Facing4.PlusR,
                90 => Facing4.PlusQ,
                180 => Facing4.MinusR,
                270 => Facing4.MinusQ,
                _ => Facing4.PlusR
            };
        }

        public static Facing4 LeftOf(Facing4 f) => f switch  // 逆时针 90°
        {
            Facing4.PlusR => Facing4.MinusQ,
            Facing4.MinusQ => Facing4.MinusR,
            Facing4.MinusR => Facing4.PlusQ,
            _ => Facing4.PlusR,
        };

        public static Facing4 RightOf(Facing4 f) => f switch // 顺时针 90°
        {
            Facing4.PlusR => Facing4.PlusQ,
            Facing4.PlusQ => Facing4.MinusR,
            Facing4.MinusR => Facing4.MinusQ,
            _ => Facing4.PlusR,
        };

        public static Facing4 OppositeOf(Facing4 f) => f switch
        {
            Facing4.PlusR => Facing4.MinusR,
            Facing4.MinusR => Facing4.PlusR,
            Facing4.PlusQ => Facing4.MinusQ,
            _ => Facing4.PlusQ,
        };

        static Vector2 BasisXZ(Facing4 f) => f switch
        {
            Facing4.PlusR => new Vector2(0f, 1f),
            Facing4.MinusR => new Vector2(0f, -1f),
            Facing4.PlusQ => new Vector2(1f, 0f),
            _ => new Vector2(-1f, 0f),
        };

        /// 45° 扇区一次性转向：|δ|≤keep -> 保持； ≤turn -> 左/右相邻；>turn -> 反向
        public static (Facing4 facing, float targetYaw) ChooseFacingByAngle45(
            Facing4 currentFacing, Vector3 fromWorld, Vector3 toWorld,
            float keepDeg = 45f, float turnDeg = 135f)
        {
            Vector3 v3 = toWorld - fromWorld; v3.y = 0f;
            if (v3.sqrMagnitude < 1e-6f) return (currentFacing, YawFromFacing(currentFacing));

            Vector2 v = new Vector2(v3.x, v3.z).normalized;
            Vector2 f = BasisXZ(currentFacing);

            float dot = f.x * v.x + f.y * v.y;
            float cross = f.x * v.y - f.y * v.x; // 左正右负
            float delta = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg; // (-180,180]
            float a = Mathf.Abs(delta);

            if (a <= keepDeg)
                return (currentFacing, YawFromFacing(currentFacing));
            else if (a <= turnDeg)
            {
                var nf = (delta > 0f) ? LeftOf(currentFacing) : RightOf(currentFacing);
                return (nf, YawFromFacing(nf));
            }
            else
            {
                var nf = OppositeOf(currentFacing);
                return (nf, YawFromFacing(nf));
            }
        }

        /// 旋转协程：以角速度(度/秒)缓动到目标 yaw
        public static System.Collections.IEnumerator RotateToYaw(Transform t, float targetYaw, float degPerSec = 720f)
        {
            float Normalize(float a) => (a % 360f + 360f) % 360f;
            var cur = Normalize(t.eulerAngles.y);
            var dst = Normalize(targetYaw);
            float delta = Mathf.DeltaAngle(cur, dst);

            while (Mathf.Abs(delta) > 0.5f)
            {
                float step = Mathf.Sign(delta) * degPerSec * Time.deltaTime;
                if (Mathf.Abs(step) > Mathf.Abs(delta)) step = delta;
                t.Rotate(0f, step, 0f, Space.World);
                cur = Normalize(t.eulerAngles.y);
                delta = Mathf.DeltaAngle(cur, dst);
                yield return null;
            }
            var e = t.eulerAngles;
            t.eulerAngles = new Vector3(e.x, dst, e.z);
        }
    }
}
