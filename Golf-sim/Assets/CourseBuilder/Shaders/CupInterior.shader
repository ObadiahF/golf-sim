// Inside of the cup (soil collar, plastic liner, bottom). Drawn only where CupMask marked the
// stencil and with ZTest Always, so it shows below the green surface. Its inward-facing walls
// form a convex shell, so back-face culling alone keeps the draw order correct.
Shader "GolfSim/CupInterior"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+2" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CupInterior"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest Always
            Cull Back
            Stencil { Ref 64 ReadMask 64 Comp Equal }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Brightness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.color = i.color; // baked colour with depth darkening (the hole is mostly in shadow)
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(i.color.rgb * _Brightness, 1); }
            ENDHLSL
        }
    }
}
