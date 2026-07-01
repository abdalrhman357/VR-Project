// Screen-Space Fluid — Thickness pass.
// Renders each SPH particle as a camera-facing soft round blob, accumulated ADDITIVELY into an
// RFloat target. The result is a smooth "thickness" field (how much fluid is along each view ray),
// which the composite pass turns into a continuous liquid surface. Drawn via DrawMeshInstancedIndirect
// reading the GPU Positions buffer directly (no CPU readback).
Shader "Fluid/SSFThickness"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Blend One One        // additive accumulation
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            StructuredBuffer<float3> Positions;
            float _Radius;
            float _ThicknessScale;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v, uint iid : SV_InstanceID)
            {
                v2f o;
                float3 worldCentre = Positions[iid];
                float3 viewCentre  = mul(UNITY_MATRIX_V, float4(worldCentre, 1.0)).xyz;
                float2 corner = v.vertex.xy * 2.0;                 // quad verts ±0.5 → ±1
                float3 viewPos = viewCentre + float3(corner * _Radius, 0.0);
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv  = corner;
                return o;
            }

            float frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0) discard;                              // round blob
                return exp(-r2 * 3.0) * _ThicknessScale;           // soft gaussian falloff
            }
            ENDCG
        }
    }
}
