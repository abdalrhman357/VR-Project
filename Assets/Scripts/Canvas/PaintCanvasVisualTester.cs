using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pendulum Paint Dropper — true 2D pendulum swing in both X and Z axes.
/// Independent periods create Lissajous / spiral / figure-8 patterns on the canvas.
///
/// Setup:
///   1. Add to an empty GameObject.
///   2. Assign CanvasToTest and ArmPivot (empty GO above the canvas).
///   3. Play — [P] paint on/off, [Space] clear, [R] reset swing, [1-4] colours.
/// </summary>
public class PendulumPaintDropper : MonoBehaviour
{
    [Header("References")]
    public PaintCanvas CanvasToTest;
    public Transform   ArmPivot;

    [Header("Pendulum — arm")]
    [Range(0.5f, 10f)]
    public float ArmLength = 3f;

    [Header("Pendulum — X axis (left / right)")]
    [Range(0f, 80f)]  public float SwingAmplitudeX = 40f;
    [Range(0.3f, 12f)] public float SwingPeriodX   = 2.5f;
    [Range(0f, 0.5f)]  public float DampingX        = 0.04f;

    [Header("Pendulum — Z axis (forward / back)")]
    [Range(0f, 80f)]  public float SwingAmplitudeZ = 40f;
    [Range(0.3f, 12f)] public float SwingPeriodZ   = 3.7f;   // ← different from X = pattern!
    [Range(0f, 0.5f)]  public float DampingZ        = 0.04f;

    [Header("Phase offset")]
    [Range(0f, 360f)]
    [Tooltip("90° = circle/ellipse start. 0° = diagonal line start. Try different values.")]
    public float PhaseOffsetDeg = 90f;

    [Header("Drop rate")]
    [Range(0.5f, 60f)] public float DropsPerSecond = 20f;

    [Header("Paint")]
    public Color PaintColor  = Color.blue;
    [Range(0.1f, 10f)]  public float Viscosity   = 1.5f;
    [Range(0.001f, 0.05f)] public float DropMass  = 0.008f;
    public bool RandomColors = false;

    [Header("Physics")]
    [Range(1f, 30f)]  public float Gravity          = 9.8f;
    [Range(0f, 0.5f)] public float HorizontalSpread = 0.02f;

    [Header("Visuals")]
    public bool  ShowDropVisualsInGameView = true;
    [Range(0.005f, 0.08f)] public float DropVisualSize = 0.016f;

    [Header("Control")]
    public bool IsPainting = false;

    // ── private ──────────────────────────────────────────────────

    private class FallingDrop
    {
        public Vector3    Position, Velocity, Color;
        public Color      Col;
        public GameObject Visual;
    }

    private readonly List<FallingDrop> _drops    = new List<FallingDrop>();
    private float _spawnTimer;
    private float _timeX, _timeZ;          // independent clocks
    private float _envX = 1f, _envZ = 1f; // damping envelopes
    private Vector3 _bucketPos, _bucketVel;
    private Material _sharedMat;

    // ── lifecycle ────────────────────────────────────────────────

    void Awake()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                 ?? Shader.Find("Standard");
        _sharedMat = new Material(sh);
    }

    void Update()
    {
        HandleInput();
        UpdatePendulum();
        SpawnDrops();
        SimulateAndPaint();
    }

    // ── input ────────────────────────────────────────────────────

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.P))     IsPainting = !IsPainting;
        if (Input.GetKeyDown(KeyCode.Space) && CanvasToTest != null) CanvasToTest.Clear();
        if (Input.GetKeyDown(KeyCode.R))     ResetSwing();
        if (Input.GetKeyDown(KeyCode.Alpha1)) PaintColor = Color.red;
        if (Input.GetKeyDown(KeyCode.Alpha2)) PaintColor = Color.blue;
        if (Input.GetKeyDown(KeyCode.Alpha3)) PaintColor = Color.yellow;
        if (Input.GetKeyDown(KeyCode.Alpha4)) PaintColor = Color.green;
    }

    public void ResetSwing()
    {
        _timeX = 0f; _timeZ = 0f;
        _envX  = 1f; _envZ  = 1f;
    }

    // ── pendulum — the core fix ───────────────────────────────────

    void UpdatePendulum()
    {
        if (ArmPivot == null) return;

        float dt = Time.deltaTime;

        // Advance independent clocks
        _timeX += dt;
        _timeZ += dt;

        // Decay envelopes
        _envX *= Mathf.Exp(-DampingX * dt);
        _envZ *= Mathf.Exp(-DampingZ * dt);

        float omegaX = (2f * Mathf.PI) / Mathf.Max(SwingPeriodX, 0.01f);
        float omegaZ = (2f * Mathf.PI) / Mathf.Max(SwingPeriodZ, 0.01f);
        float phase  = PhaseOffsetDeg * Mathf.Deg2Rad;

        // Angle in each axis (degrees → radians)
        float angleX = SwingAmplitudeX * _envX * Mathf.Sin(omegaX * _timeX)           * Mathf.Deg2Rad;
        float angleZ = SwingAmplitudeZ * _envZ * Mathf.Sin(omegaZ * _timeZ + phase)    * Mathf.Deg2Rad;

        // Angular velocities (for seeding drop lateral speed)
        float adotX  = SwingAmplitudeX * _envX * omegaX * Mathf.Cos(omegaX * _timeX)           * Mathf.Deg2Rad;
        float adotZ  = SwingAmplitudeZ * _envZ * omegaZ * Mathf.Cos(omegaZ * _timeZ + phase)    * Mathf.Deg2Rad;

        // Bucket tip position in world space
        // angleX tilts the arm left/right  (motion in World X)
        // angleZ tilts the arm forward/back (motion in World Z)
        // Both are small-angle so we can combine them additively without gimbal issues
        Vector3 localTip = new Vector3(
             Mathf.Sin(angleX) * ArmLength,              // X displacement
            -Mathf.Cos(angleX) * Mathf.Cos(angleZ) * ArmLength,  // Y (always mostly down)
             Mathf.Sin(angleZ) * ArmLength               // Z displacement
        );

        Vector3 prevPos = _bucketPos;
        _bucketPos = ArmPivot.position + localTip;

        // Velocity derived from angular velocities (tangential speed = omega × arm)
        _bucketVel = new Vector3(
            adotX * ArmLength,   // lateral X velocity
            0f,
            adotZ * ArmLength    // lateral Z velocity
        );

        // Smooth it with finite-difference as a sanity check
        if (dt > 0f)
            _bucketVel = Vector3.Lerp(_bucketVel, (_bucketPos - prevPos) / dt, 0.5f);
    }

    // ── spawning ─────────────────────────────────────────────────

    void SpawnDrops()
    {
        if (!IsPainting || ArmPivot == null) return;

        _spawnTimer += Time.deltaTime;
        float interval = 1f / Mathf.Max(DropsPerSecond, 0.01f);

        while (_spawnTimer >= interval)
        {
            _spawnTimer -= interval;
            SpawnOneDrop();
        }
    }

    void SpawnOneDrop()
    {
        Color col = RandomColors
            ? Random.ColorHSV(0f, 1f, 0.75f, 1f, 0.75f, 1f)
            : PaintColor;

        // Seed lateral velocity from the bucket's current swing velocity
        Vector3 vel = _bucketVel;
        vel.x += Random.Range(-HorizontalSpread, HorizontalSpread);
        vel.z += Random.Range(-HorizontalSpread, HorizontalSpread);

        var drop = new FallingDrop { Position = _bucketPos, Velocity = vel, Col = col };
        if (ShowDropVisualsInGameView) drop.Visual = CreateDropSphere(_bucketPos, col);
        _drops.Add(drop);
    }

    // ── simulate ──────────────────────────────────────────────────

    void SimulateAndPaint()
    {
        if (CanvasToTest == null || _drops.Count == 0) return;

        float dt       = Time.deltaTime;
        var   toRemove = new List<FallingDrop>();

        foreach (var d in _drops)
        {
            d.Velocity += Vector3.down * Gravity * dt;
            d.Position += d.Velocity * dt;
            if (d.Visual != null) d.Visual.transform.position = d.Position;

            var fp = new FluidParticleData(d.Position, d.Velocity, d.Col, Viscosity, dt);
            fp.Mass = DropMass;

            bool hit = CanvasToTest.TryPaint(fp, dt);
            float limit = (ArmPivot != null ? ArmPivot.position.y : 0f) - ArmLength - 25f;
            if (hit || d.Position.y < limit) toRemove.Add(d);
        }

        foreach (var d in toRemove)
        {
            if (d.Visual != null) Destroy(d.Visual);
            _drops.Remove(d);
        }
    }

    // ── sphere visual ─────────────────────────────────────────────

    GameObject CreateDropSphere(Vector3 pos, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "PaintDrop";
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one * DropVisualSize;
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().material = new Material(_sharedMat) { color = col };
        return go;
    }

    // ── gizmos ────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (ArmPivot == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(ArmPivot.position, 0.05f);
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.9f);
        Gizmos.DrawSphere(_bucketPos, 0.06f);
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.7f);
        Gizmos.DrawLine(ArmPivot.position, _bucketPos);

        // Preview the Lissajous path in Scene view
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.2f);
        float omX = (2f * Mathf.PI) / Mathf.Max(SwingPeriodX, 0.01f);
        float omZ = (2f * Mathf.PI) / Mathf.Max(SwingPeriodZ, 0.01f);
        float ph  = PhaseOffsetDeg * Mathf.Deg2Rad;
        Vector3 prev = Vector3.zero;
        int segs = 120;
        float tMax = Mathf.Max(SwingPeriodX, SwingPeriodZ) * 4f;
        for (int i = 0; i <= segs; i++)
        {
            float t  = i / (float)segs * tMax;
            float ax = SwingAmplitudeX * Mathf.Sin(omX * t)      * Mathf.Deg2Rad;
            float az = SwingAmplitudeZ * Mathf.Sin(omZ * t + ph) * Mathf.Deg2Rad;
            Vector3 tip = ArmPivot.position + new Vector3(
                Mathf.Sin(ax) * ArmLength,
               -Mathf.Cos(ax) * Mathf.Cos(az) * ArmLength,
                Mathf.Sin(az) * ArmLength);
            if (i > 0) Gizmos.DrawLine(prev, tip);
            prev = tip;
        }
    }

    void OnDestroy()
    {
        foreach (var d in _drops)
            if (d.Visual != null) Destroy(d.Visual);
        _drops.Clear();
        if (_sharedMat != null) Destroy(_sharedMat);
    }
}