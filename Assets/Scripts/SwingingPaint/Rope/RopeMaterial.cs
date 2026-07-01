using UnityEngine;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// Physical description of a rope, expressed in real engineering units, plus
    /// the derived quantities the XPBD solver actually consumes.
    ///
    /// The point of keeping *real* units (Pa, kg/m³, m) is that the different
    /// rope types then differ only in their material constants — the solver code
    /// is identical for a steel cable and a rubber band. That is exactly the
    /// "variable / different materials" requirement.
    ///
    /// Rod mechanics used here (a rope segment = a short elastic rod):
    ///   • Cross-section area          A  = π·(d/2)²                     [m²]
    ///   • Linear mass density         μ  = ρ·A                          [kg/m]
    ///   • Axial (stretch) stiffness   k  = E·A / L₀                     [N/m]
    ///         (Hooke for a rod: F = (E·A/L₀)·ΔL)
    ///   • XPBD compliance             α  = 1/k = L₀/(E·A)               [m/N]
    ///   • Breaking tension            T_max = σ_break · A               [N]
    ///         (σ_break = ultimate tensile stress)
    /// </summary>
    [System.Serializable]
    public class RopeMaterial
    {
        [Header("Identity")]
        public RopeType type = RopeType.Nylon;

        [Header("Geometry")]
        [Tooltip("Rope diameter (thickness) in metres.")]
        public float diameter = 0.02f;

        [Header("Bulk material constants (real units)")]
        [Tooltip("Mass density ρ [kg/m³].")]
        public float density = 1150f;

        [Tooltip("Young's modulus E [Pa]. Governs how hard the rope resists stretching.")]
        public float youngsModulus = 3.0e9f;

        [Tooltip("Ultimate tensile stress σ_break [Pa]. Rope snaps above this.")]
        public float breakingStress = 7.5e7f;

        [Header("Behavioural (dimensionless 0..1 unless noted)")]
        [Tooltip("Resistance to bending. 0 = perfectly floppy chain, 1 = rigid bar.")]
        [Range(0f, 1f)] public float bendingStiffness = 0.15f;

        [Tooltip("Resistance to twisting about the rope's own axis.")]
        [Range(0f, 1f)] public float torsionStiffness = 0.1f;

        [Tooltip("Structural / internal-friction damping applied to stretch (energy lost per oscillation).")]
        [Range(0f, 1f)] public float internalDamping = 0.08f;

        [Tooltip("Whole-rope velocity damping [1/s] — models oscillation decay.")]
        public float oscillationDamping = 0.4f;

        [Tooltip("Aerodynamic drag coefficient of the rope (used in quadratic air drag).")]
        public float airDragCoefficient = 1.1f;

        [Header("Failure")]
        [Tooltip("If true the rope can snap once local tension exceeds T_max.")]
        public bool canBreak = false;

        [Tooltip("Safety multiplier on the computed breaking tension (design margin).")]
        public float breakingSafetyFactor = 1.0f;

        // ── Derived quantities (pure functions of the fields above) ──────────

        /// <summary>Cross-section area A = π·(d/2)² [m²].</summary>
        public float CrossSectionArea
        {
            get { float r = diameter * 0.5f; return Mathf.PI * r * r; }
        }

        /// <summary>Linear mass density μ = ρ·A [kg/m].</summary>
        public float LinearMassDensity => density * CrossSectionArea;

        /// <summary>Mass of one rope segment of rest length L₀ [kg].</summary>
        public float SegmentMass(float restLength) => LinearMassDensity * Mathf.Max(restLength, 1e-6f);

        /// <summary>Axial stiffness of one segment k = E·A/L₀ [N/m].</summary>
        public float AxialStiffness(float restLength)
            => youngsModulus * CrossSectionArea / Mathf.Max(restLength, 1e-6f);

        /// <summary>
        /// XPBD compliance of one stretch constraint, α = 1/k = L₀/(E·A) [m/N].
        /// Near-zero for steel (almost rigid) and large for rubber (very soft).
        /// </summary>
        public float StretchCompliance(float restLength)
            => Mathf.Max(restLength, 1e-6f) / (youngsModulus * CrossSectionArea);

        /// <summary>Breaking tension T_max = σ_break·A·safety [N].</summary>
        public float MaxTension => breakingStress * CrossSectionArea * Mathf.Max(0.01f, breakingSafetyFactor);

        /// <summary>
        /// Compliance for a bending constraint, mapped from the 0..1 knob to a
        /// physically-ordered range. Higher bendingStiffness → smaller compliance
        /// → stiffer. We clamp so 0 means "free hinge" (huge compliance) instead
        /// of an exact 0 that would over-constrain the solver.
        /// </summary>
        public float BendingCompliance
        {
            get
            {
                float s = Mathf.Clamp01(bendingStiffness);
                // s=0 → ~1e-1 (very soft), s=1 → ~1e-6 (near rigid); log-interpolated.
                return Mathf.Pow(10f, Mathf.Lerp(-1f, -6f, s));
            }
        }

        /// <summary>Deep copy — used so runtime tweaks never mutate a shared preset.</summary>
        public RopeMaterial Clone()
        {
            return (RopeMaterial)MemberwiseClone();
        }

        // ── Real-world presets ───────────────────────────────────────────────
        //  Values are order-of-magnitude realistic (handbook figures). They are
        //  intentionally readable so they can be defended in the viva.

        public static RopeMaterial CreatePreset(RopeType t)
        {
            switch (t)
            {
                case RopeType.Steel:
                    return new RopeMaterial {
                        type = t, diameter = 0.012f,
                        density = 7850f, youngsModulus = 2.0e11f, breakingStress = 1.5e9f,
                        bendingStiffness = 0.85f, torsionStiffness = 0.7f,
                        internalDamping = 0.02f, oscillationDamping = 0.15f,
                        airDragCoefficient = 0.9f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Plastic:
                    return new RopeMaterial {
                        type = t, diameter = 0.02f,
                        density = 950f, youngsModulus = 1.2e9f, breakingStress = 3.0e7f,
                        bendingStiffness = 0.3f, torsionStiffness = 0.2f,
                        internalDamping = 0.12f, oscillationDamping = 0.5f,
                        airDragCoefficient = 1.1f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Rubber:
                    return new RopeMaterial {
                        type = t, diameter = 0.02f,
                        density = 1100f, youngsModulus = 5.0e6f, breakingStress = 1.5e7f,
                        bendingStiffness = 0.05f, torsionStiffness = 0.05f,
                        internalDamping = 0.25f, oscillationDamping = 0.9f,
                        airDragCoefficient = 1.2f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Cotton:
                    return new RopeMaterial {
                        type = t, diameter = 0.018f,
                        density = 400f, youngsModulus = 8.0e8f, breakingStress = 4.0e8f,
                        bendingStiffness = 0.12f, torsionStiffness = 0.1f,
                        internalDamping = 0.18f, oscillationDamping = 0.7f,
                        airDragCoefficient = 1.3f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Nylon:
                    return new RopeMaterial {
                        type = t, diameter = 0.016f,
                        density = 1150f, youngsModulus = 3.0e9f, breakingStress = 7.5e7f,
                        bendingStiffness = 0.18f, torsionStiffness = 0.15f,
                        internalDamping = 0.1f, oscillationDamping = 0.45f,
                        airDragCoefficient = 1.1f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Hemp:
                    return new RopeMaterial {
                        type = t, diameter = 0.022f,
                        density = 860f, youngsModulus = 3.5e9f, breakingStress = 5.5e8f,
                        bendingStiffness = 0.35f, torsionStiffness = 0.25f,
                        internalDamping = 0.2f, oscillationDamping = 0.6f,
                        airDragCoefficient = 1.35f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Elastic:
                    return new RopeMaterial {
                        type = t, diameter = 0.01f,
                        density = 1050f, youngsModulus = 1.5e6f, breakingStress = 2.0e7f,
                        bendingStiffness = 0.02f, torsionStiffness = 0.02f,
                        internalDamping = 0.3f, oscillationDamping = 1.1f,
                        airDragCoefficient = 1.2f, canBreak = true, breakingSafetyFactor = 1.5f };

                case RopeType.Chain:
                    // A chain has negligible bending stiffness (links pivot freely)
                    // but a very stiff, near-inextensible axial response.
                    return new RopeMaterial {
                        type = t, diameter = 0.014f,
                        density = 7800f, youngsModulus = 1.8e11f, breakingStress = 8.0e8f,
                        bendingStiffness = 0.0f, torsionStiffness = 0.4f,
                        internalDamping = 0.05f, oscillationDamping = 0.2f,
                        airDragCoefficient = 1.0f, canBreak = true, breakingSafetyFactor = 1f };

                case RopeType.Custom:
                default:
                    return new RopeMaterial { type = RopeType.Custom };
            }
        }
    }
}
