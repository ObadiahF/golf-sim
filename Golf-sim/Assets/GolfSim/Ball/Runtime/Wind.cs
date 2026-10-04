using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// A steady wind: its speed and the compass direction it comes from (degrees clockwise from north, +z), as a
    /// forecast says it. The ball flies through it (BallPhysics.Fly takes Velocity); the HUD shows it against the aim.
    /// </summary>
    public readonly struct Wind
    {
        public readonly float mph;
        /// <summary>Where it blows from: 0 = a north wind (blowing toward -z).</summary>
        public readonly float from;

        public Wind(float mph, float from)
        {
            this.mph = Mathf.Max(0f, mph);
            this.from = Mathf.Repeat(from, 360f);
        }

        public static readonly Wind Calm = new Wind(0f, 0f);

        public bool IsCalm => mph < 0.5f;
        /// <summary>The direction it blows toward, degrees clockwise from north.</summary>
        public float Heading => Mathf.Repeat(from + 180f, 360f);
        public float MetersPerSecond => mph * ShotData.MetersPerSecondPerMph;
        public Vector3 Velocity => Quaternion.Euler(0f, Heading, 0f) * Vector3.forward * MetersPerSecond;
        /// <summary>0..1 for a flag: limp when calm, flying straight out at 25 mph.</summary>
        public float FlagStrength => Mathf.Clamp01(0.15f + mph / 25f);

        /// <summary>
        /// Where it blows relative to a shot along `aim`, degrees clockwise: 0 helping (straight down the aim), 90 left to
        /// right, 180 straight into the player's face.
        /// </summary>
        public float RelativeTo(Vector3 aim) => Mathf.Repeat(Heading - Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, 360f);

        /// <summary>
        /// A day's wind for a hole: mostly light (calm 15 %, under 8 mph 40 %, 8-15 mph 33 %, gusty 15-22 mph 12 %) from
        /// anywhere; `scale` multiplies the speed (0 = no wind).
        /// </summary>
        public static Wind Random(System.Random rng, float scale = 1f)
        {
            double band = rng.NextDouble(), t = rng.NextDouble();
            float mph = band < 0.15 ? 0f : band < 0.55 ? Mathf.Lerp(2f, 8f, (float)t) : band < 0.88 ? Mathf.Lerp(8f, 15f, (float)t) : Mathf.Lerp(15f, 22f, (float)t);
            return new Wind(Mathf.Round(mph * scale), (float)(rng.NextDouble() * 360.0));
        }
    }
}
