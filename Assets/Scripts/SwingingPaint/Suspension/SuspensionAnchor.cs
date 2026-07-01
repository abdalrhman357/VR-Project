using UnityEngine;

namespace SwingingPaint.Suspension
{
    /// <summary>
    /// A single attachment point for the rope. It is purely kinematic (a moving
    /// boundary condition for the XPBD solver) — no Unity physics. Changing the
    /// anchor completely changes the pendulum, because the rope head is pinned to
    /// <see cref="GetWorldPosition"/> every sub-step and the whole rope+bucket
    /// system hangs from it.
    /// </summary>
    [System.Serializable]
    public class SuspensionAnchor
    {
        public enum AnchorKind
        {
            Arbitrary,   // an explicit world position
            Ceiling,     // slides on a horizontal plane at 'height'
            Wall         // slides on a vertical plane at 'wallDistance'
        }

        public AnchorKind kind = AnchorKind.Ceiling;

        [Tooltip("Explicit world position (Arbitrary) or the base of the ceiling/wall reference.")]
        public Vector3 basePosition = new Vector3(0f, 3f, 0f);

        [Tooltip("Optional transform the anchor is parented to (moving ceiling/crane). If set, basePosition/offset are relative to it.")]
        public Transform reference;

        [Tooltip("Extra offset applied after everything else — the 'offset attachment' requirement.")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("Ceiling height (Ceiling kind).")]
        public float ceilingHeight = 3f;

        [Tooltip("Horizontal position on the ceiling plane (Ceiling kind): (x, z).")]
        public Vector2 ceilingXZ = Vector2.zero;

        [Tooltip("Distance from origin along the wall normal (Wall kind).")]
        public float wallDistance = 2f;

        [Tooltip("Height & lateral position on the wall (Wall kind): (lateral, height).")]
        public Vector2 wallLateralHeight = new Vector2(0f, 2.5f);

        [Tooltip("Wall normal direction (Wall kind). Normalised at use.")]
        public Vector3 wallNormal = Vector3.forward;

        /// <summary>
        /// Resolve the anchor to a world position for the current frame. This is
        /// what the solver pins the rope head to.
        /// </summary>
        public Vector3 GetWorldPosition()
        {
            Vector3 local;
            switch (kind)
            {
                case AnchorKind.Ceiling:
                    local = new Vector3(ceilingXZ.x, ceilingHeight, ceilingXZ.y);
                    break;

                case AnchorKind.Wall:
                {
                    Vector3 n = wallNormal.sqrMagnitude > 1e-6f ? wallNormal.normalized : Vector3.forward;
                    // Build an orthonormal frame on the wall: lateral ⟂ normal, up = world up projected.
                    Vector3 up = Vector3.up;
                    Vector3 lateral = Vector3.Cross(up, n);
                    if (lateral.sqrMagnitude < 1e-6f) lateral = Vector3.right;
                    lateral.Normalize();
                    local = n * wallDistance
                          + lateral * wallLateralHeight.x
                          + up * wallLateralHeight.y;
                    break;
                }

                default: // Arbitrary
                    local = basePosition;
                    break;
            }

            local += offset;
            return reference != null ? reference.TransformPoint(local) : local;
        }

        public void DrawGizmo()
        {
            Vector3 p = GetWorldPosition();
            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            Gizmos.DrawSphere(p, 0.04f);
            Gizmos.DrawLine(p - Vector3.up * 0.12f, p + Vector3.up * 0.12f);
            Gizmos.DrawLine(p - Vector3.right * 0.12f, p + Vector3.right * 0.12f);
        }
    }
}
