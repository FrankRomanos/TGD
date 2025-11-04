using UnityEngine;
using Shapes;

namespace TGD.VFXV2
{
    // 旋转 + 厚度/透明度脉冲 + 可选倒计时（角度收缩）
    public class TelegraphRingSpinner : MonoBehaviour
    {
        [Header("Refs")]
        public Disc disc;                  // 绑定一个 Shapes/Disc (Type=Ring)

        [Header("Spin / Pulse")]
        public float spinDegPerSec = 180f; // 旋转速度
        public float pulseHz = 2f;         // 脉冲频率
        [Range(0f, 0.9f)] public float pulseAmp = 0.2f;  // 厚度脉冲幅度(百分比)
        [Range(0f, 1f)] public float alphaMin = 0.35f;   // 最暗透明度

        [Header("Countdown (optional)")]
        public bool showCountdownArc = true;    // 开启后会从满圆慢慢收口
        public float countdownSeconds = 0f;     // >0 时开始倒计时
        float _countdownLeft;

        float _baseThickness;
        Color _baseColor;

        void Awake()
        {
            if (!disc) disc = GetComponentInChildren<Disc>();
            if (disc)
            {
                _baseThickness = disc.Thickness;
                _baseColor = disc.Color;
            }
        }

        public void Arm(float seconds)
        {
            countdownSeconds = Mathf.Max(0, seconds);
            _countdownLeft = countdownSeconds;
        }

        void Update()
        {
            if (!disc) return;

            // 1) 自转
            transform.Rotate(Vector3.up, spinDegPerSec * Time.deltaTime, Space.World);

            // 2) 厚度/透明度脉冲
            float pulse = 1f + pulseAmp * Mathf.Sin(Time.time * Mathf.PI * 2f * pulseHz);
            disc.Thickness = _baseThickness * pulse;
            var c = _baseColor;
            c.a = Mathf.Lerp(alphaMin, _baseColor.a, (pulse - (1f - pulseAmp)) / (pulseAmp * 2f));
            disc.Color = c;

            // 3) 倒计时（用圆环的角度收口表达“快爆了”）
            if (showCountdownArc && countdownSeconds > 0f)
            {
                _countdownLeft = Mathf.Max(0f, _countdownLeft - Time.deltaTime);
                float t = (_countdownLeft / countdownSeconds);
                // Shapes 的 Disc 支持扇形/圆弧，常见字段是 AngleStart/AngleEnd（单位：度）
                // 这里用 360 * t 表达从满圆 → 0 的收口（如果你的版本字段名不同，按组件 Inspector 的名字替换即可）
                disc.AngRadiansStart = 0f;
                disc.AngRadiansEnd = Mathf.Deg2Rad * (360f * t);
            }
        }
    }
}
