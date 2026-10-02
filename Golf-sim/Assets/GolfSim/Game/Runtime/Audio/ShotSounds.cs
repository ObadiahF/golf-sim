using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// The ball's sounds: the strike (driver, iron or putter, louder and higher the faster the ball), landings and
    /// bounces by surface, trees, rocks, the splash and the cup. Live from the ball's events (bounces after the first
    /// landing are spotted from its motion), and again from the recording when the replay plays it.
    /// </summary>
    public class ShotSounds : BallWatcher
    {
        const float MinContactSpeed = 1.2f; // m/s; softer touches make no sound
        const float BounceGap = 0.12f;      // s; one sound per bounce

        Vector3 last, lastVelocity;
        float lastContact = -1f;
        bool tracking;

        protected override void OnEnable()
        {
            base.OnEnable();
            var replay = GetComponent<ReplayDirector>();
            if (replay) replay.EventPlayed += OnReplayEvent;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            var replay = GetComponent<ReplayDirector>();
            if (replay) replay.EventPlayed -= OnReplayEvent;
        }

        protected override void Bind(GolfBall ball)
        {
            ball.ShotStarted += OnShotStarted;
            ball.Landed += OnLanded;
            ball.HitObstacle += OnHitObstacle;
            ball.ShotFinished += OnShotFinished;
        }

        protected override void Unbind(GolfBall ball)
        {
            ball.ShotStarted -= OnShotStarted;
            ball.Landed -= OnLanded;
            ball.HitObstacle -= OnHitObstacle;
            ball.ShotFinished -= OnShotFinished;
            tracking = false;
        }

        // ---- live ----

        void OnShotStarted(GolfBall ball)
        {
            var director = RoundDirector.Instance;
            Strike(director ? director.Club : "", ball.LastShot.ballSpeed, ball.LaunchPoint, flat: false);
            last = ball.transform.position;
            lastVelocity = Vector3.zero;
            lastContact = -1f;
            tracking = true;
        }

        void OnLanded(GolfBall ball)
        {
            if (ball.Status is BallStatus.InWater or BallStatus.OutOfBounds) return; // the finish says it
            float speed = lastVelocity.sqrMagnitude > 0f ? lastVelocity.magnitude : 15f; // no frames yet (a tool driving the ball)
            Contact(CourseSurface.At(ball.transform.position), speed, ball.transform.position, flat: false);
            lastContact = Time.time;
        }

        void OnHitObstacle(GolfBall ball, ObstacleHit hit) =>
            Obstacle(hit.canopy ? "leaves" : hit.IsRock ? "rock" : "trunk", hit.impactSpeed, hit.point, flat: false);

        void OnShotFinished(GolfBall ball)
        {
            tracking = false;
            Finish(ball.Status, ball.transform.position, Hole ? Hole.PinWorld : ball.transform.position, flat: false);
        }

        /// <summary>Bounces after the first landing: the ball was coming down near the ground and now goes up.</summary>
        void LateUpdate()
        {
            if (!tracking || !Ball || Time.deltaTime <= 0f) return;
            var p = Ball.transform.position;
            var v = (p - last) / Time.deltaTime;
            bool nearGround = p.y - CourseSurface.GroundAt(p) < BallPhysicsSettings.Radius + 0.05f;
            if (lastContact >= 0f && Ball.Status == BallStatus.Flying && lastVelocity.y < -MinContactSpeed && v.y > 0f && nearGround &&
                Time.time - lastContact > BounceGap)
            {
                Contact(CourseSurface.At(p), lastVelocity.magnitude, p, flat: false);
                lastContact = Time.time;
            }
            last = p;
            lastVelocity = v;
        }

        // ---- replay ----

        void OnReplayEvent(ShotEvent e, ShotRecording rec)
        {
            switch (e.kind)
            {
                case ShotEventKind.Strike: Strike(rec.setup.club, e.speed, e.position, flat: true); break;
                case ShotEventKind.Contact: Contact(e.surface, e.speed, e.position, flat: true); break;
                case ShotEventKind.Obstacle: Obstacle(e.surface, e.speed, e.position, flat: true); break;
                case ShotEventKind.Holed: Finish(BallStatus.Holed, e.position, rec.pin, flat: true); break;
                case ShotEventKind.Water: Finish(BallStatus.InWater, e.position, rec.pin, flat: true); break;
            }
        }

        // ---- what each sounds like (the same live and in the replay) ----

        /// <summary>Driver thwack, crisp iron, putter tap: by the club, louder and a little higher with ball speed.</summary>
        static void Strike(string club, float ballSpeed, Vector3 at, bool flat)
        {
            var c = Clubs.Find(club);
            bool putt = c.IsPutter || ballSpeed < 12f;
            bool wood = !putt && (c.name == "Driver" || c.name.Contains("Wood"));
            var id = putt ? SoundId.StrikePutter : wood ? SoundId.StrikeDriver : SoundId.StrikeIron;
            float full = putt ? 10f : wood ? 75f : 62f; // m/s of a full swing with this club
            float k = Mathf.Clamp01(ballSpeed / full);
            GameAudio.Play(id, at, Mathf.Lerp(putt ? 0.25f : 0.45f, 1f, k), Mathf.Lerp(0.94f, 1.04f, k), flat);
        }

        static void Contact(string surface, float speed, Vector3 at, bool flat)
        {
            if (speed < MinContactSpeed || surface is "water" or "") return;
            var id = CourseSurface.IsSand(surface) ? SoundId.LandSand : CourseSurface.IsGreen(surface) ? SoundId.LandGreen : SoundId.LandGrass;
            float k = Mathf.Clamp01(speed / 28f);
            GameAudio.Play(id, at, Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(k)), Mathf.Lerp(1.08f, 0.94f, k), flat);
        }

        static void Obstacle(string what, float speed, Vector3 at, bool flat)
        {
            var id = what == "leaves" ? SoundId.TreeLeaves : what == "rock" ? SoundId.Rock : SoundId.TreeTrunk;
            float k = Mathf.Clamp01(speed / 30f);
            GameAudio.Play(id, at, Mathf.Lerp(0.35f, 1f, k), Mathf.Lerp(0.95f, 1.05f, k), flat);
        }

        static void Finish(BallStatus status, Vector3 at, Vector3 pin, bool flat)
        {
            if (status == BallStatus.Holed) GameAudio.Play(SoundId.Cup, pin, 1f, 1f, flat);
            else if (status == BallStatus.InWater) GameAudio.Play(SoundId.Water, at, 1f, 1f, flat);
        }
    }
}
