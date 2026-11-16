using System.Drawing;


namespace IconSwap.Core.Art;

/// <summary>Draws the Xbox replacements for the UFC 3 PS4 button skin.</summary>
public sealed class XboxArt
{
    private readonly EbArchive _eb;
    private readonly string _prefix;

    // Xbox One face button colours, and lighter versions for the large dark style.
    private static readonly Dictionary<string, (string Letter, Color Color, Color Light)> Face = new()
    {
        ["digital_one"] = ("A", Color.FromArgb(107, 190, 70), Color.FromArgb(160, 222, 128)),
        ["digital_two"] = ("B", Color.FromArgb(232, 60, 60), Color.FromArgb(246, 140, 140)),
        ["digital_three"] = ("Y", Color.FromArgb(250, 196, 30), Color.FromArgb(252, 222, 120)),
        ["digital_four"] = ("X", Color.FromArgb(40, 130, 240), Color.FromArgb(140, 185, 248)),
    };

    public XboxArt(EbArchive eb, string prefix) { _eb = eb; _prefix = prefix; }

    private RgbaImage Original(string rel)
    {
        var e = _eb.Find(_prefix + rel + ".gnf") ?? throw new FileNotFoundException(rel);
        return GnfTexture.Parse(_eb.Read(e)).Decode();
    }

    public List<(string Rel, RgbaImage Img)> RenderAll()
    {
        var list = new List<(string, RgbaImage)>();
        void Add(string rel, Func<RgbaImage, RgbaImage> draw) => list.Add((rel, draw(Original(rel))));

        const string B = "skins/buttons/ps3/";
        foreach (var (name, (letter, color, light)) in Face)
        {
            Add(B + name, o => FaceDark(o, letter, color, ring: true));
            Add(B + "black_large/" + name, o => FaceDark(o, letter, light, ring: false));
            Add(B + "white/" + name, o => FaceLight(o, letter));
            Add(B + "white_large/" + name, o => FaceLight(o, letter));
            Add(B + "ue_" + name, o => FaceOutline(o, letter));
        }

        foreach (var dir in new[] { "", "white/" })
        {
            Add(B + dir + "left_bumper", o => Relabel(o, "LB"));
            Add(B + dir + "right_bumper", o => Relabel(o, "RB"));
            Add(B + dir + "left_trigger", o => Relabel(o, "LT"));
            Add(B + dir + "right_trigger", o => Relabel(o, "RT"));
            Add(B + dir + "left_right_bumper", o => Relabel(o, "LB", "RB"));
            Add(B + dir + "left_right_trigger", o => Relabel(o, "LT", "RT"));
            Add(B + dir + "start", o => Relabel(o, Label.Menu));
            Add(B + dir + "select", o => Relabel(o, Label.View));
            Add(B + dir + "guide", o => Relabel(o, Label.Guide));
            Add(B + dir + "ras_c", o => Relabel(o, "RS"));
        }
        Add(B + "las_c", o => Relabel(o, "LS"));
        foreach (var (n, t) in new[] { ("left_bumper", "LB"), ("right_bumper", "RB"), ("left_trigger", "LT"), ("right_trigger", "RT") })
            Add(B + "text_ground_game_hud/" + n, o => Relabel(o, t));

        Add(B + "careehubmainicon", o => Relabel(o, new Rectangle(0, (int)(o.Height * 0.78), o.Width, o.Height), Label.Menu));
        Add(B + "controller", Controller);
        Add(B + "controller_white", Controller);

        // Controls screen diagrams (the ones definition.kettle.assetpaths points to).
        const string D = "skins/settings/controller/ps3/";
        foreach (var n in new[] { "clinch", "ground", "standup", "loading" })
            Add(D + n, o => Diagram(o, new[]
            {
                (Rectangle.FromLTRB(136, 58, 147, 65), "LT"), (Rectangle.FromLTRB(509, 58, 520, 65), "RT"),
                (Rectangle.FromLTRB(132, 82, 141, 89), "LB"), (Rectangle.FromLTRB(514, 82, 523, 89), "RB"),
                (Rectangle.FromLTRB(186, 134, 218, 142), "VIEW"), (Rectangle.FromLTRB(433, 134, 476, 142), "MENU"),
            }));
        Add(D + "tower", o => Diagram(o, new[]
        {
            (Rectangle.FromLTRB(139, 50, 148, 56), "LT"), (Rectangle.FromLTRB(513, 50, 524, 56), "RT"),
            (Rectangle.FromLTRB(135, 73, 143, 78), "LB"), (Rectangle.FromLTRB(518, 73, 527, 78), "RB"),
            (Rectangle.FromLTRB(190, 125, 221, 133), "VIEW"), (Rectangle.FromLTRB(436, 125, 479, 133), "MENU"),
        }));
        Add(D + "left_plus_right_triggers", o => Relabel(o, "LT", "RT"));
        return list;
    }

    // ------------------------------------------------------------------ controls diagrams

    /// <summary>
    /// Keeps the controller drawing and its leader lines, but turns the coloured
    /// triangle/circle/cross/square into Y/B/A/X and rewrites the given text labels.
    /// </summary>
    private static RgbaImage Diagram(RgbaImage o, (Rectangle Box, string Text)[] labels)
    {
        const int Faint = 24;
        int W = o.Width, H = o.Height;
        var px = (byte[])o.Pixels.Clone();
        int Chroma(int i) => Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])) - Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));

        // Coloured symbols = saturated connected shapes.
        var seen = new bool[W * H];
        var symbols = new List<(Rectangle Box, byte Alpha)>();
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int s = y * W + x;
            if (seen[s] || px[s * 4 + 3] < 60 || Chroma(s * 4) < 50) continue;
            var stack = new Stack<int>(); stack.Push(s); seen[s] = true;
            int x0 = x, x1 = x, y0 = y, y1 = y, n = 0; byte amax = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop(); n++;
                int cx = i % W, cy = i / W;
                x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx); y0 = Math.Min(y0, cy); y1 = Math.Max(y1, cy);
                amax = Math.Max(amax, px[i * 4 + 3]);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int j = ny * W + nx;
                    if (seen[j] || px[j * 4 + 3] < 60 || Chroma(j * 4) < 50) continue;
                    seen[j] = true; stack.Push(j);
                }
            }
            if (n >= 15) symbols.Add((Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1), amax));
        }
        if (symbols.Count != 4) throw new InvalidOperationException($"На схеме найдено цветных символов: {symbols.Count}");

        // Layout of the diamond decides the letter: top Y, bottom A, left X, right B.
        var top = symbols.OrderBy(s => s.Box.Y).First();
        var bottom = symbols.OrderBy(s => s.Box.Y).Last();
        var left = symbols.OrderBy(s => s.Box.X).First();
        var right = symbols.OrderBy(s => s.Box.X).Last();
        var draws = new List<Action<Painter>>();
        foreach (var (sym, key) in new[] { (top, "digital_three"), (right, "digital_two"), (bottom, "digital_one"), (left, "digital_four") })
        {
            var e = Rectangle.Inflate(sym.Box, 2, 2);
            for (int y = Math.Max(0, e.Top); y < Math.Min(H, e.Bottom); y++)
            for (int x = Math.Max(0, e.Left); x < Math.Min(W, e.Right); x++)
            {
                int i = (y * W + x) * 4;
                if (Chroma(i) >= 25) px[i + 3] = 0;
            }
            var (letter, color, _) = Face[key];
            var c = Color.FromArgb(sym.Alpha, color);
            var box = new RectangleF(sym.Box.X - 4, sym.Box.Y - 2, sym.Box.Width + 8, sym.Box.Height + 4);
            draws.Add(p => p.Text(letter, c, box, sym.Box.Height * 1.05f));
        }

        // Text labels: erase the shapes lying entirely inside the label box (lines passing
        // through touch its border and are kept), redraw in the same colour.
        foreach (var (rect, text) in labels)
        {
            var e = Rectangle.Inflate(rect, 2, 2);
            long r = 0, g = 0, b = 0, wsum = 0; byte amax = 0;
            var visited = new HashSet<int>();
            for (int y = e.Top; y < e.Bottom; y++)
            for (int x = e.Left; x < e.Right; x++)
            {
                int s = y * W + x;
                if (px[s * 4 + 3] < Faint || !visited.Add(s)) continue;
                var shape = new List<int>();
                bool touchesBorder = false;
                var stack = new Stack<int>(); stack.Push(s);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); shape.Add(i);
                    int cx = i % W, cy = i / W;
                    if (cx == e.Left || cy == e.Top || cx == e.Right - 1 || cy == e.Bottom - 1) touchesBorder = true;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < e.Left || ny < e.Top || nx >= e.Right || ny >= e.Bottom) continue;
                        int j = ny * W + nx;
                        if (px[j * 4 + 3] < Faint || !visited.Add(j)) continue;
                        stack.Push(j);
                    }
                }
                if (touchesBorder) continue;
                foreach (int i in shape)
                {
                    int a = px[i * 4 + 3];
                    r += px[i * 4] * a; g += px[i * 4 + 1] * a; b += px[i * 4 + 2] * a; wsum += a;
                    amax = Math.Max(amax, (byte)a);
                    px[i * 4 + 3] = 0;
                }
            }
            if (wsum == 0) throw new InvalidOperationException($"Нет надписи в {rect}");
            // Faint anti-aliasing left around the erased letters.
            for (int y = rect.Top; y < rect.Bottom; y++)
            for (int x = rect.Left; x < rect.Right; x++)
                if (px[(y * W + x) * 4 + 3] < Faint) px[(y * W + x) * 4 + 3] = 0;
            var c = Color.FromArgb(amax, (int)(r / wsum), (int)(g / wsum), (int)(b / wsum));
            float mid = rect.X + rect.Width / 2f;
            float width = Math.Max(rect.Width, Painter.TextWidth(text, rect.Height)) + 2;
            var box = new RectangleF(mid - width / 2, rect.Y, width, rect.Height);
            draws.Add(p => p.Text(text, c, box, rect.Height, FontStyle.Bold));
        }

        using var painter = new Painter(W, H);
        foreach (var d in draws) d(painter);
        return ImageOps.Over(painter.Result(), new RgbaImage(W, H, px));
    }

    // ------------------------------------------------------------------ face buttons

    private static (float cx, float cy, float r) Disc(RgbaImage o)
    {
        var b = ImageOps.AlphaBounds(o, 100);
        float d = Math.Min(b.Width, b.Height);
        return (b.X + b.Width / 2f, b.Y + b.Height / 2f, d / 2f);
    }

    private static void Letter(Painter p, string letter, Color c, float cx, float cy, float r) =>
        p.Text(letter, c, new RectangleF(cx - r, cy - r, r * 2, r * 2), r * 0.98f);

    /// <summary>Black disc with a coloured letter (and coloured rim, like the original).</summary>
    private static RgbaImage FaceDark(RgbaImage o, string letter, Color color, bool ring)
    {
        var (cx, cy, r) = Disc(o);
        using var p = new Painter(o.Width, o.Height);
        p.FillCircle(Color.Black, cx, cy, r);
        if (ring) p.StrokeCircle(color, cx, cy, r - r * 0.09f, r * 0.17f);
        Letter(p, letter, color, cx, cy, r * (ring ? 0.92f : 1f));
        return p.Result();
    }

    /// <summary>White disc with a dark letter.</summary>
    private static RgbaImage FaceLight(RgbaImage o, string letter)
    {
        var (cx, cy, r) = Disc(o);
        var dark = ImageOps.AverageColor(o, l => l < 140);
        if (dark.IsEmpty) dark = Color.FromArgb(75, 75, 85);
        using var p = new Painter(o.Width, o.Height);
        p.FillCircle(Color.White, cx, cy, r);
        Letter(p, letter, dark, cx, cy, r);
        return p.Result();
    }

    /// <summary>Thin white ring with a white letter.</summary>
    private static RgbaImage FaceOutline(RgbaImage o, string letter)
    {
        var (cx, cy, r) = Disc(o);
        using var p = new Painter(o.Width, o.Height);
        float w = Math.Max(1.1f, r * 0.08f);
        p.StrokeCircle(Color.White, cx, cy, r - w / 2, w);
        Letter(p, letter, Color.White, cx, cy, r * 0.95f);
        return p.Result();
    }

    // ------------------------------------------------------------------ relabelling

    private enum Label { Text, Menu, View, Guide }

    private RgbaImage Relabel(RgbaImage o, params string[] labels) =>
        Relabel(o, new Rectangle(0, 0, o.Width, o.Height), labels.Select(l => (Label.Text, l)).ToArray());

    private RgbaImage Relabel(RgbaImage o, Label kind) => Relabel(o, new Rectangle(0, 0, o.Width, o.Height), (kind, ""));

    private RgbaImage Relabel(RgbaImage o, Rectangle area, Label kind) => Relabel(o, area, (kind, ""));

    /// <summary>
    /// Keeps the original badge shape, erases its dark lettering and draws a new label in the
    /// same place and colour. One label per badge (connected shape), left to right.
    /// </summary>
    private static RgbaImage Relabel(RgbaImage o, Rectangle area, params (Label Kind, string Text)[] labels)
    {
        int W = o.Width, H = o.Height;
        var px = (byte[])o.Pixels.Clone();
        bool Solid(int x, int y) => x >= area.Left && y >= area.Top && x < area.Right && y < area.Bottom && px[(y * W + x) * 4 + 3] > 128;

        // Connected badges.
        var comp = new int[W * H];
        var badges = new List<List<int>>();
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (!Solid(x, y) || comp[y * W + x] != 0) continue;
            var pixels = new List<int>();
            var stack = new Stack<int>();
            stack.Push(y * W + x);
            comp[y * W + x] = badges.Count + 1;
            while (stack.Count > 0)
            {
                int i = stack.Pop(); pixels.Add(i);
                int cx = i % W, cy = i / W;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H || comp[ny * W + nx] != 0 || !Solid(nx, ny)) continue;
                    comp[ny * W + nx] = badges.Count + 1;
                    stack.Push(ny * W + nx);
                }
            }
            if (pixels.Count >= 12) badges.Add(pixels);
        }
        badges.Sort((a, b) => a.Min(i => i % W).CompareTo(b.Min(i => i % W)));
        if (badges.Count > labels.Length)
        {
            // Keep the shapes that actually carry dark lettering.
            badges = badges.Where(b =>
            {
                var lums = b.Select(i => ImageOps.Lum(px, i * 4)).OrderBy(l => l).ToList();
                return lums[lums.Count * 3 / 4] - lums[lums.Count / 20] > 70;
            }).ToList();
        }
        if (badges.Count != labels.Length)
            throw new InvalidOperationException($"Ожидалось плашек: {labels.Length}, найдено: {badges.Count}");

        var overlays = new List<Action<Painter>>();
        for (int k = 0; k < badges.Count; k++)
        {
            int id = comp[badges[k][0]];
            bool Inside(int x, int y, int margin)
            {
                for (int dy = -margin; dy <= margin; dy++)
                for (int dx = -margin; dx <= margin; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H || comp[ny * W + nx] != id) return false;
                }
                return true;
            }

            // Fill = light colour of the badge interior, ink = its dark lettering.
            var interior = badges[k].Where(i => Inside(i % W, i / W, 2)).ToList();
            int fillLum = interior.Select(i => ImageOps.Lum(px, i * 4)).OrderBy(l => l).ElementAt(interior.Count * 3 / 4);
            // Dark pixels connected to the badge edge are its outline, not lettering.
            var dark = new HashSet<int>(badges[k].Where(i => ImageOps.Lum(px, i * 4) < fillLum - 70));
            var outline = new HashSet<int>();
            var edge = badges[k].Where(i => !Inside(i % W, i / W, 1)).ToList();
            bool outlined = edge.Count(dark.Contains) > edge.Count / 2;
            var queue = new Queue<int>(outlined ? edge.Where(dark.Contains) : Enumerable.Empty<int>());
            foreach (var i in queue) outline.Add(i);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                foreach (var n in new[] { i - 1, i + 1, i - W, i + W })
                    if (dark.Contains(n) && outline.Add(n)) queue.Enqueue(n);
            }
            var ink = interior.Where(i => dark.Contains(i) && !outline.Contains(i)).ToList();
            if (ink.Count == 0) throw new InvalidOperationException("Не найден текст на плашке");
            var fill = Avg(px, interior.Where(i => ImageOps.Lum(px, i * 4) >= fillLum - 20));
            var inkColor = Avg(px, ink.OrderBy(i => ImageOps.Lum(px, i * 4)).Take(Math.Max(1, ink.Count / 2)));
            int ix0 = ink.Min(i => i % W), ix1 = ink.Max(i => i % W) + 1, iy0 = ink.Min(i => i / W), iy1 = ink.Max(i => i / W) + 1;

            // Erase the old lettering (anti-aliased edges included).
            for (int y = Math.Max(0, iy0 - 1); y < Math.Min(H, iy1 + 1); y++)
            for (int x = Math.Max(0, ix0 - 1); x < Math.Min(W, ix1 + 1); x++)
            {
                if (!Inside(x, y, 1)) continue;
                int o4 = (y * W + x) * 4;
                px[o4] = fill.R; px[o4 + 1] = fill.G; px[o4 + 2] = fill.B;
            }

            // Room for the new label: the lettering box widened to the badge interior.
            var room = interior.Where(i => i / W >= iy0 && i / W < iy1 && !outline.Contains(i)).ToList();
            int bx0 = room.Min(i => i % W);
            int bx1 = room.Max(i => i % W) + 1;
            float centerX = (ix0 + ix1) / 2f;
            float half = Math.Min(centerX - bx0, bx1 - centerX) - 1.5f;
            var box = new RectangleF(centerX - half, iy0, half * 2, iy1 - iy0);
            var (kind, text) = labels[k];
            overlays.Add(p => DrawLabel(p, kind, text, box, inkColor, fill));
        }

        var baseImg = new RgbaImage(W, H, px);
        using var painter = new Painter(W, H);
        foreach (var draw in overlays) draw(painter);
        return ImageOps.Over(painter.Result(), baseImg);
    }

    private static void DrawLabel(Painter p, Label kind, string text, RectangleF box, Color ink, Color fill)
    {
        float h = box.Height;
        switch (kind)
        {
            case Label.Text:
                p.Text(text, ink, box, h, squeeze: 0.96f);
                break;
            case Label.Menu:
            {
                // "≡ MENU" when there is room for it, otherwise only the glyph.
                float g = h * 1.05f, gap = h * 0.35f;
                float tw = Painter.TextWidth("MENU", h);
                if (g + gap + tw <= box.Width)
                {
                    float x = box.X + (box.Width - (g + gap + tw)) / 2;
                    p.MenuGlyph(ink, new RectangleF(x, box.Y + (h - g) / 2, g, g));
                    p.Text("MENU", ink, new RectangleF(x + g + gap, box.Y, tw, h), h);
                }
                else p.MenuGlyph(ink, CenterSquare(box, Math.Min(box.Width, h * 1.1f)));
                break;
            }
            case Label.View:
                p.ViewGlyph(ink, fill, CenterSquare(box, Math.Min(box.Width, box.Height) * 0.95f));
                break;
            case Label.Guide:
            {
                float r = Math.Min(box.Width, box.Height) / 2f * 1.02f;
                p.XboxLogo(ink, fill, box.X + box.Width / 2, box.Y + box.Height / 2, r);
                break;
            }
        }
    }

    private static RectangleF CenterSquare(RectangleF box, float s) =>
        new(box.X + (box.Width - s) / 2, box.Y + (box.Height - s) / 2, s, s);

    private static Color Avg(byte[] px, IEnumerable<int> idx)
    {
        long r = 0, g = 0, b = 0, n = 0;
        foreach (var i in idx) { r += px[i * 4]; g += px[i * 4 + 1]; b += px[i * 4 + 2]; n++; }
        return n == 0 ? Color.White : Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
    }

    // ------------------------------------------------------------------ controller icon

    private static RgbaImage Controller(RgbaImage o)
    {
        var b = ImageOps.AlphaBounds(o, 40);
        var c = ImageOps.AverageColor(o, _ => true, minAlpha: 120);
        if (c.IsEmpty) c = Color.White;
        // Keep the original line opacity.
        int maxA = 0;
        for (int i = 3; i < o.Pixels.Length; i += 4) maxA = Math.Max(maxA, o.Pixels[i]);
        using var p = new Painter(o.Width, o.Height);
        p.XboxController(Color.FromArgb(maxA, c), new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1), Math.Max(1.1f, b.Width / 64f));
        return p.Result();
    }
}

