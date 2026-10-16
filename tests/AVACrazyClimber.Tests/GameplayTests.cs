using AVACrazyClimber.Engine;

namespace AVACrazyClimber.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avacrazyclimber-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>On the wall, calm: every window open and staying so, nothing dropped, nothing attacking.</summary>
    private static GameEngine Game(int building = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(building);
        for (int f = 0; f < g.Floors; f++)
            for (int c = 0; c < Field.Cols; c++) g.SetWindow(c, f, Pane.Open);
        g.Signs.Clear();
        g.SetGargoyle(null);
        return g;
    }

    /// <summary>A real climb, with plenty of lives.</summary>
    private static GameEngine Real(int building = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(building);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls Up = new(Up: true), Brace = new(Brace: true);

    // ───────────────────────────── climbing ─────────────────────────────

    [Fact]
    public void Holding_up_climbs_a_hand_at_a_time_a_floor_every_two_reaches()
    {
        var g = Game();
        int ticks = (int)Math.Round(GameEngine.ReachTime * 60);
        g.Run(ticks, Up);
        Assert.Equal(0, g.Climber.Floor);
        Assert.Equal(0.5, g.Climber.Lift, 2);
        g.Run(ticks, Up);
        Assert.Equal(1, g.Climber.Floor);
        Assert.Equal(GameEngine.FloorPoints, g.Score);
        // Let go partway, and it settles back to the hold it had.
        g.Run(3, Up);
        g.Idle(30);
        Assert.Equal(1, g.Climber.Floor);
        Assert.Equal(0, g.Climber.Lift, 3);
    }

    [Fact]
    public void A_shut_window_above_stops_the_climb()
    {
        var g = Game();
        g.SetWindow(3, 1, Pane.Shut);
        g.Run(60, Up);
        Assert.Equal(0, g.Climber.Floor);
        Assert.Equal(0, g.Climber.Lift, 3);
    }

    [Fact]
    public void Across_to_the_next_column_only_where_the_window_is_open()
    {
        var g = Game();
        g.Run(19, new Controls(Dx: 1));
        g.Idle(2);
        Assert.Equal(4, g.Climber.Col);
        g.SetWindow(5, 0, Pane.Shut);
        g.Run(25, new Controls(Dx: 1));
        Assert.Equal(4, g.Climber.Col);
        g.Run(60, new Controls(Dx: -1));
        Assert.True(g.Climber.Col < 4);
    }

    [Fact]
    public void Bracing_holds_it_still()
    {
        var g = Game();
        g.Run(60, new Controls(Up: true, Brace: true));
        Assert.Equal(0, g.Climber.Floor);
        Assert.True(g.Climber.Bracing);
    }

    // ───────────────────────────── windows ─────────────────────────────

    [Fact]
    public void A_window_shutting_on_its_hands_knocks_the_climber_off_unless_it_braces()
    {
        var g = Game();
        g.Calm = false;
        g.SetWindow(3, 0, Pane.Closing, 0.05);
        g.Run(10, Brace);
        Assert.Equal(Phase.Climb, g.Phase);
        g.SetWindow(3, 0, Pane.Closing, 0.05);
        g.Idle(10);
        Assert.Equal(Phase.Falling, g.Phase);
        Assert.Equal("window", g.Cause);
    }

    [Fact]
    public void Windows_open_and_shut_on_their_own()
    {
        var g = Real();
        var states = new HashSet<Pane>();
        for (int i = 0; i < 60 * 12; i++) { g.Tick(new Controls(Brace: true)); foreach (var w in g.Windows) states.Add(w.State); }
        Assert.Contains(Pane.Closing, states);
        Assert.Contains(Pane.Shut, states);
        Assert.Contains(Pane.Opening, states);
        // The bottom floors never shut, and every floor has one that never does.
        for (int c = 0; c < Field.Cols; c++) Assert.True(g.Windows[c, 0].Fixed);
        for (int f = 0; f < g.Floors; f++) Assert.Contains(Enumerable.Range(0, Field.Cols), c => g.Windows[c, f].Fixed);
    }

    // ───────────────────────────── things dropped ─────────────────────────────

    [Fact]
    public void A_dropped_thing_knocks_the_climber_off_unless_it_braces_and_then_bounces_for_points()
    {
        var g = Game();
        g.Drop(ThingKind.Flowerpot, 3, 3);
        g.Run(40, Brace);
        Assert.Equal(Phase.Climb, g.Phase);
        Assert.Equal(GameEngine.BonkPoints, g.Score);
        Assert.Empty(g.Things);
        g.Drop(ThingKind.Dumbbell, 3, 3);
        g.Idle(40);
        Assert.Equal(Phase.Falling, g.Phase);
        Assert.Equal("dumbbell", g.Cause);
    }

    [Fact]
    public void A_thing_in_the_next_column_misses()
    {
        var g = Game();
        g.Drop(ThingKind.Bucket, 4, 3);
        g.Idle(40);
        Assert.Equal(Phase.Climb, g.Phase);
    }

    [Fact]
    public void Residents_lean_out_and_drop_things()
    {
        var g = Real();
        g.Climber.Safe = 999;
        bool leaned = false;
        for (int i = 0; i < 60 * 20 && g.Things.Count == 0; i++) { g.Tick(Brace); foreach (var w in g.Windows) if (w.Leaning > 0) leaned = true; }
        Assert.True(leaned);
        Assert.NotEmpty(g.Things);
    }

    // ───────────────────────────── the gargoyle, the vulture, the signs ─────────────────────────────

    [Fact]
    public void The_gargoyle_winds_up_then_swipes_its_side_and_only_a_brace_saves_you()
    {
        var g = Game(2);
        var gg = new Gargoyle { Floor = 0, Side = 0, Swipe = 0.35 };
        g.SetGargoyle(gg);
        g.Place(1, 0);
        g.Run(2, Brace);
        Assert.Equal(Phase.Climb, g.Phase);
        g.Idle(2);
        Assert.Equal(Phase.Falling, g.Phase);
        Assert.Equal("gargoyle", g.Cause);
        Assert.True(GameEngine.Reaches(gg, 2));
        Assert.False(GameEngine.Reaches(gg, 3));
    }

    [Fact]
    public void A_sign_sparking_throws_off_anyone_on_it_braced_or_not()
    {
        var g = Game();
        g.Signs.Add(new Sign { Floor = 0, From = 2, To = 4, Spark = 0.5, Timer = 99 });
        g.Run(2, Brace);
        Assert.Equal(Phase.Falling, g.Phase);
        Assert.Equal("sign", g.Cause);
    }

    [Fact]
    public void The_vulture_crosses_and_drops_an_egg()
    {
        var g = Real(3);
        g.Climber.Safe = 999;
        for (int i = 0; i < 60 * 12 && !g.Things.Any(t => t.Kind == ThingKind.Egg); i++) g.Tick(Brace);
        Assert.Contains(g.Things, t => t.Kind == ThingKind.Egg);
    }

    // ───────────────────────────── falling, and the roof ─────────────────────────────

    [Fact]
    public void Knocked_off_it_comes_back_on_the_same_floor_safe_a_moment_and_the_last_life_ends_it()
    {
        var g = Game();
        g.SetLives(2);
        g.Place(3, 6);
        g.Drop(ThingKind.Bucket, 3, 9);
        g.Idle(40);
        Assert.Equal(Phase.Falling, g.Phase);
        g.Idle((int)(GameEngine.FallingSeconds * 60) + 2);
        Assert.Equal(Phase.Climb, g.Phase);
        Assert.Equal(6, g.Climber.Floor);
        Assert.True(g.Climber.Safe > 0);
        g.Idle((int)(GameEngine.SafeSeconds * 60));
        g.Drop(ThingKind.Bucket, g.Climber.Col, 9);
        g.Idle(40);
        g.Idle((int)(GameEngine.FallingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void To_the_roof_for_a_bonus_by_the_time_left_then_the_next_building()
    {
        var g = Game();
        g.Place(3, g.Floors - 1);
        g.Run(40, Up);
        Assert.Equal(Phase.Roof, g.Phase);
        Assert.True(g.Bonus >= 1000);
        int before = g.Score;
        g.Idle((int)(GameEngine.RoofSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(before + g.Bonus, g.Score);
        Assert.Equal(0, g.Climber.Floor);
        Assert.True(g.Floors > Towers.All[0].Floors);
    }

    [Fact]
    public void Each_building_is_taller_and_busier()
    {
        Assert.True(Towers.All.Zip(Towers.All.Skip(1)).All(p => p.Second.Floors > p.First.Floors));
        Assert.Null(Real(1).Gargoyle);
        Assert.NotNull(Real(2).Gargoyle);
        Assert.Equal(3, Real(4).Signs.Count);
        Assert.True(Real(5).Busy > Real(1).Busy);
    }

    [Fact]
    public void Points_for_each_new_floor_and_an_extra_life_at_20000()
    {
        var g = Game();
        g.SetScore(GameEngine.FirstExtraLife - 10);
        int lives = g.Lives;
        g.Run(30, Up);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_climber_reaches_the_first_roof()
    {
        var g = Real();
        for (int i = 0; i < 60 * 120 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on building {g.Wave}, floor {g.Climber.Floor}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 30; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Climber.Col} {g.Climber.Floor} {g.Climber.Lift:0.00} {g.Phase} {g.Things.Count}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(15, Up);
        double lift = g.Climber.Lift;
        g.Pause();
        g.Idle(120);
        Assert.Equal(lift, g.Climber.Lift);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
