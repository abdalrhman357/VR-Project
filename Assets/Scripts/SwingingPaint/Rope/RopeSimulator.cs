using UnityEngine;
using SwingingPaint.Core;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// A rope modelled as a chain of point masses joined by XPBD constraints.
    /// Pure C# (no MonoBehaviour, no Unity physics) so it can be unit-tested and
    /// driven by an external fixed-step loop.
    ///
    /// The class is written in *phases* (Predict → Solve* → FinalizeVelocities)
    /// instead of a single Step(), so the owning system can interleave the
    /// rope's constraints with the bucket-attachment constraint inside one shared
    /// XPBD sub-step. Solving all constraints together (rather than rope-then-
    /// bucket) is what keeps a stiff cable stable while it drags a heavy bucket.
    ///
    /// Constraints implemented:
    ///   • Stretch  — distance constraint per segment, compliance = L₀/(EA).
    ///   • Bending  — angle/curvature constraint per interior node.
    ///   • Air drag — quadratic aerodynamic force in the predict step.
    ///   • Damping  — internal (per-segment relative velocity) + global.
    ///   • Breaking — a segment is cut when its tension exceeds T_max.
    /// </summary>
    public class RopeSimulator
    {
        // ── Node state (structure-of-arrays for cache locality) ──────────────
        public Vector3[] pos;        // current predicted position
        public Vector3[] prevPos;    // position at start of the sub-step
        public Vector3[] vel;        // velocity
        public float[]   invMass;    // 1/m, 0 for pinned nodes
        public bool[]    pinned;
        public bool[]    segmentBroken;   // per segment i (between node i and i+1)

        public int NodeCount { get; private set; }
        public int SegmentCount => NodeCount - 1;
        public float RestSegmentLength { get; private set; }
        public RopeMaterial Material { get; private set; }

        // Cached compliances (recomputed when material/length changes)
        float _stretchCompliance;
        float _bendCompliance;
        float _segmentMass;

        public int TailIndex => NodeCount - 1;
        public Vector3 TailPosition => pos[TailIndex];
        public Vector3 TailVelocity => vel[TailIndex];
        public Vector3 HeadPosition => pos[0];

        /// <summary>Reported tension magnitude per segment [N] (for stats/UI/breaking).</summary>
        public float[] segmentTension;

        /// <summary>
        /// Build a straight rope from <paramref name="head"/> (top, pinned) to
        /// <paramref name="tail"/> (bottom) using <paramref name="nodeCount"/>
        /// point masses.
        /// </summary>
        public RopeSimulator(Vector3 head, Vector3 tail, int nodeCount, RopeMaterial material)
        {
            NodeCount = Mathf.Max(2, nodeCount);
            Material  = material;

            pos           = new Vector3[NodeCount];
            prevPos       = new Vector3[NodeCount];
            vel           = new Vector3[NodeCount];
            invMass       = new float[NodeCount];
            pinned        = new bool[NodeCount];
            segmentBroken = new bool[NodeCount - 1];
            segmentTension= new float[NodeCount - 1];

            float totalLen    = Vector3.Distance(head, tail);
            RestSegmentLength = Mathf.Max(1e-4f, totalLen / SegmentCount);

            for (int i = 0; i < NodeCount; i++)
            {
                float t = i / (float)(NodeCount - 1);
                pos[i] = prevPos[i] = Vector3.Lerp(head, tail, t);
                vel[i] = Vector3.zero;
            }

            RecomputeMassAndCompliance();

            // Head is pinned to the anchor by default.
            SetPinned(0, true);
        }

        /// <summary>Recompute per-segment mass and XPBD compliances from the material.</summary>
        public void RecomputeMassAndCompliance()
        {
            _segmentMass       = Mathf.Max(1e-5f, Material.SegmentMass(RestSegmentLength));
            _stretchCompliance = Material.StretchCompliance(RestSegmentLength);
            _bendCompliance    = Material.BendingCompliance;

            // A node's mass is the average of its adjacent half-segments → each
            // interior node carries one full segment mass, ends carry half.
            for (int i = 0; i < NodeCount; i++)
            {
                if (pinned[i]) { invMass[i] = 0f; continue; }
                float share = (i == 0 || i == NodeCount - 1) ? 0.5f : 1.0f;
                float m = _segmentMass * share;
                invMass[i] = m > 1e-8f ? 1f / m : 0f;
            }
        }

        /// <summary>
        /// Force the total rest length (e.g. the user typed an exact rope length,
        /// or is changing it live from the GUI). Splits it evenly across segments
        /// and recomputes segment mass + XPBD compliance so the material response
        /// stays physically consistent.
        /// </summary>
        public void RestSegmentLengthOverride(float totalRestLength)
        {
            RestSegmentLength = Mathf.Max(1e-4f, totalRestLength / SegmentCount);
            RecomputeMassAndCompliance();
        }

        public void SetPinned(int index, bool isPinned)
        {
            pinned[index] = isPinned;
            if (isPinned) invMass[index] = 0f;
            else RecomputeMassAndCompliance();
        }

        /// <summary>Force a pinned node to a world position (e.g. the ceiling anchor).</summary>
        public void DriveNode(int index, Vector3 worldPos)
        {
            pos[index] = prevPos[index] = worldPos;
            vel[index] = Vector3.zero;
        }

        // ── XPBD phase 1: predict ────────────────────────────────────────────
        /// <summary>
        /// Save history and advance free nodes under gravity + quadratic air drag.
        /// Air drag per node:  F_drag = −½·ρ_air·C_d·A_ref·|v|·v   (opposes motion).
        /// A_ref ≈ diameter·restLength (side-on projected area of the segment).
        /// </summary>
        public void Predict(float h, Vector3 gravity, float airDensity)
        {
            float aRef = Material.diameter * RestSegmentLength;
            float dragK = 0.5f * airDensity * Material.airDragCoefficient * aRef;

            for (int i = 0; i < NodeCount; i++)
            {
                prevPos[i] = pos[i];
                if (invMass[i] == 0f) continue;      // pinned

                Vector3 a = gravity;

                // Quadratic aerodynamic drag as an acceleration (F/m).
                float speed = vel[i].magnitude;
                if (speed > 1e-5f)
                    a += (-dragK * speed * invMass[i]) * vel[i];

                vel[i] += a * h;
                pos[i] += vel[i] * h;
            }
        }

        // ── XPBD phase 2a: stretch constraints ───────────────────────────────
        /// <summary>
        /// Distance constraint per segment with XPBD compliance.
        ///   C   = |p_a − p_b| − L₀
        ///   α̃  = α / h²
        ///   Δλ = (−C − α̃·λ) / (w_a + w_b + α̃)
        ///   Δp = ±Δλ·w·n
        /// The accumulated λ is proportional to the constraint force, so the
        /// segment tension is T = |λ| / h² — used both for the stats readout and
        /// for the breaking test.
        /// </summary>
        public void SolveStretch(float h, float[] lambda)
        {
            float invH2 = 1f / (h * h);
            float aTilde = _stretchCompliance * invH2;

            for (int s = 0; s < SegmentCount; s++)
            {
                if (segmentBroken[s]) continue;

                int a = s, b = s + 1;
                float wa = invMass[a], wb = invMass[b];
                float wSum = wa + wb;
                if (wSum <= 0f) continue;

                Vector3 d = pos[a] - pos[b];
                float len = d.magnitude;
                if (len < 1e-9f) continue;
                Vector3 n = d / len;

                float C = len - RestSegmentLength;
                float dLambda = (-C - aTilde * lambda[s]) / (wSum + aTilde);
                lambda[s] += dLambda;

                Vector3 corr = dLambda * n;
                pos[a] += wa * corr;
                pos[b] -= wb * corr;

                // Tension magnitude for stats / breaking (|λ|/h² has units of N).
                float tension = Mathf.Abs(lambda[s]) * invH2;
                segmentTension[s] = tension;

                if (Material.canBreak && tension > Material.MaxTension)
                    segmentBroken[s] = true;
            }
        }

        // ── XPBD phase 2b: bending constraints ───────────────────────────────
        /// <summary>
        /// Bending resistance as a "straightening" constraint on each interior
        /// triple (i-1, i, i+1): we drive the midpoint toward the average of its
        /// neighbours. C = |p_i − ½(p_{i-1}+p_{i+1})|. With compliance this gives
        /// a controllable stiffness from floppy chain (huge compliance) to a
        /// near-rigid bar. Broken segments disable the joint that spans them.
        /// </summary>
        public void SolveBending(float h)
        {
            if (Material.bendingStiffness <= 0f) return;

            float aTilde = _bendCompliance / (h * h);

            for (int i = 1; i < NodeCount - 1; i++)
            {
                if (segmentBroken[i - 1] || segmentBroken[i]) continue;

                int l = i - 1, r = i + 1;
                float wl = invMass[l], wm = invMass[i], wr = invMass[r];

                Vector3 mid = 0.5f * (pos[l] + pos[r]);
                Vector3 d = pos[i] - mid;
                float len = d.magnitude;
                if (len < 1e-7f) continue;
                Vector3 n = d / len;

                // Gradients: ∂C/∂p_i = n, ∂C/∂p_l = −½n, ∂C/∂p_r = −½n
                float wSum = wm + 0.25f * wl + 0.25f * wr;
                if (wSum <= 0f) continue;

                float C = len;
                float dLambda = -C / (wSum + aTilde);

                pos[i] += wm * dLambda * n;
                pos[l] -= 0.5f * wl * dLambda * n;
                pos[r] -= 0.5f * wr * dLambda * n;
            }
        }

        // ── XPBD phase 3: derive velocities + damping ────────────────────────
        /// <summary>
        /// v = (x − x_prev)/h, then apply internal (relative-velocity) damping and
        /// a global oscillation damping. Internal damping removes energy from the
        /// *stretching* mode only (like a rod's material hysteresis), while the
        /// global term models overall air/oscillation losses.
        /// </summary>
        public void FinalizeVelocities(float h)
        {
            float invH = 1f / h;
            for (int i = 0; i < NodeCount; i++)
            {
                if (invMass[i] == 0f) { vel[i] = Vector3.zero; continue; }
                vel[i] = (pos[i] - prevPos[i]) * invH;
            }

            // Internal / structural damping along each segment (Rayleigh-style).
            float dInt = Mathf.Clamp01(Material.internalDamping);
            if (dInt > 0f)
            {
                for (int s = 0; s < SegmentCount; s++)
                {
                    if (segmentBroken[s]) continue;
                    int a = s, b = s + 1;
                    if (invMass[a] == 0f && invMass[b] == 0f) continue;

                    Vector3 dir = pos[b] - pos[a];
                    float len = dir.magnitude;
                    if (len < 1e-7f) continue;
                    dir /= len;

                    // Relative velocity along the segment axis.
                    float vRel = Vector3.Dot(vel[b] - vel[a], dir);
                    Vector3 impulse = dInt * vRel * dir;

                    float wSum = invMass[a] + invMass[b];
                    if (wSum <= 0f) continue;
                    vel[a] += (invMass[a] / wSum) * impulse;
                    vel[b] -= (invMass[b] / wSum) * impulse;
                }
            }

            // Global oscillation damping (exponential, frame-rate independent).
            float g = NumericalIntegrator.DampingFactor(Material.oscillationDamping, h);
            for (int i = 0; i < NodeCount; i++)
                if (invMass[i] != 0f) vel[i] *= g;
        }

        /// <summary>Total rope length right now (for stats / stretch readout).</summary>
        public float CurrentLength()
        {
            float total = 0f;
            for (int s = 0; s < SegmentCount; s++)
                total += Vector3.Distance(pos[s], pos[s + 1]);
            return total;
        }

        public float RestLength => RestSegmentLength * SegmentCount;

        /// <summary>Highest tension currently in any segment [N].</summary>
        public float MaxTensionNow()
        {
            float m = 0f;
            for (int s = 0; s < SegmentCount; s++)
                if (!segmentBroken[s] && segmentTension[s] > m) m = segmentTension[s];
            return m;
        }

        public bool IsBroken()
        {
            for (int s = 0; s < SegmentCount; s++)
                if (segmentBroken[s]) return true;
            return false;
        }
    }
}
