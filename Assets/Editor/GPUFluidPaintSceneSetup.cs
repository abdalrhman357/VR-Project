using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Seb.Fluid.Simulation;
using Seb.Fluid.Demo;

public class GPUFluidPaintSceneSetup
{
    [MenuItem("Tools/Setup GPU Fluid Paint Test Scene")]
    public static void CreateTestScene()
    {
        // Ask to save current scene first
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // Try to load the original fluid scene to preserve its complex setup
        string fluidScenePath = "Assets/Scenes/Fluid Particles.unity";
        Scene fluidScene = EditorSceneManager.OpenScene(fluidScenePath, OpenSceneMode.Single);
        
        if (!fluidScene.IsValid())
        {
            Debug.LogError("Could not find 'Fluid Particles.unity'. Please open it manually and add the Canvas.");
            return;
        }

        // 1. Find the FluidSim in the scene first to know its position
        FluidSim fluidSim = Object.FindObjectOfType<FluidSim>();
        Vector3 fluidPos = Vector3.zero;
        if (fluidSim != null)
        {
            fluidPos = fluidSim.transform.position;
        }

        // 2. Create the Canvas
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "PaintCanvas_Surface";
        // Position it 15 units below the fluid sim so there is a clear gap
        quad.transform.position = fluidPos + new Vector3(0, -15f, 0); 
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(12f, 12f, 1f);

        // Assign a bright Unlit material to the Canvas so the paint is clearly visible
        Renderer quadRenderer = quad.GetComponent<Renderer>();
        Shader unlitShader = Shader.Find("Unlit/Texture");
        if (unlitShader != null)
        {
            quadRenderer.material = new Material(unlitShader);
        }

        PaintCanvas canvas = quad.AddComponent<PaintCanvas>();
        canvas.CanvasWidth = 12f;
        canvas.CanvasHeight = 12f;
        canvas.TextureRes = 1024;
        canvas.PaintNormalLocal = Vector3.forward; // A Unity Quad's normal is +Z locally
        canvas.HitThreshold = 1.0f; // Increase threshold slightly just in case

        // Try to find the compute shader automatically
        string[] guids = AssetDatabase.FindAssets("PaintCanvas t:ComputeShader");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            canvas.PaintCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
        }
        else
        {
            Debug.LogWarning("PaintCanvas.compute not found! Please assign it manually on PaintCanvas_Surface.");
        }

        // 2. Setup the Bridge
        GPUFluidPainter painter = quad.AddComponent<GPUFluidPainter>();
        painter.paintCanvas = canvas;
        painter.PaintColor = Color.magenta; // Use a bright color like magenta to stand out

        if (fluidSim != null)
        {
            painter.fluidSimulation = fluidSim;
            // Also enable the hole so fluid drips down
            fluidSim.holeRadius = 2.0f;
        }
        else
        {
            Debug.LogWarning("FluidSim not found in the scene! Please assign it manually on PaintCanvas_Surface.");
        }

        // Add a bright Directional Light to make the cylinder and fluid look good
        GameObject lightObj = new GameObject("Main_Directional_Light");
        Light dirLight = lightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = Color.white;
        dirLight.intensity = 1.5f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.3f);

        // Adjust Camera to see both the cylinder and the canvas
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, -2f, -25f);
            cam.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            
            // If there's an OrbitCam, set its pivot to the middle
            OrbitCam orbit = cam.GetComponent<OrbitCam>();
            if (orbit != null)
            {
                orbit.focusDst = 25f;
            }
        }

        // 3. Save as a new scene so we don't overwrite the original Fluid Particles scene
        string newScenePath = "Assets/Scenes/GPUFluidPaintTestScene.unity";
        EditorSceneManager.SaveScene(fluidScene, newScenePath);
        Debug.Log("Successfully created and saved " + newScenePath);
    }
}
