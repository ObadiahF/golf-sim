using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Interface sounds: a tick for each D-pad move, a confirm for Select and a softer one for Back (heard from any
    /// input path through NavInput, before anything handles the key), the swoosh of the turn banner and a sting when
    /// a replay starts.
    /// </summary>
    public class UiSounds : MonoBehaviour
    {
        const int ListenFirst = 1000; // above every handler; it never consumes the key

        RoundDirector director;
        ReplayDirector replay;

        void OnEnable()
        {
            NavInput.Register(OnNav, ListenFirst);
            director = GetComponent<RoundDirector>();
            if (director) director.TurnStarted += OnTurn;
            replay = GetComponent<ReplayDirector>();
            if (replay) replay.Playing += OnReplay;
        }

        void OnDisable()
        {
            NavInput.Unregister(OnNav);
            if (director) director.TurnStarted -= OnTurn;
            if (replay) replay.Playing -= OnReplay;
        }

        bool OnNav(NavKey key)
        {
            GameAudio.Play(key switch
            {
                NavKey.Select => SoundId.UiSelect,
                NavKey.Back => SoundId.UiBack,
                _ => SoundId.UiMove,
            });
            return false;
        }

        void OnTurn(TurnMessage turn) => GameAudio.Play(SoundId.UiSwoosh);

        void OnReplay(bool starting)
        {
            if (starting) GameAudio.Play(SoundId.ReplaySting);
        }
    }
}
