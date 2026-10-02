using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// How the lie changes a shot at impact: the ball speed and spin kept and the launch degrees added
    /// (from BallPhysicsSettings.lies). Applied once, in GolfBall.Hit, so every input gets it.
    /// </summary>
    public readonly struct LieEffect
    {
        public readonly string surface;
        public readonly float speed;
        public readonly float spin;
        public readonly float launch;

        public LieEffect(string surface, float speed, float spin, float launch)
        {
            this.surface = surface;
            this.speed = speed;
            this.spin = spin;
            this.launch = launch;
        }

        public static LieEffect Clean(string surface) => new LieEffect(surface, 1f, 1f, 0f);

        public ShotData Apply(ShotData shot)
        {
            shot.ballSpeed *= speed;
            shot.backspin *= spin;
            shot.sidespin *= spin;
            shot.launchAngle += launch;
            return shot;
        }

        /// <summary>"Rough −12%" (ball speed lost), or just "Fairway" for a clean lie.</summary>
        public string Label => Describe(surface, speed);

        public static string Describe(string surface, float speedKept)
        {
            string name = string.IsNullOrEmpty(surface) ? "" : char.ToUpperInvariant(surface[0]) + surface.Substring(1);
            int loss = Mathf.RoundToInt((1f - speedKept) * 100f);
            return loss > 0 ? $"{name} −{loss}%" : name;
        }
    }
}
