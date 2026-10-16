namespace AVACrazyClimber.Engine;

/// <summary>The controls: shift across (-1 left, 1 right), climb, and brace against the wall.</summary>
public readonly record struct Controls(int Dx = 0, bool Up = false, bool Brace = false);

/// <summary>
/// The screen, and the building's face on it: eight columns of windows, a floor every so many pixels, the
/// climber kept at the same height on the screen while the building moves past.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Cols = 8;
    public const double ColWidth = 70, FloorHeight = 64;
    public const double Left = (Width - Cols * ColWidth) / 2, Right = Left + Cols * ColWidth;
    /// <summary>Where on the screen the climber's floor is.</summary>
    public const double ClimberY = 540;

    public static double ColX(double col) => Left + (col + 0.5) * ColWidth;
}

/// <summary>One of the buildings to climb.</summary>
public sealed record Tower(string Name, int Floors, uint Wall, uint Trim, uint Glass, double Seconds, bool Gargoyle, bool Vulture, int Signs);

public static class Towers
{
    public static readonly Tower[] All =
    [
        new("THE BRICKWORKS", 24, 0xFF7A3A2A, 0xFFC08A6A, 0xFF2A4A6A, 70, false, false, 0),
        new("THE GRAND HOTEL", 30, 0xFF4A4A6A, 0xFFB0B0D0, 0xFF3A2A5A, 80, true, false, 1),
        new("THE GLASS TOWER", 36, 0xFF2A5A6A, 0xFF8AD0E0, 0xFF103040, 90, false, true, 2),
        new("THE SKYSCRAPER", 42, 0xFF5A5A5A, 0xFFE0E0E0, 0xFF2A3A2A, 100, true, true, 3),
    ];

    public static Tower For(int building) => All[(building - 1) % All.Length];
}

public enum Pane
{
    Open,
    /// <summary>Starting to close: the shutter coming down. Get your hands out.</summary>
    Closing,
    Shut,
    Opening,
}

/// <summary>One window: its state, and how long until it changes.</summary>
public sealed class Sash
{
    public Pane State;
    public double Timer;
    /// <summary>Someone at the window, leaning out: about to drop something.</summary>
    public double Leaning;
    /// <summary>Never shuts (the bottom floors, and some others, to give the climber somewhere to go).</summary>
    public bool Fixed;
    public bool Holdable => State is Pane.Open or Pane.Opening;
}

/// <summary>The climber: on a floor and a column, partway up to the next or across to the next column.</summary>
public sealed class Climber
{
    public int Col = 3, Floor;
    /// <summary>How far through the climb to the next floor: two reaches, a hand each.</summary>
    public double Lift;
    /// <summary>Where it is going across to, and how far it has got.</summary>
    public int ToCol = -1;
    public double Shift;
    public bool Bracing, Alive = true;
    /// <summary>Which hand reaches next.</summary>
    public int Hand;
    public double Safe, FallY, FallV;
    /// <summary>Where it is, in columns and floors, counting the moves partway through.</summary>
    public double X => ToCol < 0 ? Col : Col + (ToCol - Col) * Shift;
    public double Y => Floor + Lift;
}

public enum ThingKind { Flowerpot, Bucket, Dumbbell, Egg, Girder }

/// <summary>Something dropped, falling down a column.</summary>
public sealed class Thing
{
    public ThingKind Kind;
    public double Col, Y, Vy, Spin;
    public bool Dead, Bounced;
}

/// <summary>The gargoyle: on a ledge beside the face, it comes alive and swipes across the columns near it.</summary>
public sealed class Gargoyle
{
    public int Floor, Side;
    public double Timer, Swipe;
    /// <summary>Raising its arm: the swipe comes when this runs out.</summary>
    public double WindUp;
    /// <summary>The columns its swipe reaches, from its side.</summary>
    public const int Reach = 3;
}

/// <summary>The vulture: across the sky over the climber, dropping an egg.</summary>
public sealed class Vulture
{
    public double X, Y, Vx, DropAt;
    public bool Dropped, Dead;
}

/// <summary>A neon sign across some columns of one floor: it sparks now and then, and anyone on it then is thrown off.</summary>
public sealed class Sign
{
    public int Floor, From, To;
    public string Word = "";
    public double Timer, Spark;
    /// <summary>Flickering: a spark is coming.</summary>
    public double Flicker;
    public bool Covers(int col) => col >= From && col <= To;
}

/// <summary>A fleck of something, flying out on the screen.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
