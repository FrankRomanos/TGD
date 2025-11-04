using Shapes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TGD.VFX
{
    public class TelegraphRadialDots : MonoBehaviour
    {
        [Header("Layout")]
        public int dotCount = 24;
        public float maxRadius = 4f;     // 扩散到多远
        public float radialSpeed = 2f;   // 扩散速度 (m/s)
        public float spinDegPerSec = 90f; // 整体慢速自转

        [Header("Dot Look")]
        public float dotRadius = 0.06f;  // 每个点自身半径
        public Color dotColor = new Color(1f, 0.6f, 0.1f, 0.9f);

        class Dot
        {
            public Disc disc;
            public float angle;   // 当前角度（弧度）
            public float radius;  // 当前半径
        }

        readonly List<Dot> _dots = new();

        void Start()
        {
            SpawnDots();
        }

        void SpawnDots()
        {
            ClearDots();

            for (int i = 0; i < dotCount; i++)
            {
                var go = new GameObject($"dot_{i}");
                go.transform.SetParent(transform, false);
                // 让点躺在地面：父物体已经 -90°，子物体保持 0 即可
                var disc = go.AddComponent<Disc>();
                disc.Type = DiscType.Disc;        // 实心小圆点
                disc.Radius = dotRadius;
                disc.Color = dotColor;
                disc.ZTest = CompareFunction.LessEqual; // 或 Always，看你是否要被地形遮挡

                var d = new Dot
                {
                    disc = disc,
                    angle = (Mathf.PI * 2f) * (i / (float)dotCount),
                    radius = 0f
                };
                _dots.Add(d);
            }
        }

        void ClearDots()
        {
            for (int i = 0; i < _dots.Count; i++)
                if (_dots[i].disc) Destroy(_dots[i].disc.gameObject);
            _dots.Clear();
        }

        void Update()
        {
            // 慢速自转（让整体有生命力）
            transform.Rotate(Vector3.up, spinDegPerSec * Time.deltaTime, Space.World);

            float dt = Time.deltaTime;

            foreach (var d in _dots)
            {
                d.radius += radialSpeed * dt;
                if (d.radius > maxRadius)
                {
                    // 回到中心重新发散，形成“持续蓄力”的循环感
                    d.radius = 0f;
                    // 也可以轻微抖动下角度
                    d.angle += Random.Range(-0.15f, 0.15f);
                }

                var pos = new Vector3(Mathf.Cos(d.angle), 0f, Mathf.Sin(d.angle)) * d.radius;
                d.disc.transform.localPosition = pos;

                // 距离越远越透明（临场感）
                var c = dotColor;
                float fade = Mathf.InverseLerp(maxRadius, 0f, d.radius); // 近亮远暗
                c.a *= Mathf.Lerp(0.2f, 1f, fade);
                d.disc.Color = c;
            }
        }
    }
}
