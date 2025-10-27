// File: TGD.Level/HexCameraControllerHB.cs
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.EventSystems;
using TGD.HexBoard;
using TGD.UIV2;

namespace TGD.LevelV2
{
    [DisallowMultipleComponent]
    public class HexCameraControllerHB : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] CinemachineCamera cineCam;
        [SerializeField] Transform pivot;
        [SerializeField] HexBoardAuthoringLite authoring;
        [SerializeField] HexBoardLayout layout;

        [Header("Rotate — MMB 按住")]
        [SerializeField] float rotateDegPerScreen = 180f;
        [SerializeField] bool snapYawTo60 = true;

        [Header("Zoom — 滚轮（保持俯角）")]
        [SerializeField] float zoomSpeed = 6f;
        [SerializeField] float minFollowY = 6f;
        [SerializeField] float maxFollowY = 30f;
        [SerializeField] float tiltDeg = 53f;
        [SerializeField] bool zoomTowardMouse = true;
        [SerializeField] float zoomTowardLerp = 0.15f;
        [SerializeField] float defaultFollowY = 10f;

        // 🔒 缩放保护（防“丢焦点”）
        [SerializeField] float zoomTowardMaxStep = 2.0f;   // 每次缩放 pivot 最大移动步长
        [SerializeField] float zoomTowardMaxDistance = 12f; // 超过这个距离就忽略“朝鼠标缩放”

        [Header("Edge Scroll — 屏幕边缘移动")]
        [SerializeField] bool edgeScrollEnabled = true;
        [SerializeField] int edgeThresholdPx = 22;
        [SerializeField] int edgeExitThresholdPx = 36;
        [SerializeField] float edgeDwellSeconds = 0.8f;
        [SerializeField] float baseEdgeSpeed = 10f;
        [SerializeField] float edgeSpeedMinScale = 0.7f;
        [SerializeField] float edgeSpeedMaxScale = 2.0f;
        [SerializeField] bool edgeDisableWhenAnyMouseDown = true;

        [Header("Key Pan — 方向键平移")]
        [SerializeField] bool keyPanEnabled = true;
        [SerializeField] float keyPanSpeed = 10f;           // 世界单位/秒
        [SerializeField] float keyPanFastMultiplier = 2f;   // Shift
        [SerializeField] float keyPanSlowMultiplier = 0.5f; // Ctrl

        [Header("Clamp Bounds（可选）")]
        [SerializeField] bool clampToBounds = false;
        [SerializeField] Vector2 boundsMinXZ = new(-100f, -100f);
        [SerializeField] Vector2 boundsMaxXZ = new(100f, 100f);

        bool _rotating;
        Vector3 _lastMousePos;
        Vector3 _defaultPivotPosition;

        // 边缘滚动状态
        bool _edgeActive;
        float _edgeEnterTime = -1f;

        Quaternion _defaultPivotRotation;
        Vector3 _defaultFollowOffset;

        void Awake()
        {
            if (layout == null && authoring != null) layout = authoring.Layout;
            if (pivot == null)
            {
                var go = new GameObject("CameraPivot");
                go.transform.position = transform.position;
                go.transform.rotation = transform.rotation;
                pivot = go.transform;
            }

            ApplyDefaultFollowOffset();
            CacheDefaultPivotState();
        }

        void Update()
        {
            HandleMouseRotate();     // 中键按住才旋转
            HandleKeyPan();          // ↑↓←→ 平移
            HandleEdgeScroll();      // 屏幕边缘（有停留时间）
            HandleZoom();            // y/z 联动 + 保护
            if (Input.GetKeyDown(KeyCode.Space)) ResetCameraToDefault();
            if (clampToBounds) ClampToMapBounds();
        }

        // ===== Public API =====
        public HexBoardLayout Layout => layout;

        public float FocusPlaneY => pivot != null ? pivot.position.y : _defaultPivotPosition.y;

        public Hex GetFocusCoordinate()
        {
            if (layout == null) return Hex.Zero;
            return layout.HexAt(pivot.position);
        }
        public Vector3 GetFocusWorldPosition() => pivot != null ? pivot.position : _defaultPivotPosition;
        public void FocusOn(Hex h)
        {
            if (layout == null) return;
            var world = layout.World(h, 0f);
            world = AdjustToPivotPlane(world);
            pivot.position = world;
        }

        public void ResetFocus(bool smooth = true)
        {
            if (pivot == null) return;
            _ = smooth;

            var adjusted = AdjustToPivotPlane(_defaultPivotPosition);
            pivot.position = adjusted;
        }

        // ===== Handlers =====

        // 中键旋转（按住才旋转）
        void HandleMouseRotate()
        {
            if (Input.GetMouseButtonDown(2))
            {
                _rotating = true;
                _lastMousePos = Input.mousePosition;
                _edgeActive = false;
                _edgeEnterTime = -1f;
            }
            else if (Input.GetMouseButtonUp(2))
            {
                if (_rotating && snapYawTo60)
                {
                    float yaw = pivot.eulerAngles.y;
                    float snapped = Mathf.Round(yaw / 60f) * 60f;
                    var current = pivot.eulerAngles;
                    current.y = snapped;
                    pivot.rotation = Quaternion.Euler(current);
                }
                _rotating = false;
                _lastMousePos = Input.mousePosition;
                _edgeActive = false;
                _edgeEnterTime = Time.unscaledTime + 0.2f; // 松手后 0.2s 内不触发边缘滚动
            }

            if (!_rotating) return;

            var cur = Input.mousePosition;
            var delta = (Vector2)(cur - _lastMousePos);
            _lastMousePos = cur;

            float normX = delta.x / Mathf.Max(1f, Screen.width);
            float yawDelta = normX * rotateDegPerScreen; // 度
            Vector3 keepPos = pivot.position;
            pivot.Rotate(0f, yawDelta, 0f, Space.World);
            pivot.position = keepPos;
        }

        // ↑↓←→ 平移（与边缘滚动互斥）
        void HandleKeyPan()
        {
            if (!keyPanEnabled) return;

            float x = 0f, y = 0f;
            if (Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.UpArrow)) y += 1f;

            if (Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f)) return;

            float speed = keyPanSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= keyPanFastMultiplier;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                speed *= keyPanSlowMultiplier;

            Vector3 fwd = pivot.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = pivot.right; right.y = 0f; right.Normalize();

            Vector3 dir = (right * x + fwd * y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            pivot.position += dir * speed * Time.deltaTime;

            // 键盘平移时禁用边缘滚动
            _edgeActive = false;
            _edgeEnterTime = -1f;
        }

        // 屏幕边缘滚动（带停留 & 回滞）
        void HandleEdgeScroll()
        {
            if (!edgeScrollEnabled) return;
            if (_rotating) { _edgeActive = false; _edgeEnterTime = -1f; return; }
            if (edgeDisableWhenAnyMouseDown &&
               (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)))
            { _edgeActive = false; _edgeEnterTime = -1f; return; }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            { _edgeActive = false; _edgeEnterTime = -1f; return; }

            int thr = _edgeActive ? edgeExitThresholdPx : edgeThresholdPx;

            Vector2 move = Vector2.zero;
            Vector2 mp = Input.mousePosition;

            if (mp.x <= thr) move.x = -Mathf.InverseLerp(thr, 0f, mp.x);
            else if (mp.x >= Screen.width - thr) move.x = Mathf.InverseLerp(Screen.width - thr, Screen.width, mp.x);

            if (mp.y <= thr) move.y = -Mathf.InverseLerp(thr, 0f, mp.y);
            else if (mp.y >= Screen.height - thr) move.y = Mathf.InverseLerp(Screen.height - thr, Screen.height, mp.y);

            bool inside = move.sqrMagnitude > 1e-6f;
            if (!inside) { _edgeActive = false; _edgeEnterTime = -1f; return; }

            if (!_edgeActive) { _edgeActive = true; _edgeEnterTime = Time.unscaledTime; return; }
            if (Time.unscaledTime - _edgeEnterTime < edgeDwellSeconds) return;

            float h = CurrentCameraHeight();
            float t = Mathf.InverseLerp(minFollowY, maxFollowY, Mathf.Clamp(h, minFollowY, maxFollowY));
            float speed = baseEdgeSpeed * Mathf.Lerp(edgeSpeedMinScale, edgeSpeedMaxScale, t);

            Vector3 fwd = pivot.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = pivot.right; right.y = 0f; right.Normalize();
            Vector3 worldDir = (right * move.x + fwd * move.y).normalized;

            float intensity = Mathf.Clamp01(move.magnitude);
            pivot.position += worldDir * (speed * intensity) * Time.deltaTime;
        }

        // 滚轮缩放（固定俯角 + 丢焦保护）
        void HandleZoom()
        {
            if (ChainPopupState.IsVisible)
                return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) return;

            var follow = (cineCam != null) ? cineCam.GetComponent<CinemachineFollow>() : null;
            if (follow == null) return;

            Vector3 centerAnchor = Vector3.zero;
            bool hasCenterAnchor = TryProjectScreenPointToGround(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out centerAnchor);
            if (hasCenterAnchor && pivot != null)
                centerAnchor.y = pivot.position.y;

            var off = follow.FollowOffset;
            float newY = Mathf.Clamp(off.y - wheel * zoomSpeed, minFollowY, maxFollowY);

            float tiltRad = Mathf.Deg2Rad * Mathf.Clamp(tiltDeg, 1f, 89f);
            off.y = newY;
            off.z = -newY / Mathf.Tan(tiltRad);
            follow.FollowOffset = off;

            bool pivotAdjusted = false;

            if (pivot != null && zoomTowardMouse && TryProjectMouseToGround(out var hit))
            {
                hit.y = pivot.position.y;
                Vector3 delta = hit - pivot.position;
                float dist = delta.magnitude;

                // 超远目标不采用“朝鼠标缩放”
                if (dist <= zoomTowardMaxDistance)
                {
                    Vector3 target = Vector3.Lerp(pivot.position, hit, Mathf.Clamp01(zoomTowardLerp));
                    Vector3 step = target - pivot.position;

                    float maxStep = Mathf.Max(0f, zoomTowardMaxStep);
                    if (maxStep > 0f && step.magnitude > maxStep)
                        step = step.normalized * maxStep;

                    pivot.position += step;
                    pivotAdjusted = true;
                }
            }

            if (!pivotAdjusted && hasCenterAnchor && pivot != null)
            {
                pivot.position = new Vector3(centerAnchor.x, pivot.position.y, centerAnchor.z);
            }
        }

        float CurrentCameraHeight()
        {
            var follow = (cineCam != null) ? cineCam.GetComponent<CinemachineFollow>() : null;
            if (follow != null) return follow.FollowOffset.y;
            return Mathf.Max(1f, transform.position.y - pivot.position.y);
        }

        void ClampToMapBounds()
        {
            var pos = pivot.position;
            pos.x = Mathf.Clamp(pos.x, boundsMinXZ.x, boundsMaxXZ.x);
            pos.z = Mathf.Clamp(pos.z, boundsMinXZ.y, boundsMaxXZ.y);
            pivot.position = pos;
        }

        void ApplyDefaultFollowOffset()
        {
            if (cineCam == null)
                return;

            var follow = cineCam.GetComponent<CinemachineFollow>();
            if (follow == null)
                return;

            var off = follow.FollowOffset;

            if (defaultFollowY > 0f)
            {
                float height = Mathf.Clamp(defaultFollowY, minFollowY, maxFollowY);
                if (!Mathf.Approximately(off.y, height))
                    off.y = height;

                float tilt = Mathf.Deg2Rad * Mathf.Clamp(tiltDeg, 1f, 89f);
                off.z = -height / Mathf.Tan(tilt);
                follow.FollowOffset = off;
            }

            _defaultFollowOffset = follow.FollowOffset;
        }

        void CacheDefaultPivotState()
        {
            if (pivot != null)
            {
                _defaultPivotPosition = pivot.position;
                _defaultPivotRotation = pivot.rotation;
            }
            else
            {
                _defaultPivotPosition = Vector3.zero;
                _defaultPivotRotation = Quaternion.identity;
            }
        }

        void ResetCameraToDefault()
        {
            if (pivot != null)
            {
                pivot.position = _defaultPivotPosition;
                pivot.rotation = _defaultPivotRotation;
            }

            if (cineCam != null)
            {
                var follow = cineCam.GetComponent<CinemachineFollow>();
                if (follow != null)
                {
                    follow.FollowOffset = _defaultFollowOffset;
                }
            }
        }

        bool TryProjectMouseToGround(out Vector3 hit)
        {
            return TryProjectScreenPointToGround(Input.mousePosition, out hit);
        }

        bool TryProjectScreenPointToGround(Vector2 screenPoint, out Vector3 hit)
        {
            var cam = Camera.main != null ? Camera.main : GetComponent<Camera>();
            if (cam == null) { hit = default; return false; }

            var ray = cam.ScreenPointToRay(screenPoint);
            // 用经过 pivot 的水平面；如果你的地形有高度，以后可以换 Physics.Raycast + 地面层
            var pivotPos = pivot != null ? pivot.position : transform.position;
            var plane = new Plane(Vector3.up, new Vector3(0f, pivotPos.y, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                hit = ray.GetPoint(enter);
                return true;
            }
            hit = default;
            return false;
        }

        Vector3 AdjustToPivotPlane(Vector3 world)
        {
            float y = pivot != null ? pivot.position.y : _defaultPivotPosition.y;
            world.y = y;
            return world;
        }
    }
}
