// Skybox for dusk and night (SkyLighting): a zenith-to-horizon gradient, an afterglow on the horizon toward
// the set sun, twinkling stars hashed from the view direction and a moon disc. A skybox is drawn behind
// everything and never fogged, so the stars survive the night fog that a far-away star mesh would not.
Shader "GolfSim/NightSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.008, 0.012, 0.04, 1)
        _HorizonColor ("Horizon", Color) = (0.05, 0.07, 0.14, 1)
        _GroundColor ("Below the horizon", Color) = (0.02, 0.02, 0.03, 1)
        [HDR] _GlowColor ("Afterglow", Color) = (0, 0, 0, 1)
        _GlowDirection ("Afterglow direction (world, toward the sun)", Vector) = (0, 0, 1, 0)
        _StarBrightness ("Stars", Range(0, 4)) = 1
        [HDR] _MoonColor ("Moon", Color) = (0, 0, 0, 1)
        _MoonDirection ("Moon direction (world, toward the moon)", Vector) = (0, 0.6, 0.8, 0)
        _MoonSize ("Moon radius (radians)", Range(0.005, 0.1)) = 0.022
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor, _GlowColor, _MoonColor;
                float4 _GlowDirection, _MoonDirection;
                half _StarBrightness;
                float _MoonSize;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz; // the skybox mesh is centred on the camera: position = view direction
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // One star per lit cell of a 3D grid, jittered, sized to about a pixel at 1080p; a few twinkle.
            half Stars(float3 dir, float cells, float density, float radius)
            {
                float3 p = dir * cells;
                float3 cell = floor(p);
                float h = Hash(cell);
                if (h > density) return 0;
                float3 star = cell + 0.25 + 0.5 * float3(Hash(cell + 11.1), Hash(cell + 23.7), Hash(cell + 37.3));
                float d = length(p - star);
                half twinkle = 0.75 + 0.25 * sin(_Time.y * (1.5 + 3.0 * Hash(cell + 5.5)) + h * 40.0);
                half brightness = lerp(0.3, 1.0, Hash(cell + 51.9));
                return saturate(1.0 - d / radius) * brightness * twinkle;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float up = dir.y;

                half3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(up), 0.45));
                col = lerp(col, _GroundColor.rgb, saturate(-up * 6.0));

                // Afterglow: strongest on the horizon toward the sun, fading up the sky and around to the sides.
                float2 flat = normalize(dir.xz + 1e-5);
                float toward = saturate(dot(flat, normalize(_GlowDirection.xz + 1e-5)));
                col += _GlowColor.rgb * pow(toward, 3.0) * exp(-max(up, 0.0) * 7.0) * step(-0.05, up);

                // Stars fade in above the horizon haze and behind the afterglow.
                half starMask = smoothstep(0.03, 0.25, up) * _StarBrightness;
                half stars = Stars(dir, 220.0, 0.22, 0.2) + 1.6 * Stars(dir, 70.0, 0.05, 0.12);
                col += stars * starMask * (1.0 - saturate(dot(_GlowColor.rgb, half3(0.2126, 0.7152, 0.0722)) * pow(toward, 2.0) * 0.6)) * half3(0.9, 0.95, 1.0);

                // Moon: a limb-darkened disc with a soft halo.
                float3 moonDir = normalize(_MoonDirection.xyz);
                float cosAngle = dot(dir, moonDir);
                float angle = acos(clamp(cosAngle, -1.0, 1.0));
                half disc = smoothstep(_MoonSize, _MoonSize * 0.85, angle);
                half limb = sqrt(saturate(1.0 - pow(angle / _MoonSize, 2.0)));
                half halo = exp(-angle / (_MoonSize * 3.0)) * 0.08 + exp(-angle * 9.0) * 0.015;
                col += _MoonColor.rgb * (disc * lerp(0.6, 1.0, limb) + halo);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
