using UnityEngine;

public class PlayerCameraModeController : MonoBehaviour
{
    [Header("Camera References")]
    public vThirdPersonCamera thirdPersonCamera;
    public Camera playerCamera;
    public Transform firstPersonAnchor;

    [Header("Camera Settings")]
    public KeyCode cameraToggleKey = KeyCode.C;
    public float firstPersonSensitivity = 3f;
    public float minimumPitch = -40f;
    public float maximumPitch = 80f;

    private bool firstPersonActive;

    private float yaw;
    private float pitch;

    private void Update()
    {
        if (Input.GetKeyDown(cameraToggleKey))
        {
            ToggleCamera();
        }
    }

    private void ToggleCamera()
    {
        firstPersonActive = !firstPersonActive;

        if (firstPersonActive)
        {
            yaw = playerCamera.transform.eulerAngles.y;

            pitch = playerCamera.transform.eulerAngles.x;

            if (pitch > 180f)
                pitch -= 360f;

            thirdPersonCamera.enabled = false;
        }
        else
        {
            thirdPersonCamera.enabled = true;
        }
    }

    private void LateUpdate()
    {
        if (!firstPersonActive)
            return;

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        yaw += mouseX * firstPersonSensitivity;
        pitch -= mouseY * firstPersonSensitivity;

        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

        playerCamera.transform.position = firstPersonAnchor.position;

        playerCamera.transform.rotation = Quaternion.Euler(
            pitch,
            yaw,
            0f
        );
    }
}