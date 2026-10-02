using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Green speed and putt distance on flat ground: the one place the sim turns ball speed into roll distance.
    /// The phone app has the same function with the same constants (Golf-app Model/PuttModel.swift), so its
    /// power meter and the sim agree; change both together.
    ///
    /// A Stimpmeter releases the ball at 1.83 m/s and the green's Stimp reading is how many feet it rolls.
    /// A rolling ball slows at a constant rate (rolling resistance x g, BallPhysics.Roll), so distance grows
    /// with speed squared: distance = stimp (m) x (speed / 1.83)². The sim's green (rolling 0.05) is Stimp 11.2.
    /// </summary>
    public static class PuttModel
    {
        /// <summary>Ball speed leaving a Stimpmeter ramp, m/s (6 ft/s).</summary>
        public const float StimpReleaseSpeed = 1.83f;
        public const float MetersPerFoot = 0.3048f;
        /// <summary>A putt aimed to finish this far past the hole (the preview and the power meter target).</summary>
        public const float Overshoot = 0.4f;

        /// <summary>Stimp reading (feet) of a surface with this rolling resistance (fraction of g).</summary>
        public static float StimpFeet(float rolling) =>
            StimpReleaseSpeed * StimpReleaseSpeed / (2f * Mathf.Max(1e-4f, rolling) * BallPhysicsSettings.Gravity) / MetersPerFoot;

        /// <summary>Metres a putt at this ball speed (m/s) rolls on a flat green of this Stimp (feet).</summary>
        public static float RollDistance(float ballSpeed, float stimpFeet)
        {
            float ratio = Mathf.Max(0f, ballSpeed) / StimpReleaseSpeed;
            return stimpFeet * MetersPerFoot * ratio * ratio;
        }

        /// <summary>Ball speed (m/s) that rolls this many metres on a flat green of this Stimp (feet).</summary>
        public static float SpeedFor(float distance, float stimpFeet) =>
            StimpReleaseSpeed * Mathf.Sqrt(Mathf.Max(0f, distance) / Mathf.Max(0.1f, stimpFeet * MetersPerFoot));

        /// <summary>The Stimp of the green in these settings.</summary>
        public static float GreenStimp(BallPhysicsSettings settings) => StimpFeet(settings.For("green").rolling);

        /// <summary>The putter's shot at this ball speed (the bag's putter launch, no spin).</summary>
        public static ShotData PuttAt(float ballSpeed)
        {
            var shot = Clubs.Find(Clubs.Putter).shot;
            shot.ballSpeed = ballSpeed;
            return shot;
        }
    }
}
