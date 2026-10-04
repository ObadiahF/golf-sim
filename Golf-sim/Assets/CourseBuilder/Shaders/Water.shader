// Ponds and lakes (WaterBuilder). One pass, no textures, so nothing tiles:
//  - ripples: a few wind waves of different length and direction plus drifting noise, as a surface slope;
//  - the bottom seen through the water (the camera's opaque texture, bent by the ripples), fading into the water's own
//    colour with depth (the depth texture), so the shore is soft and deep water is dark;
//  - the sky mirrored by Fresnel (the reflection probe SkyLighting renders, else the scene's), with a sharp glint path
//    toward the sun or the moon (_GolfGlintDirection / _GolfGlintColor, else the main light);
//  - a little foam where the water gets shallow.
// The URP asset must keep its depth and opaque textures on (GolfSim_URP_*). Far water calms down so it never shimmers.
Shader "GolfSim/Water"
{
    Properties
    {
        _ShallowColor ("Shallow tint", Color) = (0.55, 0.78, 0.72, 1)
        _DeepColor ("Deep colour", Color) = (0.045, 0.15, 0.17, 1)
        _Clarity ("Clarity (meters to deep colour)", Float) = 1.6
        _FoamColor ("Foam", Color) = (0.9, 0.93, 0.92, 1)
        _FoamWidth ("Foam width (meters of depth)", Float) = 0.35
        _WaveStrength ("Ripple strength", Range(0, 2)) = 1
        _WaveSpeed ("Ripple speed", Float) = 1
        _Refraction ("Refraction", Range(0, 0.1)) = 0.03
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _Clarity, _FoamWidth, _WaveStrength, _WaveSpeed, _Refraction;
            CBUFFER_END
            float4 _GolfGlintDirection; // SkyLighting: xyz toward the sun or moon, w = 1 when set
            half4 _GolfGlintColor;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1.0), u.x), u.y);
            }

            // Slope of one octave of drifting noise ripples (finite differences), its grid turned so octaves never line up.
            float2 Ripples(float2 p, float scale, float angle, float2 drift, float t)
            {
                float s = sin(angle), c = cos(angle);
                float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y) * scale + drift * t;
                const float e = 0.06;
                float n = Noise(q);
                float2 g = float2(Noise(q + float2(e, 0)) - n, Noise(q + float2(0, e)) - n) / e * scale;
                return float2(c * g.x + s * g.y, -s * g.x + c * g.y); // back to world axes
            }

            // Surface slope (d height / dx, dz): two long, gentle wind swells (deep-water speeds) under three octaves of
            // noise ripples, each turned and drifting its own way, so there is no grid and no repeat to spot.
            float2 Slope(float2 p, float t)
            {
                const float2 dirs[2] = { float2(0.8, 0.6), float2(-0.42, 0.91) };
                const float lengths[2] = { 4.3, 2.6 };
                float2 slope = 0;
                for (int i = 0; i < 2; i++)
                {
                    float k = 2.0 * PI / lengths[i];
                    float phase = dot(dirs[i], p) * k + t * sqrt(9.81 * k);
                    slope += dirs[i] * 0.05 * cos(phase);
                }
                slope += Ripples(p, 0.55, 0.3, float2(0.25, 0.1), t) * 0.09;
                slope += Ripples(p, 1.3, 1.7, float2(-0.2, 0.3), t) * 0.05;
                slope += Ripples(p, 3.1, 2.9, float2(0.4, -0.25), t) * 0.025;
                return slope;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float3 view = GetWorldSpaceNormalizeViewDir(positionWS);
                float distance = length(GetCameraPositionWS() - positionWS);
                float t = _Time.y * _WaveSpeed;
                float calm = lerp(1.0, 0.15, saturate(distance / 350.0)); // far ripples would alias
                float2 slope = Slope(positionWS.xz, t) * _WaveStrength * calm;
                float3 normal = normalize(float3(-slope.x, 1.0, -slope.y));

                // How much water the view crosses here, from the depth texture.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceEye = input.positionCS.w;
                float depth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) - surfaceEye;
                float2 bentUV = uv + normal.xz * _Refraction * saturate(depth);
                float bentDepth = LinearEyeDepth(SampleSceneDepth(bentUV), _ZBufferParams) - surfaceEye;
                if (bentDepth < 0.0) { bentUV = uv; bentDepth = depth; } // what's bent in would be in front of the water
                float water = max(bentDepth, 0.0);

                // The water body: the bottom tinted and fading into the deep colour, lit like the scene.
                Light main = GetMainLight();
                half3 light = main.color * saturate(dot(normal, main.direction)) * 0.6 + SampleSH(normal);
                half3 bottom = SampleSceneColor(bentUV) * lerp(1.0, _ShallowColor.rgb, saturate(water * 1.5));
                half3 body = lerp(bottom, _DeepColor.rgb * light, 1.0 - exp(-water / _Clarity));

                // The sky by Fresnel, and the glint path of the sun or moon.
                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normal, view)), 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-view, normal), positionWS, 0.03, 1.0, uv);
                half3 col = lerp(body, sky, fresnel);
                float3 glintDir = _GolfGlintDirection.w > 0.5 ? normalize(_GolfGlintDirection.xyz) : main.direction;
                half3 glintColor = _GolfGlintDirection.w > 0.5 ? _GolfGlintColor.rgb : main.color;
                float nh = saturate(dot(normal, normalize(glintDir + view)));
                col += glintColor * (pow(nh, 1500.0) * 12.0 + pow(nh, 200.0) * 0.12) * calm * smoothstep(-0.05, 0.05, glintDir.y);

                // Foam in the shallows, broken up by noise, lit like the scene.
                float shore = 1.0 - saturate(water / _FoamWidth);
                float foamNoise = Noise(positionWS.xz * 2.2 + t * 0.15) * 0.6 + Noise(positionWS.xz * 6.0 - t * 0.3) * 0.4;
                half foam = smoothstep(0.45, 0.75, shore * 1.25 - foamNoise * 0.6 + 0.2) * shore;
                col = lerp(col, _FoamColor.rgb * (SampleSH(float3(0, 1, 0)) + main.color * 0.5), foam * 0.75);

                // The very edge melts into the bank.
                col = lerp(SampleSceneColor(uv), col, saturate(depth / 0.08));
                col = MixFog(col, input.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
