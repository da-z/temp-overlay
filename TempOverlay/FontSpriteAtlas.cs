using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace TempOverlay
{
    internal static class FontSpriteAtlas
    {
        private const string DefaultCharset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Atlas> Atlases = new Dictionary<string, Atlas>(StringComparer.Ordinal);

        public static void WarmAtlas(Font font, Color fill, Color outline, int outlineWidth)
        {
            var atlas = EnsureAtlas(font, fill, outline, outlineWidth);
            for (var i = 0; i < DefaultCharset.Length; i++)
            {
                atlas.GetGlyph(DefaultCharset[i]);
            }
        }

        public static void ExportAtlasPng(Font font, Color fill, Color outline, int outlineWidth, string outputPath)
        {
            var atlas = EnsureAtlas(font, fill, outline, outlineWidth);
            for (var i = 0; i < DefaultCharset.Length; i++)
            {
                atlas.GetGlyph(DefaultCharset[i]);
            }

            const int columns = 16;
            const int cellPad = 2;
            var maxAdvance = 1;
            for (var i = 0; i < DefaultCharset.Length; i++)
            {
                var g = atlas.GetGlyph(DefaultCharset[i]);
                if (g.Advance > maxAdvance)
                {
                    maxAdvance = g.Advance;
                }
            }

            var rows = (int)Math.Ceiling(DefaultCharset.Length / (double)columns);
            var cellWidth = maxAdvance + 8;
            var cellHeight = Math.Max(1, atlas.LineHeight) + 4;
            var width = (columns * cellWidth) + ((columns + 1) * cellPad);
            var height = (rows * cellHeight) + ((rows + 1) * cellPad);

            using (var sheet = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(sheet))
            using (var gridPen = new Pen(Color.FromArgb(70, 255, 255, 255)))
            {
                g.Clear(Color.Transparent);
                for (var i = 0; i < DefaultCharset.Length; i++)
                {
                    var row = i / columns;
                    var col = i % columns;
                    var x = cellPad + (col * (cellWidth + cellPad));
                    var y = cellPad + (row * (cellHeight + cellPad));
                    var glyph = atlas.GetGlyph(DefaultCharset[i]);
                    g.DrawImageUnscaled(glyph.Sprite, x, y);
                    g.DrawRectangle(gridPen, x, y, cellWidth - 1, cellHeight - 1);
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                sheet.Save(outputPath, ImageFormat.Png);
            }
        }

        public static Size MeasureText(string text, Font font, Color fill, Color outline, int outlineWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Size(1, Math.Max(1, font.Height));
            }

            var atlas = EnsureAtlas(font, fill, outline, outlineWidth);
            var width = 0;
            for (var i = 0; i < text.Length; i++)
            {
                width += atlas.GetGlyph(text[i]).Advance;
            }

            return new Size(Math.Max(1, width), atlas.LineHeight);
        }

        public static void DrawText(Graphics g, string text, Point origin, Font font, Color fill, Color outline, int outlineWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var atlas = EnsureAtlas(font, fill, outline, outlineWidth);
            var x = origin.X;
            for (var i = 0; i < text.Length; i++)
            {
                var glyph = atlas.GetGlyph(text[i]);
                g.DrawImageUnscaled(glyph.Sprite, x, origin.Y);
                x += glyph.Advance;
            }
        }

        private static Atlas EnsureAtlas(Font font, Color fill, Color outline, int outlineWidth)
        {
            var safeOutline = outlineWidth < 0 ? 0 : outlineWidth;
            var key = BuildKey(font, fill, outline, safeOutline);
            lock (Sync)
            {
                if (!Atlases.TryGetValue(key, out var atlas))
                {
                    atlas = new Atlas(font, fill, outline, safeOutline);
                    Atlases[key] = atlas;
                }

                return atlas;
            }
        }

        private static string BuildKey(Font font, Color fill, Color outline, int outlineWidth)
        {
            return string.Concat(
                font.Name, "|",
                font.SizeInPoints.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "|",
                ((int)font.Style).ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                fill.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                outline.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                outlineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private sealed class Atlas
        {
            private readonly Font _font;
            private readonly Color _fill;
            private readonly Color _outline;
            private readonly int _outlineWidth;
            private readonly Dictionary<char, Glyph> _glyphs;

            public Atlas(Font font, Color fill, Color outline, int outlineWidth)
            {
                _font = font;
                _fill = fill;
                _outline = outline;
                _outlineWidth = outlineWidth;
                _glyphs = new Dictionary<char, Glyph>();
                var line = TextRenderer.MeasureText("Ag", _font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                LineHeight = Math.Max(1, line.Height + (_outlineWidth * 2) + 6);
            }

            public int LineHeight { get; }

            public Glyph GetGlyph(char c)
            {
                if (_glyphs.TryGetValue(c, out var glyph))
                {
                    return glyph;
                }

                glyph = RenderGlyph(c);
                _glyphs[c] = glyph;
                return glyph;
            }

            private Glyph RenderGlyph(char c)
            {
                var s = c.ToString();
                var pad = _outlineWidth + 2;
                var measured = TextRenderer.MeasureText(s, _font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                var width = Math.Max(1, measured.Width + (_outlineWidth * 2) + 6);
                var height = Math.Max(1, measured.Height + (_outlineWidth * 2) + 6);
                var advance = Math.Max(1, measured.Width);

                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bitmap))
                using (var outlineBrush = new SolidBrush(_outline))
                using (var fillBrush = new SolidBrush(_fill))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var fmt = StringFormat.GenericTypographic;

                    if (_outlineWidth > 0)
                    {
                        for (var ox = -_outlineWidth; ox <= _outlineWidth; ox++)
                        {
                            for (var oy = -_outlineWidth; oy <= _outlineWidth; oy++)
                            {
                                if (ox == 0 && oy == 0)
                                {
                                    continue;
                                }

                                g.DrawString(s, _font, outlineBrush, pad + ox, pad + oy, fmt);
                            }
                        }
                    }

                    g.DrawString(s, _font, fillBrush, pad, pad, fmt);
                }

                return new Glyph(bitmap, advance);
            }
        }

        private readonly struct Glyph
        {
            public Glyph(Bitmap sprite, int advance)
            {
                Sprite = sprite;
                Advance = advance;
            }

            public Bitmap Sprite { get; }
            public int Advance { get; }
        }
    }
}
