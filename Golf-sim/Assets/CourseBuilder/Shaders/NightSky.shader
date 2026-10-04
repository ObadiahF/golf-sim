// Skybox for dusk and night (SkyLighting). Layers, back to front:
//  - a photographed sky (_SkyTex: an equirectangular HDRI, turned by _SkyTexRotation) or, without one, a zenith to
//    horizon gradient with an afterglow toward the set sun;
//  - a band of light along the horizon that ends in exactly the fog colour (_HorizonColor), so the course's fogged
//    distance and the sky meet without a seam;
//  - crisp stars hashed from the view direction: sizes, colours and brightness vary, they thin out and twinkle more
//    toward the horizon (a 2K photo's own stars are soft, these stay sharp at any screen size);
//  - a few thin clouds lit by the moon, and the moon itself: a limb-darkened disc with maria and a small tight glow.
// A skybox is drawn behind everything and never fogged.
Shader "GolfSim/NightSky"
{
    Properties
    {
        [NoScaleOffset] _SkyTex ("Sky photo (equirectangular HDR)", 2D) = "black" {}
        _SkyTexExposure ("Sky photo exposure (0 = gradient only)", Float) = 0
        _SkyTexRotation ("Sky photo turn (degrees)", Float) = 0
        _SkyTexContrast ("Sky photo contrast (power)", Range(0.5, 2)) = 1
        _ZenithColor ("Zenith", Color) = (0.01, 0.015, 0.05, 1)
        _HorizonColor ("Horizon = fog colour", Color) = (0.05, 0.07, 0.14, 1)
        _HorizonGlow ("Horizon band strength", Range(0, 4)) = 1
        [HDR] _GlowColor ("Afterglow", Color) = (0, 0, 0, 1)
        _GlowDirection ("Afterglow direction (world, toward the sun)", Vector) = (0, 0, 1, 0)
        _StarBrightness ("Stars", Range(0, 4)) = 1
        [HDR] _MoonColor ("Moon", Color) = (0, 0, 0, 1)
        _MoonDirection ("Moon direction (world, toward the moon)", Vector) = (0, 0.6, 0.8, 0)
        _MoonSize ("Moon radius (radians)", Range(0.005, 0.1)) = 0.03
        _CloudColor ("Cloud colour", Color) = (0.12, 0.14, 0.2, 1)
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.25
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

            TEXTURE2D(_SkyTex);
            SAMPLER(sampler_SkyTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GlowColor, _MoonColor, _CloudColor;
                float4 _GlowDirection, _MoonDirection;
                float _SkyTexExposure, _SkyTexRotation, _SkyTexContrast, _HorizonGlow, _StarBrightness, _MoonSize, _CloudCover;
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

            float Hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash2(i), Hash2(i + float2(1, 0)), u.x), lerp(Hash2(i + float2(0, 1)), Hash2(i + 1.0), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 5; i++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            // One layer of stars: at most one per cell of a 3D grid over the sky. `pixel` is the view's size of a pixel
            // in cell units, so a star is never thinner than about a pixel (no shimmering at a distance, crisp up close).
            half3 Stars(float3 dir, float cells, float density, float pixel, float up)
            {
                float3 p = dir * cells;
                float3 cell = floor(p);
                float h = Hash(cell);
                if (h > density) return 0;
                float3 star = cell + 0.2 + 0.6 * float3(Hash(cell + 11.1), Hash(cell + 23.7), Hash(cell + 37.3));
                float bright = pow(Hash(cell + 51.9), 6.0) * 5.0 + 0.15;           // a few bright ones, many faint
                float radius = pixel * lerp(0.7, 1.6, saturate(bright / 3.0));
                float d = length(p - star);
                float core = exp(-d * d / (radius * radius));
                float temp = Hash(cell + 71.3);                                       // blue-white to orange
                half3 tint = temp < 0.2 ? half3(0.75, 0.85, 1.25) : temp > 0.85 ? half3(1.25, 0.95, 0.7) : half3(1.0, 1.0, 1.0);
                float twinkleAmount = lerp(0.45, 0.1, saturate(up * 2.0));          // the thick air low down twinkles more
                float twinkle = 1.0 - twinkleAmount * (0.5 + 0.5 * sin(_Time.y * (2.0 + 5.0 * Hash(cell + 5.5)) + h * 60.0));
                return tint * core * bright * twinkle;
            }

            // The moon: a crisp disc with darker maria and limb darkening, a tight glow, and a faint wide halo.
            half3 Moon(float3 dir, float3 moonDir, float pixelAngle, out half cover)
            {
                float angle = acos(clamp(dot(dir, moonDir), -1.0, 1.0));
                float r = angle / _MoonSize;
                cover = (1.0 - smoothstep(1.0 - pixelAngle / _MoonSize, 1.0, r)) * step(1e-4, max(_MoonColor.r, max(_MoonColor.g, _MoonColor.b)));
                float3 right = normalize(cross(float3(0, 1, 0), moonDir));
                float3 upAxis = cross(moonDir, right);
                float2 disc = float2(dot(dir, right), dot(dir, upAxis)) / _MoonSize;
                float maria = smoothstep(0.48, 0.68, Fbm(disc * 2.2 + 3.7));
                float craters = Noise(disc * 14.0) * 0.12;
                float limb = pow(saturate(1.0 - r * r), 0.25);
                half3 surface = (1.0 - 0.42 * maria - craters) * lerp(0.55, 1.0, limb);
                half glow = exp(-max(r - 1.0, 0.0) * 2.5) * 0.18 * (1.0 - cover) + exp(-angle * 9.0) * 0.025;
                return _MoonColor.rgb * (surface * cover + glow);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float up = dir.y;
                float pixelAngle = max(length(fwidth(dir)), 1e-5);

                // The sky behind: the photo, or the gradient with an afterglow toward the set sun.
                half3 col;
                if (_SkyTexExposure > 0.0)
                {
                    float turn = radians(_SkyTexRotation);
                    float s = sin(turn), c = cos(turn);
                    float3 d = float3(dir.x * c - dir.z * s, max(dir.y, 0.0), dir.x * s + dir.z * c);
                    d = normalize(d);
                    float2 uv = float2(atan2(d.x, d.z) / (2.0 * PI) + 0.5, asin(d.y) / PI + 0.5);
                    // Contrast above 1 darkens the photo's faint background more than its Milky Way and stars.
                    col = pow(max(SAMPLE_TEXTURE2D_LOD(_SkyTex, sampler_SkyTex, uv, 0).rgb, 0.0), _SkyTexContrast) * _SkyTexExposure;
                }
                else col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(up), 0.45));
                float2 flat = normalize(dir.xz + 1e-5);
                float toward = saturate(dot(flat, normalize(_GlowDirection.xz + 1e-5)));
                col += _GlowColor.rgb * pow(toward, 3.0) * exp(-max(up, 0.0) * 7.0);

                // Stars, then clouds over them, then the moon (clouds pass in front of it too).
                float aboveHorizon = smoothstep(0.0, 0.3, up);
                half3 stars = Stars(dir, 300.0, 0.006, pixelAngle * 300.0, up)
                            + Stars(dir, 110.0, 0.008, pixelAngle * 110.0, up) * 1.5;
                col += stars * _StarBrightness * aboveHorizon;

                half moonCover;
                float3 moonDir = normalize(_MoonDirection.xyz);
                half3 moon = Moon(dir, moonDir, pixelAngle, moonCover);
                col = lerp(col, 0, moonCover) + moon;

                if (_CloudCover > 0.0 && up > 0.0)
                {
                    float2 uv = dir.xz / (up + 0.12) * 1.6 + _Time.y * float2(0.004, 0.0015);
                    float n = Fbm(uv) * 0.75 + Fbm(uv * 3.1 + 5.2) * 0.25;
                    float cloud = smoothstep(1.0 - _CloudCover, 1.25 - _CloudCover, n) * smoothstep(0.0, 0.15, up);
                    float silver = pow(saturate(dot(dir, moonDir)), 6.0) * 2.0 + 0.35;  // moonlit edges toward the moon
                    col = lerp(col, _CloudColor.rgb * silver + _MoonColor.rgb * 0.02, cloud * 0.8);
                }

                // The horizon: a band of light (sky glow, the town over the hill) that ends in the fog colour.
                col += _HorizonColor.rgb * _HorizonGlow * exp(-max(up, 0.0) * 9.0);
                col = lerp(col, _HorizonColor.rgb, exp(-max(up, 0.0) * 45.0));
                col = lerp(col, _HorizonColor.rgb, saturate(-up * 20.0)); // below the horizon: only ever seen behind fog
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
