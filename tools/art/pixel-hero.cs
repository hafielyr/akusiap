#:property TreatWarningsAsErrors=false
#:property AnalysisLevel=none
#:property EnforceCodeStyleInBuild=false
// Generates src/Akusiap.Web/wwwroot/img/hero-pixel.svg (BRD-06): a pixel-art park on a sunny day in Jakarta, with
// Monas on the skyline, clouds, trees, flowers and an open exam book with a pencil in the grass. 192 x 108 pixels,
// written as one SVG path per colour so it scales crisply. Run from the repo root: dotnet run tools/art/pixel-hero.cs
using System.Text;

const int W = 192, H = 108;
var px = new string[W, H];
var rng = new Random(20261023);
int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
void Set(int x, int y, string c) { if (x >= 0 && x < W && y >= 0 && y < H) px[x, y] = c; }
bool In(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;

// --- Sky: banded gradient with ordered dithering between bands ----------------------------------------------------
string[] sky = ["#1c63c9", "#2373d8", "#2d84e3", "#3c95ea", "#53a7f0", "#6db8f4", "#8bc9f7", "#a9d8fa"];
for (var y = 0; y < H; y++)
{
    var t = Math.Min(1.0, y / 70.0) * (sky.Length - 1);
    var band = (int)t; var frac = t - band;
    for (var x = 0; x < W; x++)
    {
        var next = band < sky.Length - 1 && frac * 16 > bayer[y % 4, x % 4];
        Set(x, y, sky[next ? band + 1 : band]);
    }
}

// --- Shaded blobs (clouds, canopies): lit from the upper left ------------------------------------------------------
void Blob((double X, double Y, double R)[] circles, double floorY, string light, string mid, string shade, string deep, double texture = 0)
{
    bool Inside(int x, int y) => y <= floorY && circles.Any(c => (x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) <= c.R * c.R);
    for (var y = 0; y < H; y++)
    for (var x = 0; x < W; x++)
    {
        if (!Inside(x, y)) continue;
        string col;
        if (!Inside(x - 2, y - 2)) col = light;
        else if (!Inside(x + 3, y + 3)) col = deep;
        else if (!Inside(x + 1, y + 2)) col = shade;
        else col = mid;
        if (texture > 0 && col == mid && rng.NextDouble() < texture) col = rng.NextDouble() < 0.6 ? shade : light;
        Set(x, y, col);
    }
}

const string CloudLight = "#ffffff", CloudMid = "#f1f8ff", CloudShade = "#d2e6fb", CloudDeep = "#a9cdf2";
Blob([(146, 30, 12), (158, 22, 13), (171, 30, 12), (134, 38, 9), (182, 38, 9), (160, 36, 12)], 44, CloudLight, CloudMid, CloudShade, CloudDeep);
Blob([(30, 40, 7), (40, 35, 8), (51, 41, 6), (22, 44, 5)], 46, CloudLight, CloudMid, CloudShade, CloudDeep);
Blob([(88, 18, 4), (94, 15, 5), (100, 19, 4)], 21, CloudLight, CloudMid, CloudShade, CloudDeep);
Blob([(0, 60, 6), (12, 57, 7), (110, 58, 6), (124, 55, 8), (138, 59, 6), (186, 58, 7)], 62, "#f4faff", "#e3f0fd", "#cfe3f8", "#bcd8f5");

// --- Skyline with Monas ---------------------------------------------------------------------------------------------
const int Horizon = 67;
string[] blocks = ["#7ea6d8", "#6f98cd", "#88aedd"];
var bx = 84;
while (bx < 178)
{
    var w = rng.Next(4, 9); var h = rng.Next(5, 17);
    if (bx is > 114 and < 134) { bx = 134; continue; }                            // keep the space around Monas open
    var col = blocks[rng.Next(blocks.Length)];
    for (var x = bx; x < bx + w; x++)
    for (var y = Horizon - h; y < Horizon; y++)
    {
        var window = (x - bx) % 2 == 1 && (y - (Horizon - h)) % 3 == 1 && x < bx + w - 1 && rng.NextDouble() < 0.7;
        Set(x, y, x == bx + w - 1 ? "#6489bd" : window ? "#c3dcf6" : col);
    }

    bx += w + rng.Next(0, 2);
}

for (var x = 116; x <= 132; x++) for (var y = 64; y < Horizon; y++) Set(x, y, y == 64 ? "#b9c4d1" : "#9aa8b8");     // plaza
for (var y = 40; y < 64; y++) { Set(123, y, "#f4f6f9"); Set(124, y, "#dfe5ec"); Set(125, y, "#c3ccd8"); }              // obelisk
for (var x = 121; x <= 127; x++) { Set(x, 39, "#c3ccd8"); Set(x, 38, x is 121 or 127 ? "#9aa8b8" : "#dfe5ec"); }       // cup
foreach (var (x, y, c) in new[] { (124, 33, "#ffd25e"), (123, 34, "#ffc53d"), (124, 34, "#ffe08a"), (125, 34, "#ffc53d"),
                                   (123, 35, "#e89400"), (124, 35, "#ffc53d"), (125, 35, "#ffc53d"), (122, 36, "#e89400"),
                                   (123, 36, "#ffc53d"), (124, 36, "#ffd25e"), (125, 36, "#e89400"), (126, 36, "#e89400"),
                                   (123, 37, "#e89400"), (124, 37, "#ffc53d"), (125, 37, "#e89400") })
{
    Set(x, y, c);                                                                                                    // golden flame
}

// --- Hills and grass ------------------------------------------------------------------------------------------------
double Far(int x) => Horizon - 1 + 2.4 * Math.Sin(x / 13.0) + 1.4 * Math.Sin(x / 6.0 + 1);
double Mid(int x) => 73 + 3 * Math.Sin(x / 19.0 + 2) + 1.6 * Math.Sin(x / 8.0);
double Near(int x) => 82 + 3.5 * Math.Sin(x / 26.0 + 1) + 1.2 * Math.Sin(x / 9.0 + 3);
for (var x = 0; x < W; x++)
for (var y = 0; y < H; y++)
{
    string? col = null;
    if (y >= Near(x)) col = y > 96 ? "#2c7a3d" : "#368c47";
    else if (y >= Mid(x)) col = "#4a9f58";
    else if (y >= Far(x)) col = "#69b56c";
    if (col is null) continue;
    var r = rng.NextDouble();
    if (r < 0.12) col = col switch { "#2c7a3d" => "#24692f", "#368c47" => "#2c7a3d", "#4a9f58" => "#3f9150", _ => "#5aa863" };
    else if (r > 0.95) col = col switch { "#2c7a3d" => "#4c9e4f", "#368c47" => "#57ad5a", "#4a9f58" => "#6ec070", _ => "#84cc7c" };
    Set(x, y, col);
}

for (var x = 0; x < W; x++)                                                      // light grass tips along each ridge
{
    foreach (var edge in new[] { Mid(x), Near(x) })
    {
        var y = (int)Math.Ceiling(edge);
        if (rng.NextDouble() < 0.5) Set(x, y, "#7fcb72");
        if (rng.NextDouble() < 0.2) Set(x, y - 1, "#7fcb72");
    }
}

// --- Path -----------------------------------------------------------------------------------------------------------
for (var i = 0; i <= 400; i++)
{
    var s = i / 400.0;
    var x = (1 - s) * (1 - s) * -4 + 2 * (1 - s) * s * 64 + s * s * 118;          // quadratic Bezier
    var y = (1 - s) * (1 - s) * 110 + 2 * (1 - s) * s * 84 + s * s * 74;
    var half = 6.5 * (1 - s) + 0.6;
    for (var dx = -half; dx <= half; dx += 0.5)
    {
        var col = Math.Abs(dx) > half - 1 ? "#b48c55" : rng.NextDouble() < 0.1 ? "#c99f68" : "#dcbb83";
        Set((int)(x + dx), (int)y, col);
    }
}

// --- Trees ----------------------------------------------------------------------------------------------------------
void Trunk(int x0, int x1, int y0, int y1)
{
    for (var x = x0; x <= x1; x++) for (var y = y0; y <= y1; y++) Set(x, y, x == x0 ? "#8a6340" : x == x1 ? "#4e351f" : rng.NextDouble() < 0.15 ? "#5c3f25" : "#6b4a2b");
}

Trunk(90, 92, 66, 81);
Blob([(91, 58, 8), (84, 63, 6), (98, 63, 6), (91, 52, 5), (86, 56, 5), (96, 56, 5)], 70, "#6cc163", "#3f9a4b", "#2e7d3a", "#1f5f2b", 0.18);
Trunk(178, 186, 26, 107);
Blob([(190, 8, 20), (174, 2, 14), (186, 30, 13), (168, 20, 10), (160, 6, 8)], 44, "#5fb35a", "#2f8a43", "#22703a", "#185a2d", 0.2);
Blob([(38, 78, 4), (44, 76, 5), (50, 78, 4)], 81, "#6cc163", "#3f9a4b", "#2e7d3a", "#1f5f2b", 0.15);    // bushes
Blob([(70, 82, 4), (76, 80, 5)], 84, "#6cc163", "#3f9a4b", "#2e7d3a", "#1f5f2b", 0.15);

// --- The open exam book with a pencil ------------------------------------------------------------------------------
for (var x = 108; x <= 168; x++) for (var y = 100; y <= 106; y++)                // soft shadow on the grass
{
    var dx = (x - 138) / 30.0; var dy = (y - 103) / 3.2;
    if (dx * dx + dy * dy < 1) Set(x, y, "#24692f");
}

bool InPoly((double X, double Y)[] p, double x, double y)
{
    var inside = false;
    for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
    {
        if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) inside = !inside;
    }

    return inside;
}

(double, double)[] cover = [(110, 89), (139, 92), (167, 88), (170, 101), (139, 105), (108, 102)];
(double, double)[] left = [(112, 87), (139, 90), (139, 102), (110, 99)];
(double, double)[] right = [(139, 90), (165, 86), (167, 98), (139, 102)];
for (var x = 100; x < 175; x++)
for (var y = 80; y < 108; y++)
{
    if (InPoly(cover, x + 0.5, y + 0.5)) Set(x, y, y > 100 ? "#12428f" : "#1f6feb");
    var onLeft = InPoly(left, x + 0.5, y + 0.5); var onRight = InPoly(right, x + 0.5, y + 0.5);
    if (!onLeft && !onRight) continue;
    var nearSpine = Math.Abs(x - 139) < 3;
    Set(x, y, nearSpine ? "#e6dcc4" : onLeft && x < 113 ? "#efe7d3" : "#fbf8ee");
    // Ruled answer lines on both pages.
    var rel = onLeft ? y - (87 + (x - 112) * 3.0 / 27) : y - (90 - (x - 139) * 4.0 / 26);
    if (rel > 2.5 && (int)rel % 2 == 1 && !nearSpine && (onLeft ? x is > 114 and < 136 : x is > 146 and < 163) && (x + (int)rel) % 7 != 0) Set(x, y, "#a9b8c9");
}

// Green ticks next to the answers on the right page.
foreach (var (cx, cy) in new[] { (143, 92), (143, 96), (143, 100) })
{
    foreach (var (dx, dy) in new[] { (0, 0), (1, 1), (2, 0), (3, -1), (4, -2) }) Set(cx + dx, cy + dy, "#2fa84f");
}

// The pencil, lying across the right page.
for (var i = 0; i <= 24; i++)
{
    var x = 150 + i; var y = 101 - i * 9 / 24;
    var col = i < 2 ? "#3a3a3a" : i < 4 ? "#f2d2a9" : i < 20 ? "#ffc53d" : i < 21 ? "#c0c7d0" : "#ff7a8a";
    Set(x, y, col);
    Set(x, y + 1, i < 2 ? "#3a3a3a" : i < 4 ? "#d9b48a" : i < 20 ? "#e89400" : i < 21 ? "#9aa3ae" : "#e45b6c");
}

// --- Flowers --------------------------------------------------------------------------------------------------------
string[] petals = ["#ff6b6b", "#ffd166", "#ffffff", "#c77dff", "#ff9f43"];
for (var n = 0; n < 70; n++)
{
    var x = rng.Next(0, W); var y = rng.Next(86, H);
    if (x is > 104 and < 172 && y > 84) continue;                                  // not on the book
    if (x is > 176 and < 188) continue;                                            // not on the trunk
    if (!In(x, y) || px[x, y] is "#dcbb83" or "#c99f68" or "#b48c55") continue;   // not on the path
    var c = petals[rng.Next(petals.Length)];
    Set(x, y, c); Set(x - 1, y, c); Set(x + 1, y, c); Set(x, y - 1, c); Set(x, y + 1, "#2c7a3d");
    Set(x, y, "#ffe08a");
}

// --- SVG: one path per colour, made of horizontal runs ---------------------------------------------------------------
var runs = new Dictionary<string, StringBuilder>();
for (var y = 0; y < H; y++)
{
    var x = 0;
    while (x < W)
    {
        var c = px[x, y]; var start = x;
        while (x < W && px[x, y] == c) x++;
        if (!runs.TryGetValue(c, out var sb)) runs[c] = sb = new StringBuilder();
        sb.Append('M').Append(start).Append(' ').Append(y).Append('h').Append(x - start).Append("v1h-").Append(x - start).Append('z');
    }
}

var svg = new StringBuilder();
svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"Pixel-art park in Jakarta with Monas and an open exam book\">");
foreach (var (color, path) in runs.OrderByDescending(r => r.Value.Length)) svg.Append($"<path fill=\"{color}\" d=\"{path}\"/>");
svg.Append("</svg>");
var root = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();
var output = Path.GetFullPath(Path.Combine(root, "..", "..", "src", "Akusiap.Web", "wwwroot", "img", "hero-pixel.svg"));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, svg.ToString());
Console.WriteLine($"Wrote {output} ({svg.Length / 1024.0:0.0} KB, {runs.Count} colours)");
