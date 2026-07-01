using UnityEngine;

namespace SwingingPaint.Tools
{
    /// <summary>
    /// Moves the fluid-sandbox container so you can slosh the liquid and judge the SPH behaviour.
    /// Keyboard: WASD / arrows = horizontal, Q/E = down/up, Shift = faster. Hold RIGHT mouse and drag to
    /// move it in the camera plane (left-drag is the camera orbit). The container is the FluidSim's bounds,
    /// so moving it drags the walls through the liquid (real sloshing). Press X for a quick shake impulse.
    /// </summary>
    public sealed class BoxDragController : MonoBehaviour
    {
        public Transform target;
        public Camera cam;
        public float keySpeed = 6f;
        public float mouseSpeed = 0.02f;

        Vector3 _shakeVel;

        void Update()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            float boost = Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1f;

            // Keyboard move (world axes).
            Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (Input.GetKey(KeyCode.E)) move.y += 1f;
            if (Input.GetKey(KeyCode.Q)) move.y -= 1f;
            target.position += move * keySpeed * boost * dt;

            // Mouse drag in the camera plane (RIGHT button; left is the camera orbit).
            if (cam != null && Input.GetMouseButton(1))
            {
                float mx = Input.GetAxis("Mouse X");
                float my = Input.GetAxis("Mouse Y");
                target.position += (cam.transform.right * mx + cam.transform.up * my) * (mouseSpeed * keySpeed);
            }

            // Quick shake impulse.
            if (Input.GetKeyDown(KeyCode.X))
                _shakeVel = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 18f;
            if (_shakeVel.sqrMagnitude > 0.001f)
            {
                target.position += _shakeVel * dt;
                _shakeVel = Vector3.Lerp(_shakeVel, Vector3.zero, dt * 12f);
            }
        }
    }
}
