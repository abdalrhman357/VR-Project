Shader "Fluid/BucketDepthMask"
{
    // Writes to the depth buffer only — no colour output.
    // Rendered at queue 1999 (before particles at 2000) so any particle
    // fragment that falls behind the bucket wall is discarded by ZTest.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        Pass
        {
            ZWrite On
            ZTest  LEqual
            Cull   Back
            ColorMask 0          // write NO colour, depth only
        }
    }
}
