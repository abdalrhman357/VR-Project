// Screen-Space Fluid — Composite pass.
// Reads the smooth thickness field + the rendered scene, and shades a continuous liquid SURFACE over
// the scene: a screen-space normal from the thickness gradient gives diffuse + fresnel-edge + specular
// highlights (the "wet liquid" look), and thickness drives opacity (thin edges fade). This turns the
// discrete SPH particles into one cohesive liquid body.
Shader "Fluid/SSFComposite"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;             // scene colour (set by Graphics.Blit)
            sampler2D _Thickness;
            float4    _Thickness_TexelSize;
            float4    _LiquidColor;
            float     _Threshold;
            float     _Opacity;
            float3    _LightDir;

            float4 frag(v2f_img i) : SV_Target
            {
                float th = tex2D(_Thickness, i.uv).r;
                float4 scene = tex2D(_MainTex, i.uv);
                if (th < _Threshold) return scene;                 // no fluid here → scene unchanged

                // Screen-space surface normal from the thickness gradient.
                float2 tx = _Thickness_TexelSize.xy;
                float dX = tex2D(_Thickness, i.uv + float2(tx.x, 0)).r - tex2D(_Thickness, i.uv - float2(tx.x, 0)).r;
                float dY = tex2D(_Thickness, i.uv + float2(0, tx.y)).r - tex2D(_Thickness, i.uv - float2(0, tx.y)).r;
                float3 n = normalize(float3(-dX, -dY, 0.06));

                float3 L = normalize(_LightDir);
                float diff = saturate(dot(n, L)) * 0.55 + 0.45;
                float fres = pow(1.0 - saturate(n.z), 3.0) * 0.6;                 // bright rim
                float spec = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 48.0) * 0.9;
                float3 col = _LiquidColor.rgb * diff + fres.xxx + spec.xxx;

                float alpha = saturate(th * _Opacity);
                return float4(lerp(scene.rgb, col, alpha), 1.0);
            }
            ENDCG
        }
    }
}
