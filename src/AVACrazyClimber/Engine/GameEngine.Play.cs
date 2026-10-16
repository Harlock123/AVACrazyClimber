namespace AVACrazyClimber.Engine;

public sealed partial class GameEngine
{
    /// <summary>Seconds for one reach (half a floor), and to shift across a column.</summary>
    public const double ReachTime = 0.2, ShiftTime = 0.3;
    /// <summary>How fast things fall, in floors a second.</summary>
    public const double FallSpeed = 7;
    public const double StartSeconds = 2.2, FallingSeconds = 2.2, SafeSeconds = 2, RoofSeconds = 4;
    public const int FloorPoints = 50, BonkPoints = 100, FirstExtraLife = 20000, ExtraLifeEvery = 40000;

    /// <summary>The building: 1 for the first; the four come round again, busier.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Tower Tower { get; private set; } = Towers.For(1);
    public Sash[,] Windows { get; private set; } = new Sash[Field.Cols, 1];
    public Climber Climber { get; } = new();
    public List<Thing> Things { get; } = [];
    public Gargoyle? Gargoyle { get; private set; }
    public List<Vulture> Vultures { get; } = [];
    public List<Sign> Signs { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    /// <summary>The bonus clock: what's left of it at the roof is paid out.</summary>
    public double TimeLeft { get; private set; }
    public int Bonus { get; private set; }
    /// <summary>Where the view is centred, in floors: it follows the climber up.</summary>
    public double View { get; private set; }
    /// <summary>For tests: the windows stay open, nothing is dropped and nothing attacks.</summary>
    internal bool Calm { get; set; }

    private int _best, _nextExtra;
    private double _dropIn, _vultureIn;

    public int Floors => Tower.Floors;
    /// <summary>How busy the residents are, building by building.</summary>
    public double Busy => Math.Min(2.2, 1 + 0.15 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Wreckage.Clear();
        Build();
    }

    /// <summary>A building: its windows, some shut and some for good open, its signs, its gargoyle.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Build();
        Say($"BUILDING {wave}", $"{Tower.Name} - {Floors} FLOORS", StartSeconds + 0.3);
        Play(Sfx.BuildingStart);
    }

    /// <summary>The building for the wave, laid out, and the climber at its foot.</summary>
    private void Build()
    {
        int wave = Wave;
        Tower = Towers.For(wave);
        Windows = new Sash[Field.Cols, Floors];
        int open = _rng.Next(Field.Cols);
        for (int f = 0; f < Floors; f++)
        {
            // One column on every floor never shuts, so there's always a way on up; it wanders across.
            if (f % 3 == 0) open = Math.Clamp(open + _rng.Next(-2, 3), 0, Field.Cols - 1);
            for (int c = 0; c < Field.Cols; c++)
            {
                var w = new Sash { Fixed = f < 2 || c == open, Timer = 1 + _rng.NextDouble() * 8 };
                if (!w.Fixed && _rng.NextDouble() < 0.25) w.State = Pane.Shut;
                Windows[c, f] = w;
            }
        }
        Signs.Clear();
        string[] words = ["HOTEL", "DINER", "RADIO", "TOYS", "CAFE", "BANK"];
        for (int i = 0; i < Tower.Signs; i++)
        {
            int from = _rng.Next(Field.Cols - 3);
            Signs.Add(new Sign { Floor = Floors * (i + 1) / (Tower.Signs + 1) + 2, From = from, To = from + 2, Word = words[(wave + i) % words.Length], Timer = 2 + _rng.NextDouble() * 3 });
        }
        Gargoyle = Tower.Gargoyle ? new Gargoyle { Floor = (int)(Floors * 0.6), Side = _rng.Next(2), Timer = 2 } : null;
        Vultures.Clear();
        Things.Clear();
        var c0 = Climber;
        c0.Col = 3; c0.Floor = 0; c0.Lift = 0; c0.ToCol = -1; c0.Shift = 0; c0.Alive = true; c0.Bracing = false; c0.Safe = 0;
        _best = 0;
        View = 0;
        TimeLeft = Tower.Seconds;
        _dropIn = 3;
        _vultureIn = 6;
        SetPhase(Phase.Start);
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        View += (Climber.Y - View) * Math.Min(1, 4 * dt);
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds) { SetPhase(Phase.Climb); Music(true); }
                break;
            case Phase.Climb:
                TimeLeft = Math.Max(0, TimeLeft - dt);
                Windows_(dt);
                Climb(dt, c);
                if (Phase != Phase.Climb) break;
                Hazards(dt);
                Dangers();
                break;
            case Phase.Falling:
                Climber.FallV += 1400 * dt;
                Climber.FallY += Climber.FallV * dt;
                MoveThings(dt);
                if (PhaseTime >= FallingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Roof:
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2)
                {
                    Score += Bonus;
                    Play(Sfx.Bonus);
                    CheckExtra();
                }
                if (PhaseTime >= RoofSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    public Sash? WindowAt(int col, int floor) => col >= 0 && col < Field.Cols && floor >= 0 && floor < Floors ? Windows[col, floor] : null;

    /// <summary>A window's hold, or the roof's ledge above the top floor.</summary>
    public bool Holdable(int col, int floor) => floor >= Floors || (WindowAt(col, floor)?.Holdable ?? false);

    // ───────────────────────────── the windows ─────────────────────────────

    /// <summary>Each window opens and shuts on its own clock, the climber's hands in it or not.</summary>
    private void Windows_(double dt)
    {
        if (Calm) return;
        var k = Climber;
        for (int f = 0; f < Floors; f++)
            for (int c = 0; c < Field.Cols; c++)
            {
                var w = Windows[c, f];
                if (w.Leaning > 0) w.Leaning -= dt;
                if (w.Fixed) continue;
                // Only bother with what's near enough to matter (or be seen).
                if (Math.Abs(f - k.Floor) > 12) continue;
                w.Timer -= dt * Busy;
                if (w.Timer > 0) continue;
                switch (w.State)
                {
                    case Pane.Open: w.State = Pane.Closing; w.Timer = 0.7; break;
                    case Pane.Closing:
                        w.State = Pane.Shut; w.Timer = 2 + _rng.NextDouble() * 3;
                        Shut(c, f);
                        break;
                    case Pane.Shut: w.State = Pane.Opening; w.Timer = 0.5; break;
                    default: w.State = Pane.Open; w.Timer = 3 + _rng.NextDouble() * 7; break;
                }
            }
    }

    /// <summary>A window slams shut: if the climber's hands are in it and it isn't braced, it's knocked off.</summary>
    private void Shut(int col, int floor)
    {
        var k = Climber;
        bool held = (k.Col == col || k.ToCol == col) && (k.Floor == floor || (k.Lift > 0 && k.Floor + 1 == floor));
        if (!held || !k.Alive) return;
        Play(Sfx.Shut);
        if (k.Bracing || k.Safe > 0)
        {
            // Hands pulled in just in time: a climb under way goes back down.
            if (k.Floor + 1 == floor) k.Lift = 0;
            if (k.ToCol == col) { k.ToCol = -1; k.Shift = 0; }
            return;
        }
        Fall("window");
    }

    // ───────────────────────────── the climber ─────────────────────────────

    /// <summary>
    /// Up: a hand at a time to the next floor, if its window will take a hand. Left or right: across to the next
    /// column, if its window is open. Down: brace against the wall, ready for what's coming. Bracing, it can't move.
    /// </summary>
    private void Climb(double dt, Controls c)
    {
        var k = Climber;
        k.Safe = Math.Max(0, k.Safe - dt);
        bool resting = k.ToCol < 0 && (k.Lift == 0 || k.Lift == 0.5);
        k.Bracing = c.Brace && resting;
        if (k.Bracing) return;
        if (k.ToCol >= 0)
        {
            k.Shift += dt / ShiftTime;
            if (k.Shift >= 1) { k.Col = k.ToCol; k.ToCol = -1; k.Shift = 0; }
            return;
        }
        if (c.Dx != 0 && k.Lift == 0)
        {
            int to = k.Col + c.Dx;
            if (to >= 0 && to < Field.Cols && Holdable(to, k.Floor))
            {
                k.ToCol = to;
                k.Shift = 0;
                Play(Sfx.Shift);
            }
            return;
        }
        // Up: the next reach, if the window above takes a hand (else a bump, and back).
        double rest = k.Lift < 0.5 ? 0 : 0.5, target = c.Up ? rest + 0.5 : rest;
        if (c.Up && !Holdable(k.Col, k.Floor + 1))
        {
            if (k.Lift > 0 && k.Lift < 0.5) Play(Sfx.Bump);
            k.Lift = Math.Max(0, k.Lift - dt / ReachTime);
            return;
        }
        if (k.Lift < target)
        {
            // (A hair over, so a reach takes a whole number of steps rather than one too many from rounding.)
            k.Lift = Math.Min(target, k.Lift + 0.5 * dt / ReachTime + 1e-9);
            if (k.Lift >= target)
            {
                k.Hand = 1 - k.Hand;
                Play(Sfx.Reach);
                if (k.Lift >= 1)
                {
                    k.Floor++;
                    k.Lift = 0;
                    if (k.Floor > _best) { _best = k.Floor; Award(FloorPoints, Field.ColX(k.Col), Field.ClimberY - 30, quiet: true); Play(Sfx.Floor); }
                    if (k.Floor >= Floors) Roof();
                }
            }
        }
        else if (!c.Up && k.Lift > rest) k.Lift = Math.Max(rest, k.Lift - 0.5 * dt / ReachTime);
    }

    // ───────────────────────────── the hazards ─────────────────────────────

    private void Hazards(double dt)
    {
        MoveThings(dt);
        if (Calm) return;
        var k = Climber;
        // Residents lean out of an open window above and drop something.
        if ((_dropIn -= dt * Busy) <= 0)
        {
            _dropIn = 1.6 + _rng.NextDouble() * 1.8;
            int f = k.Floor + 3 + _rng.Next(5), col = Math.Clamp(k.Col + _rng.Next(-2, 3), 0, Field.Cols - 1);
            if (WindowAt(col, f) is { State: Pane.Open } w)
            {
                w.Leaning = 1.1;
                Play(Sfx.Lean);
            }
        }
        for (int f = Math.Max(0, k.Floor); f < Math.Min(Floors, k.Floor + 12); f++)
            for (int col = 0; col < Field.Cols; col++)
            {
                var w = Windows[col, f];
                if (w.Leaning > 0 && w.Leaning - dt <= 0.35 && w.Leaning > 0.35)
                {
                    var kind = (ThingKind)_rng.Next(3);
                    Things.Add(new Thing { Kind = kind, Col = col, Y = f + 0.6, Vy = FallSpeed * (kind == ThingKind.Dumbbell ? 1.15 : 1) });
                    Play(Sfx.Whistle);
                }
            }
        // Near the top, steel from the work on the roof.
        if (k.Floor > Floors - 8 && _rng.NextDouble() < dt * 0.25 * Busy)
            Things.Add(new Thing { Kind = ThingKind.Girder, Col = _rng.Next(Field.Cols), Y = Floors + 1, Vy = FallSpeed * 1.1 });
        // The gargoyle: when the climber is close, it winds up, then swipes across its side.
        if (Gargoyle is { } g && Math.Abs(k.Floor - g.Floor) <= 5)
        {
            if (g.Swipe > 0) g.Swipe -= dt;
            else if (g.WindUp > 0)
            {
                g.WindUp -= dt;
                if (g.WindUp <= 0) { g.Swipe = 0.35; Play(Sfx.Gargoyle); }
            }
            else if ((g.Timer -= dt * Busy) <= 0) { g.Timer = 2.5 + _rng.NextDouble() * 2; g.WindUp = 0.9; }
        }
        // The vulture.
        if (Tower.Vulture && (_vultureIn -= dt) <= 0)
        {
            _vultureIn = 6 + _rng.NextDouble() * 4;
            bool left = _rng.Next(2) == 0;
            Vultures.Add(new Vulture { X = left ? -40 : Field.Width + 40, Y = k.Floor + 6.5, Vx = left ? 200 : -200, DropAt = Field.ColX(k.Col) });
            Play(Sfx.Squawk);
        }
        foreach (var v in Vultures)
        {
            v.X += v.Vx * dt;
            if (!v.Dropped && Math.Abs(v.X - v.DropAt) < 8)
            {
                v.Dropped = true;
                Things.Add(new Thing { Kind = ThingKind.Egg, Col = Math.Round((v.DropAt - Field.Left) / Field.ColWidth - 0.5), Y = v.Y, Vy = FallSpeed * 0.8 });
            }
            if (v.X < -60 || v.X > Field.Width + 60) v.Dead = true;
        }
        Vultures.RemoveAll(v => v.Dead);
        // The signs: a flicker, then a spark.
        foreach (var s in Signs)
        {
            if (s.Spark > 0) { s.Spark -= dt; continue; }
            if (s.Flicker > 0)
            {
                s.Flicker -= dt;
                if (s.Flicker <= 0) { s.Spark = 0.7; if (Math.Abs(k.Floor - s.Floor) < 8) Play(Sfx.Zap); }
                continue;
            }
            if ((s.Timer -= dt * Busy) <= 0) { s.Timer = 3 + _rng.NextDouble() * 2.5; s.Flicker = 0.8; }
        }
    }

    private void MoveThings(double dt)
    {
        foreach (var t in Things)
        {
            t.Y -= t.Vy * dt;
            t.Spin += dt * 8;
            if (t.Y < View - 8) t.Dead = true;
        }
        Things.RemoveAll(t => t.Dead);
    }

    /// <summary>What can knock the climber off: dropped things, the gargoyle's swipe, a sign's spark.</summary>
    private void Dangers()
    {
        var k = Climber;
        if (!k.Alive) return;
        foreach (var t in Things)
        {
            if (t.Bounced || Math.Abs(t.Col - k.X) > 0.55 || t.Y > k.Y + 1.1 || t.Y < k.Y - 0.2) continue;
            if (k.Bracing || k.Safe > 0)
            {
                // Off the top of the head and away.
                t.Bounced = true;
                t.Dead = true;
                Bang(Field.ColX(t.Col), Field.ClimberY - 50, 0xFFFFE14A, 10);
                if (k.Bracing) Award(BonkPoints, Field.ColX(t.Col), Field.ClimberY - 60);
                Play(Sfx.Bonk);
                continue;
            }
            Fall(t.Kind.ToString().ToLowerInvariant());
            return;
        }
        if (Gargoyle is { Swipe: > 0 } g && Math.Abs(k.Y - g.Floor) <= 1.2 && Reaches(g, (int)Math.Round(k.X)) && !k.Bracing && k.Safe <= 0) { Fall("gargoyle"); return; }
        foreach (var s in Signs)
            if (s.Spark > 0 && s.Covers((int)Math.Round(k.X)) && (k.Floor == s.Floor || (k.Lift > 0 && k.Floor + 1 == s.Floor)) && k.Safe <= 0) { Fall("sign"); return; }
    }

    public static bool Reaches(Gargoyle g, int col) => g.Side == 0 ? col < Gargoyle.Reach : col >= Field.Cols - Gargoyle.Reach;

    // ───────────────────────────── falling, and the roof ─────────────────────────────

    private void Fall(string cause)
    {
        var k = Climber;
        k.Alive = false;
        k.FallY = 0;
        k.FallV = -200;
        Cause = cause;
        Lives--;
        Music(false);
        Play(Sfx.Fall);
        SetPhase(Phase.Falling);
        if (Lives <= 0) Say("GAME OVER", null, FallingSeconds);
    }

    /// <summary>Back on the wall where it fell from, at the nearest column that will take it, safe a moment.</summary>
    private void Respawn()
    {
        var k = Climber;
        k.Alive = true;
        k.Lift = 0;
        k.ToCol = -1;
        k.Shift = 0;
        k.Bracing = false;
        k.Safe = SafeSeconds;
        int best = k.Col;
        for (int d = 0; d < Field.Cols; d++)
        {
            if (Holdable(k.Col - d, k.Floor) && k.Col - d >= 0) { best = k.Col - d; break; }
            if (Holdable(k.Col + d, k.Floor) && k.Col + d < Field.Cols) { best = k.Col + d; break; }
        }
        k.Col = best;
        Things.Clear();
        Vultures.Clear();
        SetPhase(Phase.Climb);
        Music(true);
    }

    private void Roof()
    {
        Bonus = 1000 * Wave + (int)TimeLeft * 30;
        Things.Clear();
        Vultures.Clear();
        Music(false);
        Play(Sfx.Helicopter);
        Say("TO THE ROOF!", $"BONUS {Bonus}", RoofSeconds);
        SetPhase(Phase.Roof);
    }

    private void Award(int points, double x, double y, bool quiet = false)
    {
        Score += points;
        if (!quiet) Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 0.8, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Bang(double x, double y, uint colour, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 160;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.3 + _rng.NextDouble() * 0.5, Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vy += 300 * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight onto a building's wall, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Climb);
        Message = SubMessage = null;
    }

    internal void Place(int col, int floor)
    {
        var k = Climber;
        k.Col = col; k.Floor = floor; k.Lift = 0; k.ToCol = -1; k.Shift = 0;
        _best = Math.Max(_best, floor);
        View = floor;
    }

    internal void SetWindow(int col, int floor, Pane state, double timer = 99)
    {
        var w = Windows[col, floor];
        w.State = state;
        w.Timer = timer;
        w.Fixed = false;
    }

    internal Thing Drop(ThingKind kind, int col, double y)
    {
        var t = new Thing { Kind = kind, Col = col, Y = y, Vy = FallSpeed };
        Things.Add(t);
        return t;
    }

    internal void SetGargoyle(Gargoyle? g) => Gargoyle = g;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
