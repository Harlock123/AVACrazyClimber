namespace AVACrazyClimber.Engine;

/// <summary>
/// The computer climber, for the demonstration (and the tests). Something falling down its column, it steps
/// aside if it can, or braces; the gargoyle winding up, a window it's holding starting to shut, it braces; a
/// sign above about to spark, it waits. Otherwise it climbs, moving across to a column whose window above is
/// open when its own isn't.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var k = g.Climber;
        if (g.Phase != Phase.Climb || !k.Alive) { Doing = "waiting"; return default; }
        if (k.ToCol >= 0) { Doing = "shifting"; return default; }
        int col = k.Col;

        // Something coming down this column soon.
        Thing? Incoming(int c) => g.Things.FirstOrDefault(t => !t.Bounced && Math.Abs(t.Col - c) < 0.6 && t.Y > k.Y - 0.2 && (t.Y - k.Y - 1) / t.Vy < 0.75);
        if (Incoming(col) != null)
        {
            if (k.Lift == 0)
                foreach (int d in new[] { -1, 1 })
                {
                    int to = col + d;
                    if (to >= 0 && to < Field.Cols && g.Holdable(to, k.Floor) && Incoming(to) == null && !Shutting(g, to, k.Floor) && g.Things.All(t => Math.Abs(t.Col - col) > 0.6 || (t.Y - k.Y - 1) / t.Vy > 0.35))
                    {
                        Doing = "sidestep";
                        return new Controls(d);
                    }
                }
            Doing = "bracing";
            return new Controls(Brace: true);
        }
        if (g.Gargoyle is { } gg && gg.WindUp + gg.Swipe > 0 && Math.Abs(k.Y - gg.Floor) <= 1.6 && GameEngine.Reaches(gg, col)) { Doing = "bracing"; return new Controls(Brace: true); }
        if (Shutting(g, col, k.Floor) || (k.Lift > 0 && Shutting(g, col, k.Floor + 1))) { Doing = "bracing"; return new Controls(Brace: true); }

        // A sign about to spark on the floor above (or this one): wait it out, or move off it.
        foreach (var s in g.Signs)
        {
            bool soon = s.Flicker > 0 || s.Spark > 0 || s.Timer < 0.6;
            if (!soon || !s.Covers(col)) continue;
            if (s.Floor == k.Floor && k.Lift == 0)
            {
                int d = s.From > 0 && (col - s.From) <= (s.To - col) ? -1 : 1, to = d < 0 ? s.From - 1 : s.To + 1;
                if (to >= 0 && to < Field.Cols && g.Holdable(col + d, k.Floor)) { Doing = "off the sign"; return new Controls(d); }
            }
            if (s.Floor == k.Floor + 1 || (s.Floor == k.Floor && k.Lift > 0)) { Doing = "waiting"; return new Controls(Up: false); }
        }

        // Up, if the window above takes a hand; else across to the nearest column whose does.
        if (g.Holdable(col, k.Floor + 1) && !Shutting(g, col, k.Floor + 1)) { Doing = "climbing"; return new Controls(Up: true); }
        if (k.Lift > 0) { Doing = "waiting"; return default; }
        for (int dist = 1; dist < Field.Cols; dist++)
            foreach (int d in new[] { -1, 1 })
            {
                int to = col + d * dist;
                if (to < 0 || to >= Field.Cols || !g.Holdable(to, k.Floor + 1)) continue;
                // Can it get there along this floor?
                bool path = true;
                for (int c = col + d; c != to + d; c += d) if (!g.Holdable(c, k.Floor)) path = false;
                if (!path) continue;
                if (!g.Holdable(col + d, k.Floor) || Shutting(g, col + d, k.Floor)) continue;
                Doing = "across";
                return new Controls(d);
            }
        Doing = "stuck";
        return default;
    }

    private static bool Shutting(GameEngine g, int col, int floor) => g.WindowAt(col, floor) is { } w && (w.State == Pane.Closing || (w.State == Pane.Open && !w.Fixed && w.Timer / g.Busy < 0.25));
}
