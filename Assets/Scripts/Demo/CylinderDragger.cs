using UnityEngine;

namespace Seb.Fluid.Demo
{
    /// <summary>
    /// Drag the simulation cylinder with the RIGHT mouse button.
    ///
    /// Controls:
    ///   Hold RMB + move mouse horizontally → move cylinder in XZ plane
    ///   Hold RMB + move mouse vertically   → move cylinder up / down (Y axis)
    ///   Release RMB                        → stop dragging
    /// </summary>
    public class CylinderDragger : MonoBehaviour
    {
        [Header("Movement Speed")]
        [Tooltip("Horizontal drag sensitivity.")]
        [Range(0.5f, 30f)]
        public float horizontalSpeed = 5f;

        [Tooltip("Vertical drag sensitivity.")]
        [Range(0.5f, 30f)]
        public float verticalSpeed = 5f;

        [Tooltip("How fast the cylinder smoothly follows the target position.")]
        [Range(0.5f, 30f)]
        public float followSpeed = 8f;

        [Header("References")]
        [Tooltip("Leave empty — auto-found at Start.")]
        public OrbitCam orbitCam;

        [Tooltip("Leave empty — auto-found at Start.")]
        public PendulumSimulationManager manager;

        // ── private state ────────────────────────────────────────────────
        Camera  mainCam;
        bool    isDragging;
        Vector2 lastMousePos;

        // ── Unity ────────────────────────────────────────────────────────
        void Start()
        {
            mainCam = Camera.main;

            if (orbitCam == null && mainCam != null)
                orbitCam = mainCam.GetComponent<OrbitCam>();

            if (manager == null)
                manager = FindObjectOfType<PendulumSimulationManager>();
        }

        void Update()
        {
            // Right mouse button pressed → start drag
            if (Input.GetMouseButtonDown(1))
                BeginDrag();

            // Right mouse button released → end drag
            if (Input.GetMouseButtonUp(1))
                EndDrag();

            // Drag in progress
            if (isDragging)
                TickDrag();
        }

        // ── helpers ──────────────────────────────────────────────────────

        void BeginDrag()
        {
            isDragging   = true;
            lastMousePos = Input.mousePosition;

            // Disable orbit so RMB doesn't also rotate the camera
            if (orbitCam != null) orbitCam.enabled = false;

            if (manager != null) manager.BeginDrag();
        }

        void EndDrag()
        {
            isDragging = false;
            if (orbitCam != null) orbitCam.enabled = true;

            if (manager != null) manager.EndDrag();
        }

        void TickDrag()
        {
            Vector2 currentMousePos = Input.mousePosition;
            Vector2 delta           = currentMousePos - lastMousePos;
            lastMousePos            = currentMousePos;

            // Normalise by screen size so speed is resolution-independent
            float dx = delta.x / Screen.width;
            float dy = delta.y / Screen.height;

            // Horizontal mouse movement → XZ plane (relative to camera orientation)
            Vector3 camRight   = mainCam.transform.right;
            camRight.y         = 0f;
            if (camRight.sqrMagnitude > 0.001f) camRight.Normalize();

            Vector3 camForward = mainCam.transform.forward;
            camForward.y       = 0f;
            if (camForward.sqrMagnitude > 0.001f) camForward.Normalize();

            // نضاعف السرعة داخلياً بشكل كبير جداً (× 8) لضمان قدرة المستخدم على سحب البندول 
            // لزوايا ضخمة (مثل 90 درجة) بمسحة ماوس بسيطة، متجاوزين أي قيم قديمة في الـ Inspector.
            Vector3 displacement = camRight * dx * (horizontalSpeed * 8f) + Vector3.up * dy * (verticalSpeed * 8f);

            if (manager != null)
            {
                manager.UpdateDragPosition(displacement);
            }
        }
    }
}
