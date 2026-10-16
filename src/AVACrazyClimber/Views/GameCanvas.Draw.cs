using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVACrazyClimber.Engine;
using Effect = AVACrazyClimber.Engine.Effect;

namespace AVACrazyClimber.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVACrazyClimber/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>Where a floor is on the screen, with the view following the climber.</summary>
    private double FloorY(double floor) => Field.ClimberY - (floor - Engine.View) * Field.FloorHeight;

    /// <summary>The sky for each building: morning, afternoon, sunset, night.</summary>
    private static readonly (uint Top, uint Bottom)[] Skies =
        [(0xFF3A7AD0, 0xFFB0D8F0), (0xFF2A5AA0, 0xFFF0D0A0), (0xFF2A1A5A, 0xFFF08A4A), (0xFF02040E, 0xFF1A2A5A)];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        int sky = (g.Wave - 1) % Skies.Length;
        var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        gradient.GradientStops.Add(new GradientStop(Color.FromUInt32(Skies[sky].Top), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromUInt32(Skies[sky].Bottom), 1));
        dc.FillRectangle(gradient, new Rect(0, 0, Field.Width, Field.Height));
        if (sky == 3) for (int i = 0; i < 80; i++) { uint h = Hash(i, 1, 9); dc.DrawEllipse(Brush(0xC0F0F0FF), null, new Point(h % 1024, (h >> 10) % 500), 1, 1); }
        if (InGame || g.State == GameState.Title)
        {
            DrawSkyline(dc, g.View, sky);
            DrawBuilding(dc);
            if (InGame)
            {
                foreach (var v in g.Vultures) DrawVulture(dc, v.X, FloorY(v.Y), v.Vx);
                foreach (var t in g.Things) DrawThing(dc, t.Kind, Field.ColX(t.Col), FloorY(t.Y) - 10, t.Spin);
                DrawClimberNow(dc);
                foreach (var d in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(d.X - 2, d.Y - 2, 4, 4));
                }
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>Other buildings behind, far off, moving slower than the climb so the city seems deep.</summary>
    private static void DrawSkyline(DrawingContext dc, double view, int sky)
    {
        for (int layer = 0; layer < 2; layer++)
        {
            double drift = view * Field.FloorHeight * (layer == 0 ? 0.15 : 0.3);
            uint body = sky == 3 ? (layer == 0 ? 0xFF0A1028u : 0xFF060A1Au) : (layer == 0 ? 0x803A4A6Au : 0xA02A3450u);
            for (int i = 0; i < 16; i++)
            {
                uint h = Hash(i, layer, 4);
                double w = 50 + h % 60, x = i * 68 - 20 + (h >> 6) % 30, height = 200 + (h >> 10) % 380 + layer * 80;
                double top = Field.Height - height + drift;
                if (top > Field.Height) continue;
                dc.FillRectangle(Brush(body), new Rect(x, top, w, Field.Height - top + 2000));
                if (sky == 3)
                    for (double wy = top + 10; wy < Field.Height; wy += 16)
                        for (double wx = x + 6; wx < x + w - 6; wx += 12)
                            if (Hash((int)wx, (int)(wy - drift), layer) % 4 == 0) dc.FillRectangle(Brush(0xA0FFE08A), new Rect(wx, wy, 5, 6));
            }
        }
    }

    /// <summary>The face of the building: its storeys of windows, the street at the foot, the roof at the top.</summary>
    private void DrawBuilding(DrawingContext dc)
    {
        var g = Engine;
        var t = g.Tower;
        double top = FloorY(t.Floors) - 6, bottom = FloorY(0) + Field.FloorHeight * 0.5;
        dc.FillRectangle(Brush(t.Wall), new Rect(Field.Left - 8, Math.Max(-10, top), Field.Cols * Field.ColWidth + 16, Math.Min(Field.Height + 20, bottom) - Math.Max(-10, top)));
        // The street.
        if (bottom < Field.Height + 40)
        {
            dc.FillRectangle(Brush(0xFF3A3A40), new Rect(0, bottom, Field.Width, Field.Height - bottom + 200));
            dc.FillRectangle(Brush(0xFF8A8A90), new Rect(0, bottom, Field.Width, 6));
            for (double x = 20; x < Field.Width; x += 90) dc.FillRectangle(Brush(0xFFE0D070), new Rect(x, bottom + 40, 50, 5));
            Text(dc, "ENTRANCE", new Point(Field.ColX(3.5), bottom - 18), 18, t.Trim, center: true);
        }
        // The roof: a parapet and the helipad.
        if (top > -60)
        {
            dc.FillRectangle(Brush(t.Trim), new Rect(Field.Left - 14, top - 10, Field.Cols * Field.ColWidth + 28, 16));
            dc.FillRectangle(Brush(0xFF5A5A60), new Rect(Field.ColX(2), top - 26, 4 * Field.ColWidth, 16));
            Text(dc, "H", new Point(Field.ColX(3.5), top - 18), 18, Yellow, center: true);
            if (g.Phase == Phase.Roof) DrawHelicopter(dc, Field.ColX(3.5), top - 70 - Math.Max(0, g.PhaseTime - 1.6) * 120);
            else DrawHelicopter(dc, Field.ColX(3.5), top - 70 + Math.Sin(_clock * 3) * 4);
        }
        for (int f = 0; f < t.Floors; f++)
        {
            double y = FloorY(f);
            if (y < -Field.FloorHeight || y > Field.Height + Field.FloorHeight) continue;
            // A ledge along each storey.
            dc.FillRectangle(Brush(Mix(t.Wall, 0xFF000000, 0.35)), new Rect(Field.Left - 8, y + 4, Field.Cols * Field.ColWidth + 16, 5));
            dc.FillRectangle(Brush(t.Trim), new Rect(Field.Left - 8, y + 2, Field.Cols * Field.ColWidth + 16, 3));
            for (int c = 0; c < Field.Cols; c++) DrawWindow(dc, g.Windows[c, f], Field.ColX(c), y, t);
        }
        foreach (var s in g.Signs) DrawSign(dc, s, t);
        if (g.Gargoyle is { } gg) DrawGargoyle(dc, gg);
    }

    /// <summary>
    /// A window: dark inside with curtains when open; a shutter coming down as it closes, slatted when shut; and
    /// now and then someone leaning out with something to drop.
    /// </summary>
    private void DrawWindow(DrawingContext dc, Sash w, double x, double sill, Tower t)
    {
        var r = new Rect(x - 24, sill - 48, 48, 46);
        dc.FillRectangle(Brush(t.Glass), r);
        dc.FillRectangle(Brush(0x60FFE0A0), new Rect(r.X + 2, r.Y + 2, 10, r.Height - 4));
        dc.FillRectangle(Brush(0x60FFE0A0), new Rect(r.Right - 12, r.Y + 2, 10, r.Height - 4));
        double shut = w.State switch { Pane.Shut => 1, Pane.Closing => 1 - w.Timer / 0.7, Pane.Opening => w.Timer / 0.5, _ => 0 };
        if (shut > 0)
        {
            double h = r.Height * Math.Clamp(shut, 0, 1);
            dc.FillRectangle(Brush(Mix(t.Trim, 0xFF000000, 0.15)), new Rect(r.X, r.Y, r.Width, h));
            for (double sy = r.Y + 5; sy < r.Y + h; sy += 6) dc.DrawLine(Pen(Mix(t.Trim, 0xFF000000, 0.4), 1), new Point(r.X, sy), new Point(r.Right, sy));
            if (w.State == Pane.Closing) dc.DrawRectangle(null, Pen(Alpha(Red, 0.5 + 0.5 * Math.Sin(_clock * 20)), 2), r);
        }
        dc.DrawRectangle(null, Pen(t.Trim, 3), r);
        if (w.Leaning > 0 && w.State == Pane.Open)
        {
            // A resident, arms out, holding something over the sill.
            dc.DrawEllipse(Brush(0xFFFFD0A0), null, new Point(x, sill - 26), 9, 9);
            dc.FillRectangle(Brush(0xFF6A3A8A), new Rect(x - 12, sill - 18, 24, 16));
            dc.DrawEllipse(Brush(0xFF3A2A1A), null, new Point(x, sill - 33), 9, 5);
            if (w.Leaning > 0.35) DrawThing(dc, ThingKind.Flowerpot, x, sill - 4, 0);
        }
    }

    private void DrawSign(DrawingContext dc, Sign s, Tower t)
    {
        double y = FloorY(s.Floor), x0 = Field.ColX(s.From) - 30, x1 = Field.ColX(s.To) + 30;
        var r = new Rect(x0, y - 44, x1 - x0, 30);
        dc.FillRectangle(Brush(0xE0101018), r);
        bool lit = s.Spark > 0 || (s.Flicker > 0 ? (int)(_clock * 20) % 2 == 0 : true);
        uint neon = 0xFFFF4AD0;
        dc.DrawRectangle(null, Pen(Alpha(neon, lit ? 0.9 : 0.3), 2), r);
        Text(dc, s.Word, new Point(r.Center.X, r.Center.Y), 28, lit ? neon : Alpha(neon, 0.3), center: true, shadow: false);
        if (s.Spark > 0)
            for (int i = 0; i < 6; i++)
            {
                double ax = x0 + Hash(i, (int)(_clock * 20), 1) % (uint)(x1 - x0);
                dc.DrawLine(Pen(0xFFE0F0FF, 2), new Point(ax, r.Y), new Point(ax + (i % 2 == 0 ? 8 : -8), r.Y - 16));
                dc.DrawLine(Pen(0xFF8AE0FF, 2), new Point(ax, r.Bottom), new Point(ax + (i % 2 == 0 ? -10 : 10), r.Bottom + 14));
            }
    }

    /// <summary>The gargoyle on its ledge beside the face: crouched stone, eyes glowing as it winds up, a long arm swiping.</summary>
    private void DrawGargoyle(DrawingContext dc, Gargoyle g)
    {
        double y = FloorY(g.Floor), side = g.Side == 0 ? -1 : 1, x = side < 0 ? Field.Left - 40 : Field.Right + 40;
        uint stone = 0xFF7A7A80;
        dc.FillRectangle(Brush(0xFF5A5A60), new Rect(x - 30, y, 60, 10));
        Poly(dc, stone, 0xFF3A3A40, 1.5, new Point(x - 20, y), new Point(x - 24, y - 30), new Point(x - 12, y - 48), new Point(x + 12, y - 48), new Point(x + 24, y - 30), new Point(x + 20, y));
        Poly(dc, stone, 0xFF3A3A40, 1.5, new Point(x - 14, y - 46), new Point(x - 20, y - 62), new Point(x - 6, y - 50));
        Poly(dc, stone, 0xFF3A3A40, 1.5, new Point(x + 14, y - 46), new Point(x + 20, y - 62), new Point(x + 6, y - 50));
        foreach (int wing in new[] { -1, 1 }) Poly(dc, Mix(stone, 0xFF000000, 0.25), null, 0, new Point(x + wing * 18, y - 34), new Point(x + wing * 44, y - 56), new Point(x + wing * 30, y - 20));
        uint eye = g.WindUp > 0 || g.Swipe > 0 ? Red : 0xFF3A3A40;
        dc.DrawEllipse(Brush(eye), null, new Point(x - 6, y - 36), 3.5, 3.5);
        dc.DrawEllipse(Brush(eye), null, new Point(x + 6, y - 36), 3.5, 3.5);
        if (g.WindUp > 0) dc.DrawLine(Pen(stone, 8), new Point(x - side * 10, y - 26), new Point(x - side * 22, y - 66));
        if (g.Swipe > 0)
        {
            double reach = Gargoyle.Reach * Field.ColWidth + 30;
            dc.DrawLine(Pen(stone, 10), new Point(x, y - 26), new Point(x - side * reach, y - 30));
            dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.4), 3), new Point(x - side * 20, y - 44), new Point(x - side * reach, y - 46));
            for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFF3A3A40, 3), new Point(x - side * reach, y - 30), new Point(x - side * (reach + 14), y - 40 + i * 10));
        }
    }

    private void DrawVulture(DrawingContext dc, double x, double y, double vx)
    {
        double flap = Math.Sin(_clock * 10) * 14, f = vx > 0 ? 1 : -1;
        Poly(dc, 0xFF2A2020, null, 0, new Point(x - 6, y), new Point(x - 34, y - flap), new Point(x - 10, y + 6));
        Poly(dc, 0xFF2A2020, null, 0, new Point(x + 6, y), new Point(x + 34, y - flap), new Point(x + 10, y + 6));
        dc.DrawEllipse(Brush(0xFF3A2A2A), null, new Point(x, y + 2), 14, 8);
        dc.DrawEllipse(Brush(0xFFE0B0A0), null, new Point(x + f * 14, y - 4), 6, 5);
        Poly(dc, Yellow, null, 0, new Point(x + f * 18, y - 5), new Point(x + f * 26, y - 1), new Point(x + f * 18, y - 1));
    }

    /// <summary>Things dropped: a flowerpot, a bucket of water, a dumbbell, an egg, or a girder.</summary>
    private void DrawThing(DrawingContext dc, ThingKind kind, double x, double y, double spin)
    {
        using var _ = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(kind is ThingKind.Girder or ThingKind.Bucket ? 0 : spin * 0.3) * Matrix.CreateTranslation(x, y));
        switch (kind)
        {
            case ThingKind.Flowerpot:
                Poly(dc, 0xFFC0603A, 0xFF6A2A10, 1, new Point(x - 10, y - 4), new Point(x + 10, y - 4), new Point(x + 7, y + 10), new Point(x - 7, y + 10));
                dc.DrawEllipse(Brush(0xFF3AAA3A), null, new Point(x - 4, y - 9), 5, 5);
                dc.DrawEllipse(Brush(0xFF4ACA4A), null, new Point(x + 4, y - 10), 5, 5);
                dc.DrawEllipse(Brush(0xFFFF6AA0), null, new Point(x, y - 14), 3, 3);
                break;
            case ThingKind.Bucket:
                Poly(dc, 0xFFA0A8B0, 0xFF505860, 1, new Point(x - 11, y - 8), new Point(x + 11, y - 8), new Point(x + 8, y + 9), new Point(x - 8, y + 9));
                dc.DrawEllipse(Brush(0xFF6AB0FF), null, new Point(x, y - 8), 11, 3);
                for (int i = 0; i < 3; i++) dc.DrawEllipse(Brush(0xA06AB0FF), null, new Point(x - 6 + i * 6, y - 16 - (i % 2) * 4), 2.5, 3.5);
                break;
            case ThingKind.Dumbbell:
                dc.FillRectangle(Brush(0xFF8A8A90), new Rect(x - 12, y - 2, 24, 4));
                dc.DrawRectangle(Brush(0xFF2A2A30), Pen(0xFF8A8A90, 1), new Rect(x - 16, y - 8, 7, 16), 2, 2);
                dc.DrawRectangle(Brush(0xFF2A2A30), Pen(0xFF8A8A90, 1), new Rect(x + 9, y - 8, 7, 16), 2, 2);
                break;
            case ThingKind.Egg:
                dc.DrawEllipse(Brush(0xFFF8F4E8), Pen(0xFFB0A890, 1), new Point(x, y), 7, 9);
                break;
            default:
                dc.FillRectangle(Brush(0xFFC03A2A), new Rect(x - 30, y - 6, 60, 4));
                dc.FillRectangle(Brush(0xFFC03A2A), new Rect(x - 30, y + 2, 60, 4));
                dc.FillRectangle(Brush(0xFFA02A1A), new Rect(x - 28, y - 3, 56, 6));
                for (double bx = x - 24; bx < x + 26; bx += 12) dc.DrawEllipse(Brush(0xFF6A1A10), null, new Point(bx, y), 1.6, 1.6);
                break;
        }
    }

    private void DrawHelicopter(DrawingContext dc, double x, double y)
    {
        double blade = Math.Sin(_clock * 40) * 60;
        dc.DrawLine(Pen(0xFF2A2A30, 3), new Point(x - blade, y - 22), new Point(x + blade, y - 22));
        dc.DrawLine(Pen(0xFF2A2A30, 2), new Point(x, y - 22), new Point(x, y - 14));
        dc.DrawEllipse(Brush(0xFFE0A030), Pen(0xFF6A4A10, 1.5), new Point(x, y), 26, 15);
        dc.DrawEllipse(Brush(0xFF8AD0F0), null, new Point(x + 12, y - 3), 10, 8);
        dc.FillRectangle(Brush(0xFFE0A030), new Rect(x - 60, y - 4, 38, 6));
        dc.DrawLine(Pen(0xFF2A2A30, 2), new Point(x - 60, y - 12), new Point(x - 60, y + 6));
        dc.DrawLine(Pen(0xFF2A2A30, 2), new Point(x - 18, y + 16), new Point(x + 18, y + 16));
        if (Engine.Phase == Phase.Roof && Engine.PhaseTime > 1.6) DrawClimber(dc, x, y + 44, 0, false, false, 0.8);
    }

    private void DrawClimberNow(DrawingContext dc)
    {
        var g = Engine;
        var k = g.Climber;
        if (g.Phase == Phase.Roof && g.PhaseTime > 1.6) return;
        double x = Field.Left + (k.X + 0.5) * Field.ColWidth, y = FloorY(k.Y);
        if (!k.Alive)
        {
            using var _ = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(g.PhaseTime * 8) * Matrix.CreateTranslation(x, y + k.FallY));
            DrawClimber(dc, x, y, 0, false, true, 1);
            return;
        }
        if (k.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        DrawClimber(dc, x, y, k.Lift, k.Bracing, false, 1, k.Hand);
    }

    /// <summary>
    /// The climber, seen from behind against the wall: red cap, blue overalls, arms up to the sills (one reaching
    /// higher mid-climb), legs below; bracing, it presses flat with its arms bent; falling, it flails.
    /// </summary>
    private void DrawClimber(DrawingContext dc, double x, double y, double lift, bool bracing, bool flailing, double k, int hand = 0)
    {
        double reach = (lift % 0.5) * 2, up = Field.FloorHeight * 0.5 * k;
        // y is the sill its hands are on; the body hangs below.
        double shoulder = y + 18 * k, hip = y + 46 * k;
        uint overalls = 0xFF2A5AD0, skin = 0xFFFFD0A0;
        // Arms.
        double leftHand = y - (hand == 0 ? reach * up : 0), rightHand = y - (hand == 1 ? reach * up : 0);
        if (bracing) { leftHand = y + 8 * k; rightHand = y + 8 * k; }
        if (flailing) { leftHand = y - 10 * k; rightHand = y + 30 * k; }
        dc.DrawLine(Pen(skin, 6 * k), new Point(x - 10 * k, shoulder), new Point(x - (bracing ? 20 : 14) * k, leftHand));
        dc.DrawLine(Pen(skin, 6 * k), new Point(x + 10 * k, shoulder), new Point(x + (bracing ? 20 : 14) * k, rightHand));
        dc.DrawEllipse(Brush(skin), null, new Point(x - (bracing ? 20 : 14) * k, leftHand), 4 * k, 4 * k);
        dc.DrawEllipse(Brush(skin), null, new Point(x + (bracing ? 20 : 14) * k, rightHand), 4 * k, 4 * k);
        // Legs, kicking a little as it climbs.
        double kick = Math.Sin(lift * Math.PI * 4) * 6 * k;
        dc.DrawLine(Pen(overalls, 7 * k), new Point(x - 6 * k, hip), new Point(x - 9 * k, hip + 22 * k + kick));
        dc.DrawLine(Pen(overalls, 7 * k), new Point(x + 6 * k, hip), new Point(x + 9 * k, hip + 22 * k - kick));
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x - 14 * k, hip + 20 * k + kick, 9 * k, 5 * k));
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x + 5 * k, hip + 20 * k - kick, 9 * k, 5 * k));
        // Body and head.
        dc.DrawRectangle(Brush(overalls), Pen(0xFF10306A, 1), new Rect(x - 12 * k, shoulder - 4 * k, 24 * k, hip - shoulder + 6 * k), 5 * k, 5 * k);
        dc.FillRectangle(Brush(0xFFF0E0C0), new Rect(x - 9 * k, shoulder - 4 * k, 18 * k, 8 * k));
        dc.DrawEllipse(Brush(0xFF6A3A1A), null, new Point(x, shoulder - 12 * k), 9 * k, 9 * k);
        dc.DrawEllipse(Brush(0xFFD02A2A), null, new Point(x, shoulder - 16 * k), 9 * k, 6 * k);
        dc.FillRectangle(Brush(0xFFD02A2A), new Rect(x - 9 * k, shoulder - 17 * k, 18 * k, 4 * k));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xA0000010), new Rect(0, 0, Field.Width, 70));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"BUILDING {g.Wave}", new Point(512, 16), 24, g.Tower.Trim, center: true);
        Text(dc, $"FLOOR {Math.Min(g.Climber.Floor, g.Floors)}/{g.Floors}", new Point(430, 46), 26, White, center: true);
        Text(dc, $"BONUS {(int)g.TimeLeft * 30 + 1000 * g.Wave}", new Point(610, 46), 26, g.TimeLeft < 15 ? Red : Cyan, center: true);
        int spare = Math.Max(0, g.Lives - (g.Climber.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 5); i++) DrawClimber(dc, 220 + i * 26, 22, 0, false, false, 0.45);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 300;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Falling ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB0000010), new Rect(0, 70, Field.Width, Field.Height - 70));
        Text(dc, "AVA CRAZY CLIMBER", new Point(cx, 116), 92, 0xFFFFB347, center: true);
        Text(dc, "AFTER THE 1980 ARCADE CLASSIC", new Point(cx, 172), 28, Dim, center: true);
        DrawClimber(dc, cx, 220, (_clock * 2.5) % 1, false, false, 1.6, (int)(_clock * 5) % 2);
        Text(dc, "CLIMB TO THE ROOF - THE HELICOPTER WON'T WAIT FOR EVER", new Point(cx, 352), 24, White, center: true);
        Text(dc, "WINDOWS SHUT: DON'T LEAVE YOUR HANDS IN ONE - BRACE, OR MOVE", new Point(cx, 382), 24, White, center: true);
        (ThingKind Kind, string Name)[] things = [(ThingKind.Flowerpot, "FLOWERPOT"), (ThingKind.Bucket, "BUCKET"), (ThingKind.Dumbbell, "DUMBBELL"), (ThingKind.Egg, "EGG"), (ThingKind.Girder, "GIRDER")];
        for (int i = 0; i < things.Length; i++)
        {
            double x = cx - 320 + i * 160;
            DrawThing(dc, things[i].Kind, x, 440, _clock + i);
            Text(dc, things[i].Name, new Point(x, 474), 20, Dim, center: true);
        }
        Text(dc, "BRACE AND THEY BOUNCE OFF: 100 EACH - BUT NOT A SIGN'S SPARK", new Point(cx, 518), 22, Cyan, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 616), 46, Yellow, center: true);
        Text(dc, "UP: CLIMB    LEFT/RIGHT: ACROSS    DOWN: BRACE    P: PAUSE    M: SOUND", new Point(cx, 672), 23, Dim, center: true);
        Text(dc, $"EXTRA LIFE AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 702), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000010), new Rect(0, 70, Field.Width, Field.Height - 70));
        Text(dc, "TOP CLIMBERS", new Point(cx, 140), 68, 0xFFFFB347, center: true);
        Text(dc, "RANK NAME    SCORE  BUILDING", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000010), new Rect(0, 70, Field.Width, Field.Height - 70));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }

    /// <summary>A colour part of the way to another.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Ch(24) << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}
