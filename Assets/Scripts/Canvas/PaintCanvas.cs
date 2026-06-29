using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PaintCanvas : MonoBehaviour
{
    [Header("أبعاد اللوحة (Canvas Dimensions)")]
    public float CanvasWidth  = 2f;
    public float CanvasHeight = 2f;
    public int   TextureRes   = 1024;

    [Header("خصائص السطح (Surface Properties)")]
    [Range(0f, 1f)]
    public float AbsorptionRate = 0.85f;

    [Header("اتجاه اللوحة (Canvas Orientation)")]
    public Vector3 PaintNormalLocal = Vector3.up;
    public bool AutoCalculateTilt   = true;
    [Range(0f, 180f)]
    public float TiltAngle = 0f;

    [Header("البيئة (Environment)")]
    [Range(0f, 1f)]
    public float Humidity = 0.5f;

    [Header("الجسيمات (Particles)")]
    public float    ReferenceParticleMass = 0.012f;
    public Vector2  MassScaleClamp        = new Vector2(0.6f, 2.8f);

    [Header("كشف الاصطدام (Collision Detection)")]
    public float HitThreshold = 0.05f;

    [Header("قماش اللوحة والفرشاة (Canvas Grain & Brush)")]
    [Range(5f, 80f)]
    public float GrainScale = 25f;

    public ComputeShader PaintCompute;

    private float GrainStrength => Mathf.Clamp01(AbsorptionRate * 0.5f);
    private float WetSpreadRate => Mathf.Lerp(0.02f, 0.5f, Humidity);
    
    // Core RenderTextures replacing CPU arrays
    public RenderTexture PaintTex { get; private set; }
    private RenderTexture _wetTex; 
    private RenderTexture _wetTexPrev; 
    private RenderTexture _wetTimerTex;
    private RenderTexture _thicknessTex;
    private Texture2D _grainTex;
    
    private Vector2 _uvGravityDir = new Vector2(0, -1);
    private PendulumPaintDropper _tester;
    
    private float BrushSizeScale 
    {
        get 
        {
            if (_tester != null)
            {
                float armLength = _tester.ArmLength;
                float amplitude = Mathf.Max(_tester.SwingAmplitudeX, _tester.SwingAmplitudeZ);
                return Mathf.Clamp((armLength * 0.05f) + (amplitude * 0.005f), 0.05f, 2f);
            }
            return 0.35f;
        }
    }
    
    private Renderer _renderer;
    
    // GPU Buffers
    private ComputeBuffer _hitsBuffer;
    private ComputeBuffer _dispatchArgsBuffer;
    private ComputeBuffer _cellLocksBuffer;

    private int _initKernel;
    private int _detectHitsKernel;
    private int _applyPaintKernel;
    private int _updateWetnessKernel;
    private int _prepareArgsKernel;
    private int _clearLocksKernel;

    void Awake()
    {
        _tester = FindAnyObjectByType<PendulumPaintDropper>();
        _renderer = GetComponent<Renderer>();
        if (PaintCompute == null)
        {
            Debug.LogError("PaintCanvas requires a PaintCompute shader.");
            return;
        }

        _initKernel = PaintCompute.FindKernel("InitializeCanvas");
        _detectHitsKernel = PaintCompute.FindKernel("DetectHits");
        _applyPaintKernel = PaintCompute.FindKernel("ApplyPaint");
        _updateWetnessKernel = PaintCompute.FindKernel("UpdateWetness");
        _prepareArgsKernel = PaintCompute.FindKernel("PrepareDispatchArgs");
        _clearLocksKernel = PaintCompute.FindKernel("ClearCellLocks");

        InitializeTextures();
        InitializeBuffers();
        
        Clear(); // Initializes GPU textures to white/empty
    }

    void InitializeTextures()
    {
        PaintTex = CreateRenderTexture(RenderTextureFormat.ARGB32);
        _wetTex = CreateRenderTexture(RenderTextureFormat.ARGB32);
        _wetTexPrev = CreateRenderTexture(RenderTextureFormat.ARGB32);
        _wetTimerTex = CreateRenderTexture(RenderTextureFormat.RFloat);
        _thicknessTex = CreateRenderTexture(RenderTextureFormat.RFloat);
        
        _grainTex = new Texture2D(TextureRes, TextureRes, TextureFormat.RFloat, false, true); // true for linear
        BakeGrainMap();

        _renderer.material.mainTexture = PaintTex;
    }

    RenderTexture CreateRenderTexture(RenderTextureFormat format)
    {
        var rt = new RenderTexture(TextureRes, TextureRes, 0, format, RenderTextureReadWrite.Linear);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }

    void BakeGrainMap()
    {
        float seed = Random.Range(0f, 500f);
        Color[] pixels = new Color[TextureRes * TextureRes];
        
        for (int py = 0; py < TextureRes; py++)
        {
            for (int px = 0; px < TextureRes; px++)
            {
                float u = px * GrainScale / TextureRes + seed;
                float v = py * GrainScale / TextureRes + seed;
                float g = Mathf.PerlinNoise(u, v)
                        + 0.50f * Mathf.PerlinNoise(u * 2.1f, v * 2.1f)
                        + 0.25f * Mathf.PerlinNoise(u * 4.3f, v * 4.3f);
                float val = Mathf.Clamp01(g / 1.75f);
                pixels[py * TextureRes + px] = new Color(val, val, val, 1f);
            }
        }
        _grainTex.SetPixels(pixels);
        _grainTex.Apply();
    }

    void InitializeBuffers()
    {
        // Max 50k hits per frame. Adjust if needed.
        _hitsBuffer = new ComputeBuffer(50000, 48, ComputeBufferType.Append);
        
        // Indirect args: x, y, z, hitCount
        _dispatchArgsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        
        // 64x64 grid of cells for hit deduplication
        _cellLocksBuffer = new ComputeBuffer(64 * 64, sizeof(uint));
    }

    public void Clear()
    {
        if (PaintCompute == null) return;
        PaintCompute.SetTexture(_initKernel, "PaintTex", PaintTex);
        PaintCompute.SetTexture(_initKernel, "WetTex", _wetTex);
        PaintCompute.SetTexture(_initKernel, "WetTimer", _wetTimerTex);
        PaintCompute.SetTexture(_initKernel, "ThicknessMap", _thicknessTex);
        int groups = Mathf.CeilToInt(TextureRes / 8f);
        PaintCompute.Dispatch(_initKernel, groups, groups, 1);

        PaintCompute.SetTexture(_initKernel, "WetTex", _wetTexPrev);
        PaintCompute.Dispatch(_initKernel, groups, groups, 1);
    }

    void Update()
    {
        UpdateDynamicTilt();
        
        if (PaintCompute == null) return;
        
        // Swap wet textures first so UpdateWetness reads what ApplyPaint wrote last frame
        var temp = _wetTex;
        _wetTex = _wetTexPrev;
        _wetTexPrev = temp;
        
        // Update Wetness / Drying
        PaintCompute.SetFloat("deltaTime", Time.deltaTime);
        PaintCompute.SetFloat("humidity", Humidity);
        
        // Pass tilt and flow parameters
        PaintCompute.SetVector("flowDir", _uvGravityDir);
        PaintCompute.SetFloat("tiltAngle", TiltAngle);
        PaintCompute.SetFloat("wetSpreadRate", WetSpreadRate);

        PaintCompute.SetTexture(_updateWetnessKernel, "PaintTex", PaintTex);
        PaintCompute.SetTexture(_updateWetnessKernel, "WetTexRead", _wetTexPrev);
        PaintCompute.SetTexture(_updateWetnessKernel, "WetTexWrite", _wetTex);
        PaintCompute.SetTexture(_updateWetnessKernel, "WetTimer", _wetTimerTex);
        PaintCompute.SetTexture(_updateWetnessKernel, "ThicknessMap", _thicknessTex);
        
        int groups = Mathf.CeilToInt(TextureRes / 8f);
        PaintCompute.Dispatch(_updateWetnessKernel, groups, groups, 1);
    }

    void UpdateDynamicTilt()
    {
        Vector3 n        = PaintNormalLocal.normalized;
        Vector3 localUp  = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
        Vector3 tangentU = Vector3.Cross(n, localUp).normalized;
        Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

        Vector3 wU = transform.TransformDirection(tangentU);
        Vector3 wV = transform.TransformDirection(tangentV);
        Vector3 wN = transform.TransformDirection(n);

        Vector2 grav = new Vector2(
            Vector3.Dot(Vector3.down, wU),
            Vector3.Dot(Vector3.down, wV)
        );
        _uvGravityDir = grav.sqrMagnitude > 0.001f ? grav.normalized : new Vector2(0, -1);

        if (AutoCalculateTilt)
            TiltAngle = Vector3.Angle(Vector3.up, wN);
    }

    /// <summary>
    /// Processes hits directly from fluid GPU buffers.
    /// </summary>
    public void ProcessGPUHits(ComputeBuffer positions, ComputeBuffer velocities, int particleCount, Color paintColor, float viscosity, float mass, float brushSizeScale = 0.35f)
    {
        if (particleCount == 0 || PaintCompute == null) return;

        // Reset hit append buffer count to 0
        _hitsBuffer.SetCounterValue(0);
        
        // Clear cell locks
        PaintCompute.SetBuffer(_clearLocksKernel, "CellLocks", _cellLocksBuffer);
        PaintCompute.Dispatch(_clearLocksKernel, (64 * 64) / 64, 1, 1);

        // --- 1. Detect Hits ---
        PaintCompute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);
        PaintCompute.SetFloat("canvasWidth", CanvasWidth);
        PaintCompute.SetFloat("canvasHeight", CanvasHeight);
        PaintCompute.SetFloat("hitThreshold", HitThreshold);
        PaintCompute.SetInt("numParticles", particleCount);
        
        PaintCompute.SetFloat("referenceMass", ReferenceParticleMass);
        PaintCompute.SetVector("massScaleClamp", MassScaleClamp);
        
        PaintCompute.SetVector("paintColor", paintColor);
        PaintCompute.SetFloat("paintViscosity", viscosity);
        PaintCompute.SetFloat("paintMass", mass);

        PaintCompute.SetBuffer(_detectHitsKernel, "Positions", positions);
        PaintCompute.SetBuffer(_detectHitsKernel, "Velocities", velocities);
        PaintCompute.SetBuffer(_detectHitsKernel, "Hits", _hitsBuffer);
        PaintCompute.SetBuffer(_detectHitsKernel, "CellLocks", _cellLocksBuffer);

        int hitGroups = Mathf.CeilToInt(particleCount / 256f);
        PaintCompute.Dispatch(_detectHitsKernel, hitGroups, 1, 1);

        // --- 2. Prepare Args for Indirect Dispatch ---
        // Copy the hit count to the first element of args buffer
        ComputeBuffer.CopyCount(_hitsBuffer, _dispatchArgsBuffer, 0);
        
        PaintCompute.SetBuffer(_prepareArgsKernel, "DispatchArgs", _dispatchArgsBuffer);
        PaintCompute.Dispatch(_prepareArgsKernel, 1, 1, 1);

        // --- 3. Apply Paint ---
        PaintCompute.SetFloat("humidity", Humidity);
        PaintCompute.SetFloat("absorptionRate", AbsorptionRate);
        PaintCompute.SetFloat("grainStrength", GrainStrength);
        PaintCompute.SetFloat("brushSizeScale", brushSizeScale > 0 ? brushSizeScale : BrushSizeScale);
        
        PaintCompute.SetBuffer(_applyPaintKernel, "ReadHits", _hitsBuffer);
        
        // Let the shader know the count just in case, but DispatchIndirect uses the args buffer
        // Note: For StructuredBuffer bounds checking in HLSL we need the count.
        // We will pass the args buffer as a generic StructuredBuffer to read hitCount.
        PaintCompute.SetBuffer(_applyPaintKernel, "DispatchArgs", _dispatchArgsBuffer); 
        
        PaintCompute.SetTexture(_applyPaintKernel, "PaintTex", PaintTex);
        PaintCompute.SetTexture(_applyPaintKernel, "WetTex", _wetTex);
        PaintCompute.SetTexture(_applyPaintKernel, "WetTimer", _wetTimerTex);
        PaintCompute.SetTexture(_applyPaintKernel, "ThicknessMap", _thicknessTex);
        PaintCompute.SetTexture(_applyPaintKernel, "GrainMap", _grainTex);

        // We dispatch indirectly based on the hit count calculated in _prepareArgsKernel
        PaintCompute.DispatchIndirect(_applyPaintKernel, _dispatchArgsBuffer);
    }

    void OnDestroy()
    {
        if (PaintTex != null) PaintTex.Release();
        if (_wetTex != null) _wetTex.Release();
        if (_wetTexPrev != null) _wetTexPrev.Release();
        if (_wetTimerTex != null) _wetTimerTex.Release();
        if (_thicknessTex != null) _thicknessTex.Release();
        
        if (_hitsBuffer != null) _hitsBuffer.Release();
        if (_dispatchArgsBuffer != null) _dispatchArgsBuffer.Release();
        if (_cellLocksBuffer != null) _cellLocksBuffer.Release();
    }

    // Stub to maintain compatibility with CPU painters like PendulumPaintDropper
    public bool TryPaint(FluidParticleData particle, float dt)
    {
        return false;
    }
}
