using UnityEngine;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Draws the rope as a poly-line following the XPBD node positions. Pure
    /// visualisation — it reads <see cref="PaintPendulumSystem.Rope"/> and never
    /// touches the simulation.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class RopeLineRenderer : MonoBehaviour
    {
        public PaintPendulumSystem pendulum;
        public Color ropeColor = new Color(0.75f, 0.7f, 0.6f);
        public Color brokenColor = new Color(0.8f, 0.15f, 0.1f);
        [Tooltip("Visual line width in world units. If >0 it overrides the (physical, often tiny) rope diameter so the rope stays visible at any scene scale.")]
        public float widthOverride = 0f;

        LineRenderer _lr;

        void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.numCapVertices = 2;
            _lr.numCornerVertices = 2;
            _lr.material = new Material(Shader.Find("Sprites/Default"));
            _lr.textureMode = LineTextureMode.Stretch;
            _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void LateUpdate()
        {
            if (pendulum == null) return;
            var rope = pendulum.Rope;
            if (rope == null) { _lr.positionCount = 0; return; }

            float w = widthOverride > 0f ? widthOverride : Mathf.Max(0.004f, rope.Material.diameter);
            _lr.startWidth = w;
            _lr.endWidth = w;

            bool broken = rope.IsBroken();
            _lr.startColor = broken ? brokenColor : ropeColor;
            _lr.endColor = broken ? brokenColor : ropeColor;

            if (_lr.positionCount != rope.NodeCount)
                _lr.positionCount = rope.NodeCount;
            for (int i = 0; i < rope.NodeCount; i++)
                _lr.SetPosition(i, rope.pos[i]);
        }
    }
}
