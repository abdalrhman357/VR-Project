using UnityEngine;

namespace SwingingPaint.SceneBuild
{
    /// <summary>
    /// One camera, several modes (Part 6 "multiple cameras / switch between them"):
    ///   • Orbit   — drag to orbit the target, scroll to zoom (default, best for watching the swing)
    ///   • Top     — looks straight down at the canvas (best for seeing the painting form)
    ///   • Free    — WASD/QE fly + right-drag look (free inspection)
    ///   • Close   — tight orbit near the bucket outlet
    /// Switch with keys 1–4, or cycle with Tab. Self-contained; the scene builder just sets the target.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public enum Mode { Orbit, Top, Free, CloseUp }

        public Transform target;          // what Orbit/Top/Close look at
        public Mode mode = Mode.Orbit;

        [Header("Orbit")]
        public float orbitDistance = 6f;
        public float orbitYaw = 35f;
        public float orbitPitch = 25f;
        public float orbitSensitivity = 4f;
        public float zoomSensitivity = 4f;
        public Vector2 distanceClamp = new Vector2(1.5f, 20f);

        [Header("Free fly")]
        public float flySpeed = 4f;
        public float lookSensitivity = 3f;

        Camera _cam;
        float _freeYaw, _freePitch;
        Vector3 _targetPos => target != null ? target.position : Vector3.zero;

        void Awake() => _cam = GetComponent<Camera>();

        void Update()
        {
            HandleSwitching();
            switch (mode)
            {
                case Mode.Orbit:   UpdateOrbit(orbitDistance); break;
                case Mode.CloseUp: UpdateOrbit(Mathf.Min(orbitDistance, 2.5f)); break;
                case Mode.Top:     UpdateTop(); break;
                case Mode.Free:    UpdateFree(); break;
            }
        }

        void HandleSwitching()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) mode = Mode.Orbit;
            if (Input.GetKeyDown(KeyCode.Alpha2)) mode = Mode.Top;
            if (Input.GetKeyDown(KeyCode.Alpha3)) mode = Mode.Free;
            if (Input.GetKeyDown(KeyCode.Alpha4)) mode = Mode.CloseUp;
            if (Input.GetKeyDown(KeyCode.Tab)) mode = (Mode)(((int)mode + 1) % 4);
        }

        void UpdateOrbit(float distance)
        {
            if (Input.GetMouseButton(0))
            {
                orbitYaw += Input.GetAxis("Mouse X") * orbitSensitivity;
                orbitPitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
                orbitPitch = Mathf.Clamp(orbitPitch, -10f, 85f);
            }
            orbitDistance = Mathf.Clamp(orbitDistance - Input.mouseScrollDelta.y * zoomSensitivity * 0.2f,
                                        distanceClamp.x, distanceClamp.y);
            if (mode == Mode.CloseUp) distance = Mathf.Min(orbitDistance, 2.5f);

            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 pos = _targetPos + rot * (Vector3.back * distance);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(_targetPos - pos, Vector3.up);
        }

        void UpdateTop()
        {
            Vector3 pos = _targetPos + Vector3.up * Mathf.Max(orbitDistance, 3f);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        void UpdateFree()
        {
            if (Input.GetMouseButton(1))
            {
                _freeYaw += Input.GetAxis("Mouse X") * lookSensitivity;
                _freePitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
                _freePitch = Mathf.Clamp(_freePitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);
            }
            float boost = Input.GetKey(KeyCode.LeftShift) ? 3f : 1f;
            Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            if (Input.GetKey(KeyCode.E)) move.y += 1f;
            if (Input.GetKey(KeyCode.Q)) move.y -= 1f;
            transform.position += transform.TransformDirection(move) * flySpeed * boost * Time.deltaTime;
        }

        /// <summary>Initialize free-fly yaw/pitch from the current orbit framing so switching is smooth.</summary>
        public void SyncFreeFromCurrent()
        {
            Vector3 e = transform.rotation.eulerAngles;
            _freePitch = e.x > 180f ? e.x - 360f : e.x;
            _freeYaw = e.y;
        }
    }
}
