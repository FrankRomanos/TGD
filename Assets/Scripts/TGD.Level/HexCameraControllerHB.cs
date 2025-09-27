using UnityEngine;
using Unity.Cinemachine;          // v3: CinemachineCamera / CinemachineFollow
using TGD.HexBoard;               // ���� HexBoardLayout / Hex / Authoring

namespace TGD.Level
{
    /// <summary>
    /// ���� HexBoard ��������ƣ�
    /// - WASD ƽ�ƣ���������ˮƽ�棩
    /// - Q/E ��ת����ѡ 60�� ������룩
    /// - �������ţ�CinemachineFollow.y �򱾵ؾ��룩
    /// - FocusOn(Hex) / GetFocusCoordinate()
    /// </summary>
    public class HexCameraControllerHB : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] CinemachineCamera cineCam;          // ��� CM Camera
        [SerializeField] Transform pivot;                    // �����ת�����᣻�������Զ�����
        [SerializeField] HexBoardAuthoringLite authoring;    // ��ѡ���Զ��� Layout
        [SerializeField] HexBoardLayout layout;              // Ҳ��ֱ���� Layout

        [Header("Move / Rotate / Zoom")]
        [SerializeField] float moveSpeed = 12f;
        [SerializeField] float rotateSpeed = 100f;
        [SerializeField] float zoomSpeed = 6f;
        [SerializeField] float minFollowY = 1f;
        [SerializeField] float maxFollowY = 30f;

        [Header("Quality of Life")]
        [SerializeField] bool snapYawTo60 = false;           // ��ת���ֺ���� 60��

        // �ڲ�
        float _distance = 15f; // �� Cinemachine ģʽ����
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

            // ��ʼ���룺������� pivot ˮƽ
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

            // �������� Cinemachine v3
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

            // ���ף��� CM ģʽ��ֱ������/Զ
            _distance = Mathf.Clamp(_distance - wheel * zoomSpeed, minFollowY, maxFollowY);
            transform.localPosition = new Vector3(0f, _distance, -_distance);
            transform.LookAt(new Vector3(pivot.position.x, transform.position.y, pivot.position.z));
        }
    }
}
