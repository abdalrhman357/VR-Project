using UnityEngine;
using UnityEngine.Rendering;
using Seb.Fluid.Simulation;
using Seb.Fluid.Rendering;
using Seb.Helpers;
using SwingingPaint.Core;

namespace SwingingPaint.Rendering
{
    /// <summary>
    /// Screen-Space Fluid Rendering — turns the discrete SPH particles into ONE smooth, cohesive liquid
    /// surface (the realistic "calm liquid" look that per-particle spheres can never give).
    ///
    /// Pipeline (camera image effect, Built-in RP):
    ///   1. Thickness pass — draw every particle as a soft camera-facing blob, accumulated additively
    ///      into an RFloat target (DrawMeshInstancedIndirect straight from the GPU Positions buffer).
    ///   2. Composite pass — screen-space normal from the thickness gradient → diffuse + fresnel rim +
    ///      specular, thickness → opacity. Blended over the scene.
    ///
    /// Toggle <see cref="active"/> off (control panel) to fall back to the sphere particle display.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ScreenSpaceFluidRenderer : MonoBehaviour
    {
        public FluidSim fluidSim;
        public ParticleDisplay3D particleDisplay;   // hidden while the surface is active

        [Header("Surface")]
        public bool active = true;
        public Color liquidColor = new Color(0.10f, 0.35f, 0.92f);
        [Tooltip("Blob radius (world units). ~1.3× particle spacing reads as a connected surface.")]
        public float radius = 0.16f;
        public float thicknessScale = 0.35f;
        [Range(0f, 3f)] public float threshold = 0.3f;
        [Range(0.1f, 5f)] public float opacity = 1.8f;
        public Vector3 lightDir = new Vector3(0.3f, 0.7f, -0.6f);

        Camera _cam;
        Material _thickMat, _compMat;
        Mesh _quad;
        ComputeBuffer _args;
        bool _ready;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (fluidSim == null) fluidSim = SceneRefs.FindFirst<FluidSim>();
            if (particleDisplay == null) particleDisplay = SceneRefs.FindFirst<ParticleDisplay3D>();

            _quad = MakeQuad();
            var ts = Shader.Find("Fluid/SSFThickness");
            var cs = Shader.Find("Fluid/SSFComposite");
            if (ts != null) _thickMat = new Material(ts);
            if (cs != null) _compMat = new Material(cs);

            if (fluidSim != null)
            {
                fluidSim.SimulationInitCompleted += OnFluidInit;
                if (fluidSim.positionBuffer != null) OnFluidInit(fluidSim);
            }
        }

        void OnFluidInit(FluidSim sim)
        {
            if (sim.positionBuffer == null) return;
            ComputeHelper.CreateArgsBuffer(ref _args, _quad, sim.positionBuffer.count);
            if (_thickMat != null) _thickMat.SetBuffer("Positions", sim.positionBuffer);
            _ready = _thickMat != null && _compMat != null;
        }

        void Update()
        {
            // Hide the sphere display while the smooth surface is rendering (and restore it when off).
            if (particleDisplay != null && particleDisplay.enabled == (active && _ready))
                particleDisplay.enabled = !(active && _ready);
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!active || !_ready || fluidSim == null || fluidSim.positionBuffer == null || _args == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            var thick = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.RFloat);

            _thickMat.SetBuffer("Positions", fluidSim.positionBuffer);
            _thickMat.SetFloat("_Radius", radius);
            _thickMat.SetFloat("_ThicknessScale", thicknessScale);

            var cmd = new CommandBuffer { name = "SSF Thickness" };
            cmd.SetRenderTarget(thick);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetViewProjectionMatrices(_cam.worldToCameraMatrix, _cam.projectionMatrix);
            cmd.DrawMeshInstancedIndirect(_quad, 0, _thickMat, 0, _args);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            _compMat.SetTexture("_Thickness", thick);
            _compMat.SetColor("_LiquidColor", liquidColor);
            _compMat.SetFloat("_Threshold", threshold);
            _compMat.SetFloat("_Opacity", opacity);
            _compMat.SetVector("_LightDir", lightDir.normalized);
            Graphics.Blit(src, dst, _compMat);

            RenderTexture.ReleaseTemporary(thick);
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "SSFQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),  new Vector3(-0.5f, 0.5f, 0f)
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            if (fluidSim != null) fluidSim.SimulationInitCompleted -= OnFluidInit;
            _args?.Release(); _args = null;
            if (_thickMat != null) Destroy(_thickMat);
            if (_compMat != null) Destroy(_compMat);
            if (_quad != null) Destroy(_quad);
        }
    }
}
