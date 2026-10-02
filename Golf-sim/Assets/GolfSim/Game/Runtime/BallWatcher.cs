using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolfSim.Game
{
    /// <summary>
    /// Base for presentation components that live with the game session (sound, replay) and follow the hole scene's
    /// GolfBall: Bind when a hole scene with a ball loads, Unbind when it goes. Gameplay code never calls them.
    /// </summary>
    public abstract class BallWatcher : MonoBehaviour
    {
        protected GolfBall Ball { get; private set; }
        /// <summary>The hole being played (looked up when needed: a round can rebuild it after the scene loads).</summary>
        protected HoleInfo Hole => hole ? hole : hole = FindAnyObjectByType<HoleInfo>();

        HoleInfo hole;

        protected virtual void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Rebind();
        }

        protected virtual void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SetBall(null);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rebind();

        /// <summary>Finds the scene's ball again (a scene load, or a ball that was destroyed).</summary>
        protected void Rebind()
        {
            hole = null;
            SetBall(Hole ? FindAnyObjectByType<GolfBall>() : null);
        }

        void SetBall(GolfBall ball)
        {
            if (Ball == ball) return;
            if (Ball) Unbind(Ball);
            Ball = ball;
            if (ball) Bind(ball);
        }

        protected abstract void Bind(GolfBall ball);
        protected abstract void Unbind(GolfBall ball);
    }
}
