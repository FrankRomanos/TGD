using Unity.Cinemachine;
using UnityEngine;

namespace TGD.Grid
{
    /// <summary>
    /// Simple camera controller compatible with the hex grid layout.
    /// </summary>
    public class HexCameraController : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera cinemachineCamera;
        private const float MinFollowYOffset = 1f;
        private const float MaxFollowYOffset = 9f;

        public HexCoord GetFocusCoordinate(HexGridLayout layout)
        {
            if (layout == null)
                return HexCoord.Zero;
            return layout.GetCoordinate(transform.position);
        }

        public Vector3 GetFocusWorldPosition(HexGridLayout layout)
        {
            if (layout == null)
                return transform.position;
            var coord = GetFocusCoordinate(layout);
            return layout.GetWorldPosition(coord);
        }

        private void Update()
        {
            HandleMovement();
            HandleRotation();
            HandleZoom();
        }

        private void HandleMovement()
        {
            Vector3 inputMoveDir = Vector3.zero;
            if (Input.GetKey(KeyCode.W))
                inputMoveDir.z = 1f;
            if (Input.GetKey(KeyCode.S))
                inputMoveDir.z = -1f;
            if (Input.GetKey(KeyCode.A))
                inputMoveDir.x = -1f;
            if (Input.GetKey(KeyCode.D))
                inputMoveDir.x = 1f;

            if (inputMoveDir == Vector3.zero)
                return;

            const float moveSpeed = 10f;
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(transform.rotation);
            Vector3 worldForward = rotationMatrix.MultiplyPoint3x4(Vector3.forward);
            worldForward.y = 0f;
            worldForward.Normalize();

            Vector3 worldRight = rotationMatrix.MultiplyPoint3x4(Vector3.right);
            worldRight.y = 0f;
            worldRight.Normalize();

            Vector3 moveVector = worldForward * inputMoveDir.z + worldRight * inputMoveDir.x;
            transform.position += moveVector * moveSpeed * Time.deltaTime;
        }

        private void HandleRotation()
        {
            float rotation = 0f;
            if (Input.GetKey(KeyCode.Q))
                rotation += 1f;
            if (Input.GetKey(KeyCode.E))
                rotation -= 1f;
            if (Mathf.Approximately(rotation, 0f))
                return;

            const float rotationSpeed = 100f;
            transform.eulerAngles += new Vector3(0f, rotation * rotationSpeed * Time.deltaTime, 0f);
        }

        private void HandleZoom()
        {
            if (cinemachineCamera == null)
                return;

            CinemachineFollow follow = cinemachineCamera.GetComponent<CinemachineFollow>();
            if (follow == null)
                return;

            Vector3 followOffset = follow.FollowOffset;
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(delta, 0f))
                return;

            followOffset.y -= delta;
            followOffset.y = Mathf.Clamp(followOffset.y, MinFollowYOffset, MaxFollowYOffset);
            follow.FollowOffset = followOffset;
        }
    }
}