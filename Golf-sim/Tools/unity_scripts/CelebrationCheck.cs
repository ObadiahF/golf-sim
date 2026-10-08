// Dev check (Edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/CelebrationCheck.cs --entry CelebrationCheck.Run
// The strike sounds' families and qualities (StrikeSound) for typical and mishit shots of each club, and which holed
// shots get which celebration (RoundDirector.CelebrationFor).
using System.Collections.Generic;
using GolfSim.Ball;
using GolfSim.Game;
using UnityEngine;

public static class CelebrationCheck
{
    public static string Run()
    {
        var fails = new List<string>();
        void Expect(string what, object got, object want)
        {
            if (!Equals(got, want)) fails.Add($"{what}: got {got}, want {want}");
        }

        // Every club's typical shot is a pure strike of its family.
        foreach (var c in Clubs.Bag)
        {
            var family = StrikeSound.Family(c, c.shot.ballSpeed);
            var q = StrikeSound.Quality(c.shot, c, family);
            Expect($"{c.name} typical", q, StrikeQuality.Pure);
        }
        Expect("Driver family", StrikeSound.Family(Clubs.Find("Driver"), 70f), SoundId.StrikeDriver);
        Expect("3 Wood family", StrikeSound.Family(Clubs.Find("3 Wood"), 65f), SoundId.StrikeWood);
        Expect("4 Hybrid family", StrikeSound.Family(Clubs.Find("4 Hybrid"), 60f), SoundId.StrikeWood);
        Expect("7 Iron family", StrikeSound.Family(Clubs.Find("7 Iron"), 50f), SoundId.StrikeIron);
        Expect("Sand Wedge family", StrikeSound.Family(Clubs.Find("Sand Wedge"), 35f), SoundId.StrikeWedge);
        Expect("a 9 m/s tap with a 7 iron is a putt", StrikeSound.Family(Clubs.Find("7 Iron"), 9f), SoundId.StrikePutter);

        var seven = Clubs.Find("7 Iron");
        ShotData Seven(float mph, float launch, float dir, float back, float side) => ShotData.FromMph(mph, launch, dir, back, side);
        StrikeQuality Q(ShotData s, Club c) => StrikeSound.Quality(s, c, StrikeSound.Family(c, s.ballSpeed));
        Expect("7 iron thin (launch 6°)", Q(Seven(112, 6f, 0, 4000, 0), seven), StrikeQuality.Thin);
        Expect("7 iron toe (big slice spin)", Q(Seven(110, 15f, 2, 6000, 3000), seven), StrikeQuality.Toe);
        Expect("7 iron fat (slow, high)", Q(Seven(75, 22f, 0, 7500, 0), seven), StrikeQuality.Fat);
        Expect("7 iron solid (a bit slow)", Q(Seven(108, 16f, 1, 7000, 400), seven), StrikeQuality.Solid);
        var driver = Clubs.Find("Driver");
        Expect("driver pure", Q(ShotData.FromMph(168, 11f, 1, 2600, 150), driver), StrikeQuality.Pure);
        Expect("driver topped", Q(ShotData.FromMph(120, 3f, 0, 1500, 0), driver), StrikeQuality.Thin);
        var sw = Clubs.Find("Sand Wedge");
        Expect("half sand wedge is not fat", Q(ShotData.FromMph(55, 34f, 0, 7000, 0), sw), StrikeQuality.Pure);
        var putter = Clubs.Find("Putter");
        Expect("straight putt", Q(ShotData.FromMph(5, 1f, 0.3f, 0, 0), putter), StrikeQuality.Pure);
        Expect("pushed putt", Q(ShotData.FromMph(5, 1f, 5f, 0, 0), putter), StrikeQuality.Toe);

        // Celebrations.
        string H(int strokes, int par, string lie, float m) => RoundDirector.CelebrationFor("Obi", strokes, par, lie, m, Color.green)?.headline;
        Expect("ace", H(1, 3, "tee", 150f), "HOLE IN ONE!");
        Expect("albatross", H(2, 5, "fairway", 200f), "ALBATROSS!");
        Expect("eagle putt", H(3, 5, "green", 4f), "EAGLE!");
        Expect("birdie tap-in", H(3, 4, "green", 1f), "BIRDIE!");
        Expect("birdie long putt", H(3, 4, "green", 10f), "BIRDIE BOMB!");
        Expect("chip-in birdie", H(3, 4, "rough", 20f), "CHIP-IN BIRDIE!");
        Expect("chip-in par", H(4, 4, "fringe", 12f), "CHIP-IN!");
        Expect("long par putt", H(4, 4, "green", 9f), "WHAT A PUTT!");
        Expect("par tap-in", H(4, 4, "green", 1f), null);
        Expect("bogey putt", H(5, 4, "green", 3f), null);
        Expect("practice ace", H(0, 0, "tee", 140f), "HOLE IN ONE!");
        Expect("practice putt", H(0, 0, "green", 12f), null);
        var ace = RoundDirector.CelebrationFor("Obi", 1, 3, "tee", 150f, Color.green);
        Expect("ace fireworks", ace.fireworks > 0 && ace.confetti && ace.jingle == SoundId.JingleAce, true);

        return fails.Count == 0 ? "ALL PASS" : "FAILS:\n" + string.Join("\n", fails);
    }
}
