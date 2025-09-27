using UnityEngine;
using Unity.Cinemachine;          // v3: CinemachineCamera / CinemachineFollow
using TGD.HexBoard;               // 引入 HexBoardLayout / Hex / Authoring

namespace TGD.Level
{
    /// <summary>
    /// 适配 HexBoard 的相机控制：
    /// - WASD 平移（相机朝向的水平面）
    /// - Q/E 旋转（可选 60° 网格对齐）
    /// - 滚轮缩放（CinemachineFollow.y 或本地距离）
    /// - FocusOn(Hex) / GetFocusCoordinate()
    /// </summary>
    public class HexCameraControllerHB : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] CinemachineCamera cineCam;          // 你的 CM Camera
        [SerializeField] Transform pivot;                    // 相机公转的枢轴；留空则自动创建
        [SerializeField] HexBoardAuthoringLite authoring;    // 可选：自动拿 Layout
        [SerializeField] HexBoardLayout layout;              // 也可直接拖 Layout

        [Header("Move / Rotate / Zoom")]
        [SerializeField] float moveSpeed = 12f;
        [SerializeField] float rotateSpeed = 100f;
        [SerializeField] float zoomSpeed = 6f;
        [SerializeField] float minFollowY = 1f;
        [SerializeField] float maxFollowY = 30f;

        [Header("Quality of Life")]
        [SerializeField] bool snapYawTo60 = false;           // 旋转松手后对齐 60°

        // 内部
        float _distance = 15f; // 非 Cinemachine 模式下用
        bool _rotating = false;

        void Awake()
        {
            if (layout == null && authoring != null) layout = authoring.Layout;

            if (pivot == null)
            {
                var go = new GameObject("CameraPivot");
                go.transform.position = transform.position;
                go.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                pivot = go.transform;
                transform.SetParent(pivot, worldPositionStays: true);
            }

            // 初始对齐：相机看向 pivot 水平
            transform.LookAt(new Vector3(pivot.position.x, transform.position.y, pivot.position.z));
        }

        void Update()
        {
            HandleMovement();
            HandleRotation();
            HandleZoom();
        }

        // ======== Public API ========
        public Hex GetFocusCoordinate()
        {
            if (layout == null) return Hex.Zero;
            return layout.HexAt(transform.position);
        }

        public Vector3 GetFocusWorldPosition()
        {
            if (layout == null) return transform.position;
            var h = GetFocusCoordinate();
            return layout.World(h, 0f);
        }

        public void FocusOn(Hex h)
        {
            if (layout == null) return;
            Vector3 w = layout.World(h, 0f);
            pivot.position = w;
        }

        // ======== Controls ========
        void HandleMovement()
        {
            Vector3 input = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) input.z += 1f;
            if (Input.GetKey(KeyCode.S)) input.z -= 1f;
            if (Input.GetKey(KeyCode.A)) input.x -= 1f;
            if (Input.GetKey(KeyCode.D)) input.x += 1f;
            if (input.sqrMagnitude == 0f) return;

            Vector3 fwd = pivot.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = pivot.right; right.y = 0f; right.Normalize();
            Vector3 delta = (fwd * input.z + right * input.x) * moveSpeed * Time.deltaTime;

            pivot.position += delta;
        }

        void HandleRotation()
        {
            float dir = 0f;
            if (Input.GetKey(KeyCode.Q)) dir += 1f;
            if (Input.GetKey(KeyCode.E)) dir -= 1f;

            if (Mathf.Approximately(dir, 0f))
            {
                if (_rotating && snapYawTo60 && !Input.GetKey(KeyCode.Q) && !Input.GetKey(KeyCode.E))
                {
                    _rotating = false;
                    float yaw = pivot.eulerAngles.y;
                    float snapped = Mathf.Round(yaw / 60f) * 60f;
                    pivot.rotation = Quaternion.Euler(0f, snapped, 0f);
                }
                return;
            }

            _rotating = true;
            pivot.Rotate(0f, dir * rotateSpeed * Time.deltaTime, 0f, Space.World);
        }

        void HandleZoom()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) return;

            // 优先适配 Cinemachine v3
            if (cineCam != null)
            {
                var follow = cineCam.GetComponent<CinemachineFollow>();
                if (follow != null)
                {
                    var off = follow.FollowOffset;
                    off.y = Mathf.Clamp(off.y - wheel * zoomSpeed, minFollowY, maxFollowY);
                    follow.FollowOffset = off;
                    return;
                }
            }

            // 兜底：非 CM 模式下直接拉近/远
            _distance = Mathf.Clamp(_distance - wheel * zoomSpeed, minFollowY, maxFollowY);
            transform.localPosition = new Vector3(0f, _distance, -_distance);
            transform.LookAt(new Vector3(pivot.position.x, transform.position.y, pivot.position.z));
        }
    }
}
