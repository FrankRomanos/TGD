// File: TGD.Level/HexCameraControllerHB.cs
using UnityEngine;
using Unity.Cinemachine;
using TGD.HexBoard;

namespace TGD.Level
{
    /// �����������ƣ��������������̣�
    /// - ����м���ק��ˮƽ��ת��Yaw��
    /// - ����Ҽ���ק��ƽ�ƣ������ Forward/Right ��ˮƽͶӰ��
    /// - �����֣����ţ�CinemachineFollow.y �򱾵ؾ��룩
    /// - ��ѡ���ɿ��м��� Snap �� 60�㣨hex �Ѻã�
    [DisallowMultipleComponent]
    public class HexCameraControllerHB : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] CinemachineCamera cineCam;          // CM v3 Camera
        [SerializeField] Transform pivot;                    // ��ת���ᣨ�����Ϊ�������壩
        [SerializeField] HexBoardAuthoringLite authoring;    // ��ѡ���õ� Layout
        [SerializeField] HexBoardLayout layout;

        [Header("Mouse Controls")]
        [SerializeField, Tooltip("�Ҽ���ק��ƽ�������ȣ����絥λ/���أ��Զ���߶�΢����")]
        float panSensitivity = 0.02f;
        [SerializeField, Tooltip("�м���ק����ת�����ȣ���/���أ�")]
        float rotateSensitivity = 0.25f;

        [Header("Zoom")]
        [SerializeField] float zoomSpeed = 6f;
        [SerializeField] float minFollowY = 1f;
        [SerializeField] float maxFollowY = 30f;

        [Header("Quality of Life")]
        [SerializeField, Tooltip("�ɿ��м�ʱ���뵽 60�� �ı���")]
        bool snapYawTo60 = true;

        // �ڲ�״̬
        bool _rotating = false;
        bool _panning = false;
        Vector3 _lastMousePos;
        float _distance = 15f; // �� CM ģʽ�±���

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

            // ��ʼ���룺����� pivot ��ˮƽλ��
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

        // �м���ק��ˮƽ��ת
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

            float yawDelta = delta.x * rotateSensitivity; // ��ˮƽ��ת
            pivot.Rotate(0f, yawDelta, 0f, Space.World);
        }

        // �Ҽ���ק��ƽ��
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

            // ���ݸ߶�����ƽ���ٶȣ�Խ���ߵ�ԽԶ
            float height = CurrentCameraHeight();
            float heightFactor = Mathf.Clamp(height * 0.02f, 0.5f, 3f);

            Vector3 fwd = pivot.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = pivot.right; right.y = 0f; right.Normalize();

            // ��Ļ���ص�����λ�ƣ�X���ң�Y��ǰ����Ϊ����
            Vector3 worldDelta = (right * delta.x + fwd * delta.y) * panSensitivity * heightFactor;
            pivot.position += worldDelta;
        }

        // ���֣�����
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

            // �� CM ģʽ����
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
