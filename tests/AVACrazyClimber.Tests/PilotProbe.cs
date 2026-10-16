using AVACrazyClimber.Engine;

namespace AVACrazyClimber.Tests;

/// <summary>How the computer climber gets on, building by building (set CLIMBER_PROBE to run it, for tuning; CLIMBER_PROBE=lazy for one that just holds up).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("CLIMBER_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "climber-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 8 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 300 < 20 ? 1 : 0, Up: true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Climb) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Falling && last != Phase.Falling) deaths[$"B{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"B{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Roof && last != Phase.Roof) waves.Add($"B{wave}:{(t - start) / 60:0}s");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: building {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   falls " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   floor {g.Climber.Floor}/{g.Floors} lives {g.Lives}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
