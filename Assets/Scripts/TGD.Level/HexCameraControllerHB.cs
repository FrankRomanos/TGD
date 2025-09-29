// File: TGD.Level/HexCameraControllerHB.cs
using UnityEngine;
using Unity.Cinemachine;
using TGD.HexBoard;

namespace TGD.Level
{
    /// 纯鼠标相机控制（适配六边形棋盘）
    /// - 鼠标中键拖拽：水平旋转（Yaw）
    /// - 鼠标右键拖拽：平移（沿相机 Forward/Right 的水平投影）
    /// - 鼠标滚轮：缩放（CinemachineFollow.y 或本地距离）
    /// - 可选：松开中键后 Snap 到 60°（hex 友好）
    [DisallowMultipleComponent]
    public class HexCameraControllerHB : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] CinemachineCamera cineCam;          // CM v3 Camera
        [SerializeField] Transform pivot;                    // 公转枢轴（相机作为其子物体）
        [SerializeField] HexBoardAuthoringLite authoring;    // 可选：拿到 Layout
        [SerializeField] HexBoardLayout layout;

        [Header("Mouse Controls")]
        [SerializeField, Tooltip("右键拖拽的平移灵敏度（世界单位/像素，自动随高度微调）")]
        float panSensitivity = 0.02f;
        [SerializeField, Tooltip("中键拖拽的旋转灵敏度（度/像素）")]
        float rotateSensitivity = 0.25f;

        [Header("Zoom")]
        [SerializeField] float zoomSpeed = 6f;
        [SerializeField] float minFollowY = 1f;
        [SerializeField] float maxFollowY = 30f;

        [Header("Quality of Life")]
        [SerializeField, Tooltip("松开中键时对齐到 60° 的倍数")]
        bool snapYawTo60 = true;

        // 内部状态
        bool _rotating = false;
        bool _panning = false;
        Vector3 _lastMousePos;
        float _distance = 15f; // 非 CM 模式下备用

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

            // 初始对齐：相机看 pivot 的水平位置
            transform.LookAt(new Vector3(pivot.position.x, transform.position.y, pivot.position.z));
        }

        void Update()
        {
            HandleMouseRotate();
            HandleMousePan();
            HandleZoom();
        }

        // ========== Public API ==========
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

        // ========== Mouse ==========

        // 中键拖拽：水平旋转
        void HandleMouseRotate()
        {
            if (Input.GetMouseButtonDown(2))
            {
                _rotating = true;
                _lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(2))
            {
                if (_rotating && snapYawTo60)
                {
                    float yaw = pivot.eulerAngles.y;
                    float snapped = Mathf.Round(yaw / 60f) * 60f;
                    pivot.rotation = Quaternion.Euler(0f, snapped, 0f);
                }
                _rotating = false;
            }

            if (!_rotating) return;

            var cur = Input.mousePosition;
            var delta = cur - _lastMousePos;
            _lastMousePos = cur;

            float yawDelta = delta.x * rotateSensitivity; // 仅水平旋转
            pivot.Rotate(0f, yawDelta, 0f, Space.World);
        }

        // 右键拖拽：平移
        void HandleMousePan()
        {
            if (Input.GetMouseButtonDown(1))
            {
                _panning = true;
                _lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(1))
            {
                _panning = false;
            }

            if (!_panning) return;

            var cur = Input.mousePosition;
            var delta = cur - _lastMousePos;
            _lastMousePos = cur;

            // 根据高度适配平移速度：越高走得越远
            float height = CurrentCameraHeight();
            float heightFactor = Mathf.Clamp(height * 0.02f, 0.5f, 3f);

            Vector3 fwd = pivot.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = pivot.right; right.y = 0f; right.Normalize();

            // 屏幕像素到世界位移：X→右，Y→前（上为正）
            Vector3 worldDelta = (right * delta.x + fwd * delta.y) * panSensitivity * heightFactor;
            pivot.position += worldDelta;
        }

        // 滚轮：缩放
        void HandleZoom()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) return;

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

            // 非 CM 模式兜底
            _distance = Mathf.Clamp(_distance - wheel * zoomSpeed, minFollowY, maxFollowY);
            transform.localPosition = new Vector3(0f, _distance, -_distance);
            transform.LookAt(new Vector3(pivot.position.x, transform.position.y, pivot.position.z));
        }

        float CurrentCameraHeight()
        {
            if (cineCam != null)
            {
                var follow = cineCam.GetComponent<CinemachineFollow>();
                if (follow != null) return follow.FollowOffset.y;
            }
            return Mathf.Max(1f, transform.position.y - pivot.position.y);
        }
    }
}
