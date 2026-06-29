using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{
    /// <summary>
    /// Renders a procedural cylindrical bucket around the FluidSim.
    ///
    /// Uses two draw passes to correctly occlude fluid particles:
    ///   Pass 1 — Depth Mask  (queue 1999, ColorMask 0): writes depth buffer BEFORE particles
    ///   Pass 2 — Visual      (queue 2001, normal shading): draws bucket colour AFTER particles
    ///
    /// This guarantees particles behind the bucket wall are never visible,
    /// without changing any simulation logic.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]   // Update runs before ParticleDisplay3D so depth mask is ready
    public class BucketRenderer : MonoBehaviour
    {
        // ── References ───────────────────────────────────────────────────────────
        [Header("References")]
        public FluidSim fluidSim;

        // ── Appearance ───────────────────────────────────────────────────────────
        [Header("Appearance")]
        public Color bucketColour = new Color(0.20f, 0.20f, 0.22f);
        public Color rimColour    = new Color(0.70f, 0.70f, 0.72f);
        [Range(0f, 1f)] public float metallic   = 0.7f;
        [Range(0f, 1f)] public float smoothness = 0.5f;

        // ── Geometry ─────────────────────────────────────────────────────────────
        [Header("Geometry")]
        [Range(8, 128)]
        public int segments = 48;

        [Range(0.005f, 0.15f)]
        public float wallThickness = 0.015f;

        [Range(0.005f, 0.15f)]
        public float rimHeight = 0.03f;

        [Range(0.1f, 1f)]
        public float heightScale = 0.6f;

        [Tooltip("Extra radius so particles don't poke through the wall.")]
        [Range(0f, 0.5f)]
        public float radiusPadding = 0.08f;

        [Tooltip("Extra depth below sim floor so particles don't show through the bottom.")]
        [Range(0f, 0.5f)]
        public float bottomPadding = 0.1f;

        // ── Internals ─────────────────────────────────────────────────────────────
        // Body mesh used for both depth-mask and visual passes
        Mesh     bodyMesh, rimMesh;

        // Pass 1: depth mask materials (ColorMask 0, queue 1999)
        Material bodyMaskMat, rimMaskMat;

        // Pass 2: visual materials (Standard, queue 2001)
        Material bodyVisMat, rimVisMat;

        float _centreY;

        // dirty cache
        int   _cSeg;
        float _cWall, _cRimH, _cHS, _cRP, _cBP, _cR, _cH, _cHole;
        float _cMetal, _cSmooth;
        Color _cCol, _cRimCol;

        static readonly Bounds kBigBounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

        // ─────────────────────────────────────────────────────────────────────────

        void OnEnable()  => Rebuild();
        void OnDisable() => Cleanup();
        void OnDestroy() => Cleanup();

        void LateUpdate()
        {
            if (fluidSim == null) return;

            if (IsDirty()) Rebuild();

            if (bodyMesh == null) return;

            Matrix4x4 m = Matrix4x4.TRS(
                fluidSim.transform.position + Vector3.up * _centreY,
                fluidSim.transform.rotation,
                Vector3.one);

            // Pass 1 — depth mask (before particles, no colour)
            Graphics.DrawMesh(bodyMesh, m, bodyMaskMat, 0);
            Graphics.DrawMesh(rimMesh,  m, rimMaskMat,  0);

            // Pass 2 — visual (after particles, with colour)
            Graphics.DrawMesh(bodyMesh, m, bodyVisMat, 0);
            Graphics.DrawMesh(rimMesh,  m, rimVisMat,  0);
        }

        // ── Dirty check ───────────────────────────────────────────────────────────

        bool IsDirty()
        {
            float r    = fluidSim.Scale.x * 0.5f;
            float h    = fluidSim.Scale.y;
            float hole = fluidSim.holeRadius;
            return segments      != _cSeg   || wallThickness != _cWall  ||
                   rimHeight     != _cRimH  || heightScale   != _cHS    ||
                   radiusPadding != _cRP    || bottomPadding != _cBP    ||
                   metallic      != _cMetal || smoothness    != _cSmooth ||
                   Mathf.Abs(r    - _cR)    > 1e-5f ||
                   Mathf.Abs(h    - _cH)    > 1e-5f ||
                   Mathf.Abs(hole - _cHole) > 1e-5f ||
                   bucketColour != _cCol || rimColour != _cRimCol;
        }

        // ── Rebuild ───────────────────────────────────────────────────────────────

        void Rebuild()
        {
            if (fluidSim == null) return;

            float simHalfH = fluidSim.Scale.y * 0.5f;
            float simR     = fluidSim.Scale.x * 0.5f;
            float innerR   = simR + radiusPadding;
            float outerR   = innerR + wallThickness;

            float bucketTop    =  simHalfH * heightScale;
            float bucketBottom = -simHalfH - bottomPadding;
            float halfH        = (bucketTop - bucketBottom) * 0.5f;
            _centreY           = (bucketTop + bucketBottom) * 0.5f;

            float hole = fluidSim.holeRadius;

            // Rebuild meshes
            DestroyMesh(ref bodyMesh);
            DestroyMesh(ref rimMesh);
            bodyMesh = MakeBody(innerR, outerR, halfH, hole, segments);
            rimMesh  = MakeRim (innerR, outerR, halfH, segments, rimHeight);

            // Rebuild materials
            DestroyMat(ref bodyMaskMat); DestroyMat(ref rimMaskMat);
            DestroyMat(ref bodyVisMat);  DestroyMat(ref rimVisMat);

            bodyMaskMat = MakeMaskMat();
            rimMaskMat  = MakeMaskMat();
            bodyVisMat  = MakeVisMat(bucketColour);
            rimVisMat   = MakeVisMat(rimColour);

            // Cache
            _cSeg = segments; _cWall = wallThickness; _cRimH = rimHeight;
            _cHS  = heightScale; _cRP = radiusPadding; _cBP = bottomPadding;
            _cR   = simR; _cH = fluidSim.Scale.y; _cHole = hole;
            _cCol = bucketColour; _cRimCol = rimColour;
            _cMetal = metallic; _cSmooth = smoothness;
        }

        // ── Material factories ────────────────────────────────────────────────────

        // Depth-only pass: renders BEFORE particles (queue 1999)
        // Writes depth, writes nothing to colour buffer
        static Material MakeMaskMat()
        {
            var mat = new Material(Shader.Find("Fluid/BucketDepthMask"));
            mat.renderQueue = 1999;
            return mat;
        }

        // Visual pass: renders AFTER particles (queue 2001)
        // Normal opaque shading with ZTest LEqual so it only shows where depth mask wrote
        Material MakeVisMat(Color col)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 0);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_ZWrite", 1);
            mat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            mat.renderQueue = 2001;
            mat.color       = col;
            mat.SetFloat("_Metallic",   metallic);
            mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        // ── Mesh builders ─────────────────────────────────────────────────────────

        static Mesh MakeBody(float innerR, float outerR, float halfH, float holeR, int segs)
        {
            var V = new System.Collections.Generic.List<Vector3>();
            var N = new System.Collections.Generic.List<Vector3>();
            var T = new System.Collections.Generic.List<int>();

            CylWall(V, N, T, innerR, -halfH, halfH, segs, inward: true);
            CylWall(V, N, T, outerR, -halfH, halfH, segs, inward: false);
            FlatRing(V, N, T, innerR, outerR, halfH, segs, up: true);

            if (holeR <= 0f)
            {
                FlatDisc(V, N, T, outerR, -halfH, segs, up: false);
                FlatDisc(V, N, T, outerR, -halfH, segs, up: true);
            }
            else
            {
                float ch = Mathf.Clamp(holeR, 0.001f, outerR - 0.001f);
                FlatRing(V, N, T, ch, outerR, -halfH, segs, up: false);
                FlatRing(V, N, T, ch, outerR, -halfH, segs, up: true);
            }

            return ToMesh("BucketBody", V, N, T);
        }

        static Mesh MakeRim(float innerR, float outerR, float halfH, int segs, float rimH)
        {
            var V = new System.Collections.Generic.List<Vector3>();
            var N = new System.Collections.Generic.List<Vector3>();
            var T = new System.Collections.Generic.List<int>();

            float bot = halfH, top = halfH + rimH;
            CylWall(V, N, T, outerR, bot, top, segs, inward: false);
            CylWall(V, N, T, innerR, bot, top, segs, inward: true);
            FlatRing(V, N, T, innerR, outerR, top, segs, up: true);
            FlatRing(V, N, T, innerR, outerR, bot, segs, up: false);

            return ToMesh("BucketRim", V, N, T);
        }

        // ── Geometry helpers ──────────────────────────────────────────────────────

        static void CylWall(System.Collections.Generic.List<Vector3> V,
                             System.Collections.Generic.List<Vector3> N,
                             System.Collections.Generic.List<int>     T,
                             float r, float yBot, float yTop, int segs, bool inward)
        {
            int b = V.Count; float s = inward ? -1f : 1f;
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                Vector3 n = new Vector3(cx, 0, cz) * s;
                V.Add(new Vector3(cx*r, yBot, cz*r)); N.Add(n);
                V.Add(new Vector3(cx*r, yTop, cz*r)); N.Add(n);
            }
            for (int i = 0; i < segs; i++)
            {
                int bl=b+i*2, tl=bl+1, br=b+(i+1)*2, tr=br+1;
                if (inward){ T.Add(bl);T.Add(tl);T.Add(tr); T.Add(bl);T.Add(tr);T.Add(br); }
                else       { T.Add(bl);T.Add(tr);T.Add(tl); T.Add(bl);T.Add(br);T.Add(tr); }
            }
        }

        static void FlatRing(System.Collections.Generic.List<Vector3> V,
                              System.Collections.Generic.List<Vector3> N,
                              System.Collections.Generic.List<int>     T,
                              float iR, float oR, float y, int segs, bool up)
        {
            int b = V.Count; Vector3 n = up ? Vector3.up : Vector3.down;
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                V.Add(new Vector3(cx*iR, y, cz*iR)); N.Add(n);
                V.Add(new Vector3(cx*oR, y, cz*oR)); N.Add(n);
            }
            for (int i = 0; i < segs; i++)
            {
                int i0=b+i*2, o0=i0+1, i1=b+(i+1)*2, o1=i1+1;
                if (up){ T.Add(i0);T.Add(i1);T.Add(o1); T.Add(i0);T.Add(o1);T.Add(o0); }
                else   { T.Add(i0);T.Add(o1);T.Add(i1); T.Add(i0);T.Add(o0);T.Add(o1); }
            }
        }

        static void FlatDisc(System.Collections.Generic.List<Vector3> V,
                              System.Collections.Generic.List<Vector3> N,
                              System.Collections.Generic.List<int>     T,
                              float r, float y, int segs, bool up)
        {
            int b = V.Count; Vector3 n = up ? Vector3.up : Vector3.down;
            V.Add(new Vector3(0, y, 0)); N.Add(n);
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                V.Add(new Vector3(Mathf.Cos(a)*r, y, Mathf.Sin(a)*r)); N.Add(n);
            }
            for (int i = 0; i < segs; i++)
            {
                int c=b, v0=b+1+i, v1=b+2+i;
                if (up){ T.Add(c);T.Add(v0);T.Add(v1); }
                else   { T.Add(c);T.Add(v1);T.Add(v0); }
            }
        }

        static Mesh ToMesh(string nm,
                            System.Collections.Generic.List<Vector3> V,
                            System.Collections.Generic.List<Vector3> N,
                            System.Collections.Generic.List<int>     T)
        {
            var m = new Mesh { name = nm };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────────

        void Cleanup()
        {
            DestroyMesh(ref bodyMesh);  DestroyMesh(ref rimMesh);
            DestroyMat(ref bodyMaskMat); DestroyMat(ref rimMaskMat);
            DestroyMat(ref bodyVisMat);  DestroyMat(ref rimVisMat);
        }

        static void DestroyMesh(ref Mesh m)
        {
            if (m == null) return;
#if UNITY_EDITOR
            DestroyImmediate(m);
#else
            Destroy(m);
#endif
            m = null;
        }

        static void DestroyMat(ref Material m)
        {
            if (m == null) return;
#if UNITY_EDITOR
            DestroyImmediate(m);
#else
            Destroy(m);
#endif
            m = null;
        }
    }
}
