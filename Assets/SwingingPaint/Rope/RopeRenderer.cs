using UnityEngine;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// Draws the Verlet rope as a smooth line. Reads particle positions from a <see cref="RopeSimulator"/>
    /// each frame and feeds them to a LineRenderer. Creates its own unlit material at runtime so the auto
    /// scene builder needs no asset references.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class RopeRenderer : MonoBehaviour
    {
        public RopeSimulator rope;
        public float width = 0.02f;
        public Color color = new Color(0.45f, 0.30f, 0.18f); // rope brown

        LineRenderer _lr;

        void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.widthMultiplier = width;
            _lr.numCapVertices = 4;
            _lr.numCornerVertices = 2;
            _lr.textureMode = LineTextureMode.Stretch;
            _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _lr.receiveShadows = true;

            // Built-in unlit colour material (no asset reference needed).
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _lr.material = new Material(sh) { color = color };
            _lr.startColor = _lr.endColor = color;
        }

        void LateUpdate()
        {
            if (rope == null || !rope.IsInitialized) { _lr.positionCount = 0; return; }

            var ps = rope.Rope.Particles;
            if (_lr.positionCount != ps.Count) _lr.positionCount = ps.Count;
            for (int i = 0; i < ps.Count; i++)
                _lr.SetPosition(i, ps[i].Position);
        }

        void OnDestroy()
        {
            if (_lr != null && _lr.material != null) Destroy(_lr.material);
        }
    }
}
