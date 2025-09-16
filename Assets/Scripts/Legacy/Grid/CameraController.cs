using UnityEngine;
using Unity.Cinemachine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private CinemachineCamera CinemachineCamera;
    private const float Min_FOLLOW_Y_OFFSET = 1f;
    private const float MAX_FOLLOW_Y_OFFSET = 9f;
    private void Update()
    {
        HandleMovement();
        HandleRotation();
        HandleZoom();
    }

    private void HandleMovement()
    {
        Vector3 inputMoiveDir = new Vector3(0, 0, 0);
        if (Input.GetKey(KeyCode.W))
        {
            inputMoiveDir.z = +1f;
        }
        if (Input.GetKey(KeyCode.S))
        {
            inputMoiveDir.z = -1f;
        }
        if (Input.GetKey(KeyCode.A))
        {
            inputMoiveDir.x = -1f;
        }
        if (Input.GetKey(KeyCode.D))
        {
            inputMoiveDir.x = +1f;
        }

        float moveSpeed = 10f;

        Matrix4x4 rotationMatrix = Matrix4x4.Rotate(transform.rotation);
        Vector3 worldForward = rotationMatrix.MultiplyPoint3x4(Vector3.forward);
        worldForward.y = 0;
        worldForward.Normalize();
        Vector3 worldRight = rotationMatrix.MultiplyPoint3x4(Vector3.right);
        worldRight.y = 0;
        worldRight.Normalize();

        Vector3 moveVector = worldForward * inputMoiveDir.z + worldRight * inputMoiveDir.x;
        transform.position += moveVector * moveSpeed * Time.deltaTime;
    }

    private void HandleRotation()
    {
        Vector3 rotationVector = new Vector3(0, 0, 0);

        if (Input.GetKey(KeyCode.Q))
        {
            rotationVector.y = +1f;
        }

        if (Input.GetKey(KeyCode.E))
        {
            rotationVector.y = -1f;
        }

        float rotationSpeed = 100f;
        transform.eulerAngles += rotationVector * rotationSpeed * Time.deltaTime;
    }

    private void HandleZoom()
    {
        CinemachineFollow cinemachineFollow = CinemachineCamera.GetComponent<CinemachineFollow>();
        Vector3 followOffset = cinemachineFollow.FollowOffset;
        float zoomAmount = 1f;

        if (Input.mouseScrollDelta.y > 0)
        {
            followOffset.y -= zoomAmount;
        }

        if (Input.mouseScrollDelta.y < 0)
        {
            followOffset.y += zoomAmount;
        }

        followOffset.y = Mathf.Clamp(followOffset.y, Min_FOLLOW_Y_OFFSET, MAX_FOLLOW_Y_OFFSET);
        cinemachineFollow.FollowOffset = followOffset;
    }
}
