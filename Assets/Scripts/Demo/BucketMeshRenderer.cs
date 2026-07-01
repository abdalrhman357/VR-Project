using UnityEngine;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Builds and shows the bucket mesh on the container transform the pendulum
    /// drives. It only reads the bucket's dimensions; the physics owns the motion.
    /// </summary>
    public class BucketMeshRenderer : MonoBehaviour
    {
        public PaintPendulumSystem pendulum;
        public Color bucketColor = new Color(0.22f, 0.23f, 0.26f);
        [Range(0f, 1f)] public float metallic = 0.65f;
        [Range(0f, 1f)] public float smoothness = 0.55f;
        [Range(8, 96)] public int segments = 48;

        MeshFilter _mf;
        MeshRenderer _mr;
        float _cachedR = -1f, _cachedH = -1f;

        void Start()
        {
            // NB: never use '??' with Unity Objects (fake-null pitfall). Use TryGetComponent.
            if (!TryGetComponent(out _mf)) _mf = gameObject.AddComponent<MeshFilter>();
            if (!TryGetComponent(out _mr)) _mr = gameObject.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = bucketColor;
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Glossiness", smoothness);
            _mr.sharedMaterial = mat;
        }

        void LateUpdate()
        {
            if (pendulum == null || pendulum.Bucket == null) return;
            float r = pendulum.Bucket.Radius;
            float h = pendulum.Bucket.Height;
            if (Mathf.Abs(r - _cachedR) > 1e-4f || Mathf.Abs(h - _cachedH) > 1e-4f)
            {
                if (_mf.sharedMesh != null) Destroy(_mf.sharedMesh);
                _mf.sharedMesh = PrimitiveMeshFactory.CreateBucket(
                    r, h, Mathf.Min(0.02f, r * 0.15f), segments);
                _cachedR = r; _cachedH = h;
            }
        }
    }
}
