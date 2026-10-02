// Invisible disc at the cup opening. Marks its visible pixels in the stencil buffer so CupInterior
// can draw the hole "through" the terrain without cutting the terrain mesh.
Shader "GolfSim/CupMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CupMask"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Stencil { Ref 64 WriteMask 64 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
