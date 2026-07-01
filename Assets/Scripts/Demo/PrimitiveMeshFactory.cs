using System.Collections.Generic;
using UnityEngine;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Procedural meshes for the demo, built entirely from vertices/triangles so
    /// the project needs no imported model assets and no primitive colliders
    /// (GameObject.CreatePrimitive attaches a Collider, which the spec forbids).
    /// </summary>
    public static class PrimitiveMeshFactory
    {
        /// <summary>A flat quad in the XZ plane, centred at origin, facing +Y.</summary>
        public static Mesh CreateQuadXZ(float size)
        {
            float h = size * 0.5f;
            var m = new Mesh { name = "QuadXZ" };
            m.vertices = new[]
            {
                new Vector3(-h, 0, -h), new Vector3(-h, 0,  h),
                new Vector3( h, 0,  h), new Vector3( h, 0, -h)
            };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Low-poly UV sphere for paint droplets (instanced).</summary>
        public static Mesh CreateUVSphere(float radius, int rings, int segments)
        {
            rings = Mathf.Max(3, rings);
            segments = Mathf.Max(3, segments);

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            for (int y = 0; y <= rings; y++)
            {
                float v = (float)y / rings;
                float phi = v * Mathf.PI;             // 0..π
                for (int x = 0; x <= segments; x++)
                {
                    float u = (float)x / segments;
                    float theta = u * Mathf.PI * 2f;  // 0..2π
                    Vector3 n = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));
                    norms.Add(n);
                    verts.Add(n * radius);
                }
            }

            int stride = segments + 1;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int a = y * stride + x;
                    int b = a + stride;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }

            var m = new Mesh { name = "UVSphere" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A simple open-top bucket (cup): outer wall, inner wall, an inner floor,
        /// an outer bottom, and a rim ring. Centred on the origin, axis = local Y.
        /// </summary>
        public static Mesh CreateBucket(float radius, float height, float wallThickness, int segments)
        {
            segments = Mathf.Max(8, segments);
            float rO = radius;
            float rI = Mathf.Max(0.001f, radius - wallThickness);
            float top = height * 0.5f;
            float bot = -height * 0.5f;
            float floorY = bot + wallThickness;

            var V = new List<Vector3>();
            var N = new List<Vector3>();
            var T = new List<int>();

            AddWall(V, N, T, rO, bot, top, segments, outward: true);
            AddWall(V, N, T, rI, floorY, top, segments, outward: false);
            AddRing(V, N, T, rI, rO, top, segments, up: true);       // rim
            AddDisc(V, N, T, rI, floorY, segments, up: true);        // inner floor
            AddDisc(V, N, T, rO, bot, segments, up: false);          // outer bottom

            var m = new Mesh { name = "Bucket" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }

        static void AddWall(List<Vector3> V, List<Vector3> N, List<int> T,
                            float r, float yBot, float yTop, int segs, bool outward)
        {
            int b = V.Count; float s = outward ? 1f : -1f;
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                Vector3 n = new Vector3(cx, 0, cz) * s;
                V.Add(new Vector3(cx * r, yBot, cz * r)); N.Add(n);
                V.Add(new Vector3(cx * r, yTop, cz * r)); N.Add(n);
            }
            for (int i = 0; i < segs; i++)
            {
                int bl = b + i * 2, tl = bl + 1, br = b + (i + 1) * 2, tr = br + 1;
                if (outward) { T.Add(bl); T.Add(tr); T.Add(tl); T.Add(bl); T.Add(br); T.Add(tr); }
                else { T.Add(bl); T.Add(tl); T.Add(tr); T.Add(bl); T.Add(tr); T.Add(br); }
            }
        }

        static void AddRing(List<Vector3> V, List<Vector3> N, List<int> T,
                            float iR, float oR, float y, int segs, bool up)
        {
            int b = V.Count; Vector3 n = up ? Vector3.up : Vector3.down;
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                V.Add(new Vector3(cx * iR, y, cz * iR)); N.Add(n);
                V.Add(new Vector3(cx * oR, y, cz * oR)); N.Add(n);
            }
            for (int i = 0; i < segs; i++)
            {
                int i0 = b + i * 2, o0 = i0 + 1, i1 = b + (i + 1) * 2, o1 = i1 + 1;
                if (up) { T.Add(i0); T.Add(i1); T.Add(o1); T.Add(i0); T.Add(o1); T.Add(o0); }
                else { T.Add(i0); T.Add(o1); T.Add(i1); T.Add(i0); T.Add(o0); T.Add(o1); }
            }
        }

        static void AddDisc(List<Vector3> V, List<Vector3> N, List<int> T,
                            float r, float y, int segs, bool up)
        {
            int b = V.Count; Vector3 n = up ? Vector3.up : Vector3.down;
            V.Add(new Vector3(0, y, 0)); N.Add(n);
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                V.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); N.Add(n);
            }
            for (int i = 0; i < segs; i++)
            {
                int c = b, v0 = b + 1 + i, v1 = b + 2 + i;
                if (up) { T.Add(c); T.Add(v0); T.Add(v1); }
                else { T.Add(c); T.Add(v1); T.Add(v0); }
            }
        }
    }
}
