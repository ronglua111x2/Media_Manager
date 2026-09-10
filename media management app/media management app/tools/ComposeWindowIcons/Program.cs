using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

internal static class Program
{
    private static readonly int[] IcoSizes = [16, 20, 24, 32, 48, 256];
    private const int MasterSize = 256;

    private static int Main()
    {
        var appRoot = FindAppRoot();
        if (appRoot is null)
        {
            Console.Error.WriteLine("Could not find Assets/app-icon.ico from the current directory.");
            return 1;
        }

        var assets = Path.Combine(appRoot, "Assets");
        var badges = Path.Combine(assets, "window-icons", "badges");
        var outputDir = Path.Combine(assets, "window-icons");
        Directory.CreateDirectory(badges);
        Directory.CreateDirectory(outputDir);

        var appIconPath = Path.Combine(assets, "app-icon.ico");
        using var appIcon = LoadLargestBitmap(appIconPath);

        using var consoleBadge = DrawConsoleBadge(MasterSize);
        SavePng(consoleBadge, Path.Combine(badges, "console.png"));

        using var qbitBadge = LoadQbittorrentBadge(badges, out var writeQbitPng);
        if (writeQbitPng)
        {
            SavePng(qbitBadge, Path.Combine(badges, "qbittorrent.png"));
        }

        var jellyfinPath = Path.Combine(badges, "jellyfin.png");
        if (!File.Exists(jellyfinPath))
        {
            Console.Error.WriteLine($"Missing {jellyfinPath}");
            return 1;
        }

        using var jellyfinBadge = new Bitmap(jellyfinPath);

        WriteWindowIcon(outputDir, "console-log.ico", appIcon, consoleBadge);
        WriteWindowIcon(outputDir, "qbittorrent.ico", appIcon, qbitBadge);
        WriteWindowIcon(outputDir, "jellyfin.ico", appIcon, jellyfinBadge);

        Console.WriteLine($"Wrote window icons to {outputDir}");
        return 0;
    }

    private static string? FindAppRoot()
    {
        var candidates = new List<DirectoryInfo>();
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
        {
            candidates.Add(new DirectoryInfo(AppContext.BaseDirectory));
        }

        candidates.Add(new DirectoryInfo(Directory.GetCurrentDirectory()));

        foreach (var start in candidates)
        {
            for (var dir = start; dir is not null; dir = dir.Parent)
            {
                var icon = Path.Combine(dir.FullName, "Assets", "app-icon.ico");
                if (File.Exists(icon))
                {
                    return dir.FullName;
                }
            }
        }

        return null;
    }

    private static Bitmap LoadQbittorrentBadge(string badgesDir, out bool writePng)
    {
        var png = Path.Combine(badgesDir, "qbittorrent.png");
        if (File.Exists(png))
        {
            writePng = false;
            return CloneBitmap(png);
        }

        var ico = Path.Combine(badgesDir, "qbittorrent.ico");
        if (File.Exists(ico))
        {
            writePng = true;
            return LoadLargestBitmap(ico);
        }

        throw new FileNotFoundException("qBittorrent badge not found (png or ico).", png);
    }

    private static Bitmap CloneBitmap(string path)
    {
        using var loaded = new Bitmap(path);
        return new Bitmap(loaded);
    }

    private static void SavePng(Bitmap bitmap, string path)
    {
        bitmap.Save(path, ImageFormat.Png);
    }

    private static Bitmap LoadLargestBitmap(string path)
    {
        using var source = new Icon(path, 256, 256);
        return new Bitmap(source.ToBitmap());
    }

    private static void WriteWindowIcon(string outputDir, string fileName, Bitmap appIcon, Bitmap badge)
    {
        using var master = Compose(appIcon, badge, MasterSize);
        var frames = new List<Bitmap>(IcoSizes.Length);
        try
        {
            foreach (var size in IcoSizes)
            {
                frames.Add(size == MasterSize ? (Bitmap)master.Clone() : Downscale(master, size));
            }

            IcoWriter.Write(Path.Combine(outputDir, fileName), frames);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private static Bitmap Compose(Bitmap appIcon, Bitmap badge, int size)
    {
        var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        ApplyQuality(g);
        g.Clear(Color.Transparent);
        g.DrawImage(appIcon, 0, 0, size, size);

        var margin = Math.Max(1, (int)Math.Round(size * 0.04));
        var badgeSize = Math.Max(6, (int)Math.Round(size * 0.38));
        var x = size - badgeSize - margin;
        var y = size - badgeSize - margin;
        var well = new Rectangle(x, y, badgeSize, badgeSize);

        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.FillEllipse(shadow, x + 1, y + 1, badgeSize, badgeSize);
        }

        using (var wellBrush = new SolidBrush(Color.FromArgb(255, 15, 20, 28)))
        {
            g.FillEllipse(wellBrush, well);
        }

        var pad = Math.Max(1, badgeSize / 14);
        var inner = new Rectangle(x + pad, y + pad, badgeSize - (pad * 2), badgeSize - (pad * 2));
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(inner);
            var state = g.Save();
            g.SetClip(clip);
            g.DrawImage(badge, inner);
            g.Restore(state);
        }

        using var border = new Pen(Color.FromArgb(255, 232, 237, 245), Math.Max(1f, size / 64f));
        border.Alignment = PenAlignment.Inset;
        g.DrawEllipse(border, well);

        return canvas;
    }

    private static Bitmap DrawConsoleBadge(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        ApplyQuality(g);
        g.Clear(Color.Transparent);

        var inset = (int)Math.Round(size * 0.06);
        var rect = new Rectangle(inset, inset, size - (inset * 2), size - (inset * 2));
        var radius = rect.Width * 0.22f;
        using (var path = RoundedRect(rect, radius))
        using (var fill = new SolidBrush(Color.FromArgb(255, 31, 41, 55)))
        {
            g.FillPath(fill, path);
        }

        using var chevron = new Pen(Color.FromArgb(255, 79, 168, 143), Math.Max(3f, size / 14f));
        chevron.StartCap = LineCap.Round;
        chevron.EndCap = LineCap.Round;
        chevron.LineJoin = LineJoin.Round;

        var cx = rect.X + (rect.Width * 0.38f);
        var cy = rect.Y + (rect.Height * 0.48f);
        var s = rect.Width * 0.16f;
        g.DrawLines(chevron, [
            new PointF(cx - (s * 0.35f), cy - s),
            new PointF(cx + (s * 0.55f), cy),
            new PointF(cx - (s * 0.35f), cy + s)
        ]);

        using var cursor = new Pen(Color.White, chevron.Width);
        cursor.StartCap = LineCap.Round;
        cursor.EndCap = LineCap.Round;
        var ux = rect.X + (rect.Width * 0.58f);
        var uy = rect.Y + (rect.Height * 0.70f);
        g.DrawLine(cursor, ux, uy, ux + (rect.Width * 0.22f), uy);

        return bitmap;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Bitmap Downscale(Bitmap source, int size)
    {
        var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        ApplyQuality(g);
        g.Clear(Color.Transparent);
        g.DrawImage(source, 0, 0, size, size);
        return dest;
    }

    private static void ApplyQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
    }
}

internal static class IcoWriter
{
    public static void Write(string path, IReadOnlyList<Bitmap> frames)
    {
        var pngs = new byte[frames.Count][];
        for (var i = 0; i < frames.Count; i++)
        {
            using var stream = new MemoryStream();
            frames[i].Save(stream, ImageFormat.Png);
            pngs[i] = stream.ToArray();
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var width = frames[i].Width;
            var height = frames[i].Height;
            writer.Write((byte)(width >= 256 ? 0 : width));
            writer.Write((byte)(height >= 256 ? 0 : height));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
        {
            writer.Write(png);
        }

        File.WriteAllBytes(path, output.ToArray());
    }
}
