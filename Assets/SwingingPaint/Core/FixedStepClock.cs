using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Deterministic fixed-timestep accumulator. Decouples the variable frame rate from the
    /// physics step so the Verlet rope and bucket integrate identically on every machine
    /// (fixes the frame-rate-dependent integration noted in the Rope analysis).
    ///
    /// Usage:
    ///   clock.Accumulate(Time.deltaTime * speed);
    ///   while (clock.TryConsumeStep(out float dt)) { Step(dt); }
    /// </summary>
    public sealed class FixedStepClock
    {
        public float FixedStep { get; private set; }
        public int MaxSubSteps { get; private set; }

        float _accumulator;
        public int StepsLastFrame { get; private set; }

        /// <summary>Interpolation factor [0,1) for rendering between the last two steps.</summary>
        public float Alpha => FixedStep > 0f ? Mathf.Clamp01(_accumulator / FixedStep) : 0f;

        public FixedStepClock(float fixedStep, int maxSubSteps)
        {
            Configure(fixedStep, maxSubSteps);
        }

        public void Configure(float fixedStep, int maxSubSteps)
        {
            FixedStep = Mathf.Max(1e-4f, fixedStep);
            MaxSubSteps = Mathf.Max(1, maxSubSteps);
        }

        public void Accumulate(float scaledDeltaTime)
        {
            _accumulator += Mathf.Max(0f, scaledDeltaTime);
            // Clamp the backlog so a hitch (or a breakpoint) can't trigger a death-spiral of steps.
            float maxBacklog = FixedStep * MaxSubSteps;
            if (_accumulator > maxBacklog) _accumulator = maxBacklog;
            StepsLastFrame = 0;
        }

        public bool TryConsumeStep(out float dt)
        {
            if (_accumulator >= FixedStep && StepsLastFrame < MaxSubSteps)
            {
                _accumulator -= FixedStep;
                StepsLastFrame++;
                dt = FixedStep;
                return true;
            }
            dt = 0f;
            return false;
        }

        public void Reset() { _accumulator = 0f; StepsLastFrame = 0; }
    }
}
