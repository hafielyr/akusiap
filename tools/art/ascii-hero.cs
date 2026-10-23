#:property TreatWarningsAsErrors=false
#:property AnalysisLevel=none
#:property EnforceCodeStyleInBuild=false
// Generates src/Akusiap.Web/wwwroot/img/hero-ascii.svg (BRD-05): a sunlit Greek temple of five fluted pillars with
// floating exam sheets, drawn as ASCII characters by density (Pencil-style halftone) in three blue tones.
// Run from the repo root: dotnet run tools/art/ascii-hero.cs
using System.Globalization;
using System.Text;

const int Cols = 168, Rows = 96;             // character grid
const double CellW = 5.4, CellH = 10.4;      // SVG units per character (font-size 9)
const string Ramp = " .:-=+*#%@";            // light to dense
var density = new double[Rows, Cols];
var painted = new bool[Rows, Cols];
var overlay = new (char Ch, string Tone)?[Rows, Cols];

// Scene space is 120 x 137 "units"; each character covers S units across and 2S units down, so shapes keep their
// proportions whatever the grid size.
const double S = 120.0 / Cols;
static double Clamp(double v) => Math.Max(0, Math.Min(1, v));
static double Hash(int a, int b) { var h = (uint)(a * 73856093 ^ b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535.0; }
void Paint(Func<double, double, double?> shape)
{
    for (var r = 0; r < Rows; r++)
    for (var c = 0; c < Cols; c++)
    {
        if (shape((c + 0.5) * S, (r + 0.5) * 2 * S) is { } d) { density[r, c] = Clamp(d); painted[r, c] = true; }
    }
}

// Light from the upper left, slightly in front.
static double Lambert(double nx, double ny, double nz) => Clamp(-0.6 * nx - 0.3 * ny + 0.74 * nz);

// --- Ground shadow (soft dots under the temple) ---------------------------------------------------------------------
Paint((x, y) =>
{
    var dx = (x - 66) / 62; var dy = (y - 133) / 5;
    var e = dx * dx + dy * dy;
    return e < 1 ? 0.12 * (1 - e) : null;
});

// --- Floating exam sheets -------------------------------------------------------------------------------------------
void Sheet(double cx, double cy, double w, double h, double angleDeg, bool tick)
{
    var a = angleDeg * Math.PI / 180; var cos = Math.Cos(a); var sin = Math.Sin(a);
    (double U, double V) Local(double x, double y) { var dx = x - cx; var dy = y - cy; return (dx * cos + dy * sin, -dx * sin + dy * cos); }
    Paint((x, y) =>                                   // drop shadow
    {
        var (u, v) = Local(x - 1.6, y - 2.4);
        return Math.Abs(u) <= w / 2 && Math.Abs(v) <= h / 2 ? 0.28 : null;
    });
    Paint((x, y) =>
    {
        var (u, v) = Local(x, y);
        if (Math.Abs(u) > w / 2 || Math.Abs(v) > h / 2) return null;
        if (Math.Abs(u) > w / 2 - 0.9 || Math.Abs(v) > h / 2 - 1.6) return 0.5;          // edge
        var top = v + h / 2;
        if (top is > 3 and < 5.2 && u < w / 4) return 0.62;                               // title line
        if (tick && top > 8 && (top - 8) % 6.5 < 1.6 && u < -w / 2 + 4.5 && u > -w / 2 + 2) return 0.85; // tick boxes
        var line = (top - 8) % 6.5;
        var len = w / 2 - 3 - ((int)(top / 6.5) * 37 % 9);
        if (top > 8 && line < 1.3 && u > -w / 2 + (tick ? 6 : 2.5) && u < len) return 0.36; // ruled text lines
        return 0.03;
    });
}
Sheet(11, 44, 17, 25, -13, true);
Sheet(109, 26, 15, 21, 10, false);
Sheet(113, 76, 11, 15, -7, true);

// --- Steps ----------------------------------------------------------------------------------------------------------
(double X0, double X1, double Y0, double Y1)[] steps = [(15, 105, 115, 121), (10, 110, 121, 127), (5, 115, 127, 134)];
foreach (var (x0, x1, y0, y1) in steps)
{
    Paint((x, y) =>
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return null;
        var across = (x - x0) / (x1 - x0);
        if (y < y0 + 2.2) return 0.08 + 0.1 * across;                                   // lit tread
        if (x > x1 - 1.2) return 0.55;                                                   // shaded end
        return 0.22 + 0.16 * across + 0.12 * (y - y0) / (y1 - y0);                        // riser
    });
}

// --- Cella wall behind the columns (in shadow) ---------------------------------------------------------------------
Paint((x, y) => x is > 17 and < 103 && y is > 47 and < 115 ? 0.34 + 0.2 * (115 - y) / 68 : null);

// --- Columns --------------------------------------------------------------------------------------------------------
double[] centers = [24, 42, 60, 78, 96];
const double Radius = 5.6;
foreach (var cx in centers)
{
    Paint((x, y) =>
    {
        if (y < 51 || y > 112) return null;
        var r = Radius - (112 - y) * 0.011;                                              // entasis
        var nx = (x - cx) / r;
        if (Math.Abs(nx) > 1) return null;
        var nz = Math.Sqrt(1 - nx * nx);
        var lit = Math.Pow(Lambert(nx, 0, nz), 0.8);
        var flute = 0.09 * Math.Cos(nx * Math.PI * 5);
        return 0.78 - 0.76 * lit + flute;
    });
    foreach (var (y0, y1, extra) in new[] { (111.5, 115.0, 2.0), (48.0, 51.0, 1.4), (45.0, 48.0, 2.8) })
    {
        Paint((x, y) =>
        {
            if (y < y0 || y > y1) return null;
            var nx = (x - cx) / (Radius + extra);
            if (Math.Abs(nx) > 1) return null;
            var nz = Math.Sqrt(1 - nx * nx);
            return 0.7 - 0.66 * Math.Pow(Lambert(nx, -0.4, nz), 0.8) + (y > y1 - 0.9 ? 0.12 : 0);
        });
    }
}

// --- Entablature ----------------------------------------------------------------------------------------------------
Paint((x, y) =>
{
    if (x < 13 || x > 107 || y < 33 || y > 45) return null;
    var across = (x - 13) / 94;
    if (y < 35) return 0.1;                                                              // lit fillet
    if (y > 43.6) return 0.86;                                                           // shadow line
    if (y is > 38.5 and < 39.6) return 0.6;                                              // taenia
    if (y > 39.6 && (int)((x - 13) / 4.7) % 2 == 0) return 0.36 + 0.08 * Math.Sin((x - 13) * 2.68) + 0.08 * across; // triglyphs
    return 0.14 + 0.1 * across;
});

// --- Pediment -------------------------------------------------------------------------------------------------------
const double ApexX = 60, ApexY = 10, BaseY = 33, BaseL = 9, BaseR = 111;
Paint((x, y) =>
{
    if (y < ApexY || y > BaseY) return null;
    var t = (y - ApexY) / (BaseY - ApexY);
    var left = ApexX - t * (ApexX - BaseL); var right = ApexX + t * (BaseR - ApexX);
    if (x < left || x > right) return null;
    var fromLeft = x - left; var fromRight = right - x;
    if (fromLeft < 1.1) return 0.12;                                                     // lit raking edge
    if (fromRight < 1.1) return 0.82;                                                    // shaded raking edge
    if (Math.Min(fromLeft, fromRight) < 2.6 || y > BaseY - 1.6) return 0.66;             // cornice
    return 0.1 + 0.18 * t;                                                               // tympanum
});

void Write(string text, int row, string tone)
{
    var start = (Cols - text.Length) / 2;
    for (var i = -2; i < text.Length + 2; i++) overlay[row, start + i] = i >= 0 && i < text.Length ? (text[i], tone) : (' ', tone);
}
Write("L A T I H A N   S O A L", (int)(25 / (2 * S)), "d");

foreach (var (sx, sy) in new[] { (32.0, 8.0), (88.0, 6.0), (3.0, 84.0), (118.0, 104.0), (26.0, 24.0), (100.0, 46.0) })
{
    overlay[(int)(sy / (2 * S)), (int)(sx / S)] = ('+', "t");
}

// --- SVG ------------------------------------------------------------------------------------------------------------
string Tone(double d) => d < 0.3 ? "l" : d < 0.64 ? "m" : "d";
string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
var svg = new StringBuilder();
var width = Cols * CellW; var height = Rows * CellH;
svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(width)} {F(height)}\" xml:space=\"preserve\" role=\"img\" aria-label=\"LatihanSoal temple of five pillars in ASCII art\">");
svg.Append("<style>text{font:700 9px ui-monospace,Menlo,Consolas,'Courier New',monospace;white-space:pre}.l{fill:#6fa3f5}.m{fill:#2a6fe6}.d{fill:#0f3d8a}.t{fill:#ffb020}</style>");
for (var r = 0; r < Rows; r++)
{
    var line = new StringBuilder();
    string? open = null;
    var any = false;
    for (var c = 0; c < Cols; c++)
    {
        char ch; string tone;
        if (overlay[r, c] is { } o) { (ch, tone) = o; }
        else if (!painted[r, c]) { ch = ' '; tone = ""; }
        else
        {
            var d = Clamp(density[r, c] + (Hash(r, c) - 0.5) * 0.08);                      // a little noise breaks up bands
            ch = Ramp[(int)Math.Round(d * (Ramp.Length - 1))];
            tone = Tone(d);
        }

        if (ch == ' ')
        {
            if (open is not null) { line.Append("</tspan>"); open = null; }
            line.Append(' ');
            continue;
        }

        any = true;
        if (open != tone)
        {
            if (open is not null) line.Append("</tspan>");
            line.Append("<tspan class=\"").Append(tone).Append("\">");
            open = tone;
        }

        line.Append(ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => ch.ToString() });
    }

    if (open is not null) line.Append("</tspan>");
    if (any) svg.Append($"<text x=\"0\" y=\"{F((r + 1) * CellH - 2.2)}\" textLength=\"{F(width)}\" lengthAdjust=\"spacingAndGlyphs\">{line}</text>");
}

svg.Append("</svg>");
var root = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();
var output = Path.GetFullPath(Path.Combine(root, "..", "..", "src", "Akusiap.Web", "wwwroot", "img", "hero-ascii.svg"));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, svg.ToString());
Console.WriteLine($"Wrote {output} ({svg.Length / 1024.0:0.0} KB)");
