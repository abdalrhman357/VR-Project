using UnityEngine;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Simple orbit camera. Right mouse drag = orbit, scroll = zoom, middle drag =
    /// pan. Left mouse is intentionally left free for grabbing the bucket.
    /// </summary>
    public class DemoOrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(0f, 1.2f, 0f);
        public float distance = 5f;
        public float minDistance = 1.5f;
        public float maxDistance = 15f;
        public float yaw = 30f;
        public float pitch = 15f;
        public float orbitSpeed = 4f;
        public float zoomSpeed = 4f;
        public float panSpeed = 0.01f;

        void LateUpdate()
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSpeed;
                pitch -= Input.GetAxis("Mouse Y") * orbitSpeed;
                pitch = Mathf.Clamp(pitch, -85f, 85f);
            }

            if (Input.GetMouseButton(2))
            {
                Vector3 right = transform.right;
                Vector3 up = transform.up;
                target -= (right * Input.GetAxis("Mouse X") + up * Input.GetAxis("Mouse Y"))
                          * panSpeed * distance;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
