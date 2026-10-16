using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVACrazyClimber.Engine;
using AVACrazyClimber.Views;
using Controls = AVACrazyClimber.Engine.Controls;

namespace AVACrazyClimber.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set CLIMBER_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avacrazyclimber-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("CLIMBER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVACrazyClimber-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Up = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98200, 7);
        g.HighScores.Insert("AVA", 71400, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A building, and the computer at work on it until something worth showing.</summary>
    private static void Climb(int building, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 90)
    {
        var (w, c, g) = Open(seed);
        g.Go(building);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Climb && g.Climber.Alive && g.Climber.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_street() => Climb(1, "street", g => g.Climber.Floor >= 2 && g.Climber.Floor <= 4 && g.Climber.Lift > 0.2);

    [AvaloniaFact]
    public void Something_falling() => Climb(1, "falling", g => g.Things.Any(t => Math.Abs(t.Y - g.Climber.Y) < 3.5 && t.Y > g.Climber.Y + 1) && g.Windows.Cast<Sash>().Any(x => x.State == Pane.Closing));

    [AvaloniaFact]
    public void The_gargoyle() => Climb(2, "gargoyle", g => g.Gargoyle is { } gg && (gg.WindUp > 0.2 || gg.Swipe > 0) && Math.Abs(g.Climber.Floor - gg.Floor) <= 2, seconds: 150);

    [AvaloniaFact]
    public void A_neon_sign() => Climb(3, "sign", g => g.Signs.Any(s => (s.Flicker > 0 || s.Spark > 0) && Math.Abs(s.Floor - g.Climber.Floor) <= 2) && g.Vultures.Count > 0, seconds: 150);

    [AvaloniaFact]
    public void Bracing() => Climb(4, "bracing", g => g.Climber.Bracing && g.Things.Any(t => Math.Abs(t.Y - g.Climber.Y) < 2 && t.Y > g.Climber.Y), seconds: 150);

    [AvaloniaFact]
    public void The_roof()
    {
        var (w, c, g) = Open(4);
        g.Go(1);
        g.SetLives(20);
        g.Place(3, g.Floors - 3);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Roof && g.PhaseTime > 2.2, 60));
        Shot(w, c, "roof");
    }
}
