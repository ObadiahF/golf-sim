// Wind for the trees that aren't SpeedTrees (the broadleaves and palms, on GolfSim/Foliage): each tree leans and
// sways as a whole, more the higher up (a bend that grows with the square of the height above its base, so the trunk
// stays planted), with a slower gust over it and a little crosswise rock, and its leaves flutter on top. Every tree
// gets its own phase from where it stands. The hole's wind comes in as a global (FoliageWind.cs):
//   _GolfWind.xy  the direction it blows toward (world x, z), unit length
//   _GolfWind.z   its strength, 0 (calm) to 1 (a strong wind); calm trees still breathe a little
#ifndef GOLFSIM_FOLIAGE_SWAY_INCLUDED
#define GOLFSIM_FOLIAGE_SWAY_INCLUDED

float4 _GolfWind;

float3 FoliageSway(float3 positionOS)
{
    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
    float3 p = TransformObjectToWorld(positionOS);
    float h = max(0.0, p.y - origin.y);

    float2 dir = dot(_GolfWind.xy, _GolfWind.xy) > 0.01 ? _GolfWind.xy : float2(0.7071, 0.7071);
    float strength = saturate(_GolfWind.z);
    float t = _Time.y;
    float phase = dot(origin.xz, float2(0.137, 0.173));

    // Whole tree: a steady lean downwind, swaying with it, and gusts that come and go.
    float gust = 0.55 + 0.45 * sin(t * 0.45 + phase * 0.6) * sin(t * 0.21 + phase * 1.3);
    float sway = sin(t * (1.1 + 0.25 * strength) + phase);
    float bend = h * h * 0.0022 * (strength * 0.7 * gust + (0.12 + 0.45 * strength) * sway);
    float2 across = float2(-dir.y, dir.x) * h * h * 0.0006 * (0.3 + strength) * sin(t * 1.7 + phase * 2.1);
    p.xz += dir * bend + across;
    p.y -= abs(bend) * h * 0.02; // bending pulls the top down a touch

    // Leaves: a quick flutter that varies from leaf to leaf, only up in the crown.
    float leaf = sin(t * (5.0 + 3.0 * strength) + dot(p, float3(1.9, 1.3, 2.3))) *
                 sin(t * 2.3 + dot(p, float3(0.7, 2.1, 1.1)));
    p += float3(dir.x, 0.4, dir.y) * leaf * (0.015 + 0.05 * strength) * saturate(h / 3.0);

    return TransformWorldToObject(p);
}

// A pass's vertex function with the sway applied first (the pass's own Attributes and Varyings; `position` names
// its object-space position field: positionOS, or position in DepthOnlyPass).
#define FOLIAGE_VERTEX_FIELD(name, original, position) \
    Varyings name(Attributes input) \
    { \
        UNITY_SETUP_INSTANCE_ID(input); \
        input.position.xyz = FoliageSway(input.position.xyz); \
        return original(input); \
    }
#define FOLIAGE_VERTEX(name, original) FOLIAGE_VERTEX_FIELD(name, original, positionOS)

#endif
