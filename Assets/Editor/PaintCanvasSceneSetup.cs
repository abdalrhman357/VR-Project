using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PaintCanvasSceneSetup
{
    [MenuItem("Tools/Setup Paint Canvas Test Scene")]
    public static void CreateTestScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "PaintCanvas_Surface";
        quad.transform.position = Vector3.zero;
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(3f, 3f, 1f);

        PaintCanvas canvas = quad.AddComponent<PaintCanvas>();
        GameObject bucketSimObj = new GameObject("PendulumPaintDropper");
        PendulumPaintDropper simulator = bucketSimObj.AddComponent<PendulumPaintDropper>();
        
        canvas.CanvasWidth = 3f;
        canvas.CanvasHeight = 3f;
        canvas.TextureRes = 1024;
        canvas.PaintNormalLocal = Vector3.back;
        canvas.HitThreshold = 0.5f;
        canvas.TiltAngle = 0f;

        GameObject armPivotObj = new GameObject("ArmPivot");
        armPivotObj.transform.position = new Vector3(0, 4f, 0);

        simulator.CanvasToTest = canvas;
        simulator.ArmPivot = armPivotObj.transform;
        simulator.ArmLength = 3f;
        simulator.DropsPerSecond = 18f;
        simulator.DropMass = 0.005f;
        simulator.Viscosity = 1.2f;

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 4f, 0);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        
        string scenePath = "Assets/Scenes/PaintCanvasTestScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        
        Debug.Log("[PaintCanvas] تم تجهيز المشهد بنجاح!");
    }
}
