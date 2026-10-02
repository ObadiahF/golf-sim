using System;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Launch conditions for one shot: what a launch monitor measures at impact.
    /// Every input (sliders today, the camera system later) produces one of these.
    /// </summary>
    [Serializable]
    public struct ShotData
    {
        public const float MetersPerSecondPerMph = 0.44704f;

        [Tooltip("Ball speed in m/s.")] public float ballSpeed;
        [Tooltip("Vertical launch angle in degrees.")] public float launchAngle;
        [Tooltip("Horizontal launch direction in degrees; + is right of the target line.")] public float launchDirection;
        [Tooltip("Backspin in rpm.")] public float backspin;
        [Tooltip("Sidespin in rpm; + curves right (fade/slice for a right-hander).")] public float sidespin;

        public float BallSpeedMph => ballSpeed / MetersPerSecondPerMph;
        public float TotalSpin => Mathf.Sqrt(backspin * backspin + sidespin * sidespin);
        /// <summary>Spin axis tilt in degrees, as launch monitors report it; + tilts right.</summary>
        public float SpinAxis => Mathf.Atan2(sidespin, backspin) * Mathf.Rad2Deg;

        public static ShotData FromMph(float mph, float launch, float direction, float backspin, float sidespin) =>
            new ShotData
            {
                ballSpeed = mph * MetersPerSecondPerMph,
                launchAngle = launch,
                launchDirection = direction,
                backspin = backspin,
                sidespin = sidespin,
            };

        /// <summary>From total spin and spin axis, the way most launch monitors report spin.</summary>
        public static ShotData FromSpinAxis(float speed, float launch, float direction, float totalSpin, float spinAxis)
        {
            float axis = spinAxis * Mathf.Deg2Rad;
            return new ShotData
            {
                ballSpeed = speed,
                launchAngle = launch,
                launchDirection = direction,
                backspin = totalSpin * Mathf.Cos(axis),
                sidespin = totalSpin * Mathf.Sin(axis),
            };
        }

        /// <summary>TrackMan PGA Tour averages (ball speed, launch, spin), plus a putt.</summary>
        public static readonly (string name, ShotData shot)[] Presets =
        {
            ("Driver", FromMph(167, 10.9f, 0, 2686, 0)),
            ("3-wood", FromMph(158, 9.2f, 0, 3655, 0)),
            ("5-iron", FromMph(132, 12.1f, 0, 5361, 0)),
            ("7-iron", FromMph(120, 16.3f, 0, 7097, 0)),
            ("9-iron", FromMph(109, 20.4f, 0, 8647, 0)),
            ("PW", FromMph(102, 24.2f, 0, 9304, 0)),
            ("Putt", FromMph(5, 1f, 0, 0, 0)),
        };
    }
}
