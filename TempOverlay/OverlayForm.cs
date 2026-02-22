using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TempOverlay
{
    internal sealed class OverlayForm : Form
    {
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int UlwAlpha = 0x2;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const int InnerPadding = 0;

        private readonly OutlineLabel _cpuLabel;
        private readonly OutlineLabel _gpuLabel;
        private readonly OutlineLabel _statusLabel;
        private readonly Timer _timer;
        private readonly TemperatureReader _reader;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly string _fontFamily;
        private OverlaySettings _settings;

        public OverlayForm()
        {
            _reader = new TemperatureReader();
            _settings = OverlaySettings.Load();
            _fontFamily = EmbeddedFontLoader.GetPreferredFamilyName("Tektur");
            ApplyStartupSetting();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Width = 1;
            Height = 1;

            _cpuLabel = BuildValueLabel("CPU: --.- C");
            _gpuLabel = BuildValueLabel("GPU: --.- C");
            _statusLabel = new OutlineLabel
            {
                Text = string.Empty,
                ForeColor = Color.WhiteSmoke,
                BackColor = Color.Transparent,
                Font = new Font(_fontFamily, 9, FontStyle.Regular),
                Location = new Point(0, 0),
                OutlineWidth = 1,
                Visible = false
            };

            ApplyVisualSettings();

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            _trayMenu.Items.Add("Settings...", null, (_, __) => OpenSettings());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Close", null, (_, __) => Close());

            _trayIcon = new NotifyIcon
            {
                Icon = GetTrayIcon(),
                Text = "TempOverlay",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _trayIcon.DoubleClick += (_, __) => OpenSettings();

            _timer = new Timer { Interval = 2000 };
            _timer.Tick += (_, __) => RefreshReadings();

            Load += (_, __) =>
            {
                RefreshReadings();
                PositionOverlay();
                RenderLayeredOverlay();
                _timer.Start();
            };

            FormClosing += (_, __) =>
            {
                _timer.Stop();
                _reader.Dispose();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayMenu.Dispose();
            };
            Resize += (_, __) =>
            {
                PositionOverlay();
                RenderLayeredOverlay();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow;
                return cp;
            }
        }

        private OutlineLabel BuildValueLabel(string initialText)
        {
            return new OutlineLabel
            {
                Text = initialText,
                ForeColor = Color.LawnGreen,
                BackColor = Color.Transparent,
                Font = new Font(_fontFamily, 18, FontStyle.Regular),
                Location = new Point(0, 0),
                OutlineWidth = 1
            };
        }

        private void PositionOverlay()
        {
            var screenBounds = Screen.PrimaryScreen.Bounds;
            var verticalPadding = Math.Max(0, _settings.VerticalPadding);
            var horizontalPadding = Math.Max(0, _settings.HorizontalPadding);
            switch (_settings.Position)
            {
                case OverlayPositionPreset.TopLeft:
                    Left = screenBounds.Left + horizontalPadding;
                    Top = screenBounds.Top + verticalPadding;
                    break;
                case OverlayPositionPreset.BottomRight:
                    Left = screenBounds.Right - Width - horizontalPadding;
                    Top = screenBounds.Bottom - Height - verticalPadding;
                    break;
                case OverlayPositionPreset.BottomLeft:
                    Left = screenBounds.Left + horizontalPadding;
                    Top = screenBounds.Bottom - Height - verticalPadding;
                    break;
                case OverlayPositionPreset.TopRight:
                default:
                    Left = screenBounds.Right - Width - horizontalPadding;
                    Top = screenBounds.Top + verticalPadding;
                    break;
            }
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_settings))
            {
                form.SettingsApplied += (_, __) => ApplySettingsFromForm(form);

                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ApplySettingsFromForm(form);
            }
        }

        private void ApplyStartupSetting()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                exePath = Application.ExecutablePath;
            }

            StartupManager.SetStartupEnabled(_settings.RunAtStartup, exePath);
        }

        private void RefreshReadings()
        {
            var snapshot = _reader.Read();

            _cpuLabel.Text = FormatTemp("CPU", snapshot.CpuCelsius);
            _gpuLabel.Text = FormatTemp("GPU", snapshot.GpuCelsius);

            if (!string.IsNullOrWhiteSpace(snapshot.Error))
            {
                _statusLabel.Text = "Error: " + snapshot.Error;
                _statusLabel.Visible = true;
            }
            else if (!snapshot.CpuCelsius.HasValue && !snapshot.GpuCelsius.HasValue)
            {
                _statusLabel.Text = "No CPU/GPU temperature sensors found.";
                _statusLabel.Visible = true;
            }
            else
            {
                _statusLabel.Text = string.Empty;
                _statusLabel.Visible = false;
            }

            UpdateOverlayBounds();
            PositionOverlay();
            RenderLayeredOverlay();
        }

        private static string FormatTemp(string sensor, float? value)
        {
            return value.HasValue ? string.Format("{0}: {1:0.0} C", sensor, value.Value) : sensor + ": --.- C";
        }

        private static Icon GetTrayIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
                // Fallback below.
            }

            return SystemIcons.Application;
        }

        private void ApplyVisualSettings()
        {
            var valueFontSize = GetValueFontSize(_settings.FontSize);
            var statusFontSize = Math.Max(8f, valueFontSize * 0.48f);

            _cpuLabel.Font = new Font(_fontFamily, valueFontSize, FontStyle.Regular);
            _gpuLabel.Font = new Font(_fontFamily, valueFontSize, FontStyle.Regular);
            _statusLabel.Font = new Font(_fontFamily, statusFontSize, FontStyle.Regular);

            _cpuLabel.OutlineColor = Color.Black;
            _gpuLabel.OutlineColor = Color.Black;
            _statusLabel.OutlineColor = Color.Black;
            _cpuLabel.OutlineWidth = 1;
            _gpuLabel.OutlineWidth = 1;
            _statusLabel.OutlineWidth = 1;

            GetThemeColors(_settings.Theme, out var cpuColor, out var gpuColor, out var statusColor);
            _cpuLabel.ForeColor = cpuColor;
            _gpuLabel.ForeColor = gpuColor;
            _statusLabel.ForeColor = statusColor;
        }

        private static float GetValueFontSize(OverlayFontSize size)
        {
            return size switch
            {
                OverlayFontSize.VerySmall => 9f,
                OverlayFontSize.Small => 11f,
                OverlayFontSize.Medium => 14f,
                OverlayFontSize.Large => 18f,
                _ => 14f
            };
        }

        private static int GetRowSpacing(OverlayFontSize size)
        {
            // Keep rows tighter than before while still scaling with font size.
            var valueFontSize = GetValueFontSize(size);
            return Math.Max(0, (int)Math.Floor(valueFontSize * 0.06f));
        }

        private static void GetThemeColors(OverlayTheme theme, out Color cpuColor, out Color gpuColor, out Color statusColor)
        {
            switch (theme)
            {
                case OverlayTheme.Ember:
                    cpuColor = Color.Orange;
                    gpuColor = Color.OrangeRed;
                    statusColor = Color.Moccasin;
                    break;
                case OverlayTheme.Ice:
                    cpuColor = Color.DeepSkyBlue;
                    gpuColor = Color.Cyan;
                    statusColor = Color.AliceBlue;
                    break;
                case OverlayTheme.Bw:
                    cpuColor = Color.White;
                    gpuColor = Color.White;
                    statusColor = Color.White;
                    break;
                case OverlayTheme.NeonMint:
                default:
                    cpuColor = Color.MediumSpringGreen;
                    gpuColor = Color.MediumOrchid;
                    statusColor = Color.WhiteSmoke;
                    break;
            }
        }

        private void ApplySettingsFromForm(SettingsForm form)
        {
            _settings.Position = form.SelectedPosition;
            _settings.VerticalPadding = form.SelectedVerticalPadding;
            _settings.HorizontalPadding = form.SelectedHorizontalPadding;
            _settings.RunAtStartup = form.RunAtStartup;
            _settings.Theme = form.SelectedTheme;
            _settings.FontSize = form.SelectedFontSize;
            _settings.Save();
            ApplyStartupSetting();
            ApplyVisualSettings();
            UpdateOverlayBounds();
            PositionOverlay();
            RenderLayeredOverlay();
        }

        private void UpdateOverlayBounds()
        {
            var rowSpacing = GetRowSpacing(_settings.FontSize);
            _cpuLabel.Location = new Point(InnerPadding, InnerPadding);
            _gpuLabel.Location = new Point(InnerPadding, _cpuLabel.Bottom + rowSpacing);

            var width = Math.Max(_cpuLabel.Width, _gpuLabel.Width);
            var bottom = _gpuLabel.Bottom;

            if (_statusLabel.Visible)
            {
                _statusLabel.Location = new Point(InnerPadding, _gpuLabel.Bottom + rowSpacing);
                width = Math.Max(width, _statusLabel.Width);
                bottom = _statusLabel.Bottom;
            }

            Width = Math.Max(1, width + (InnerPadding * 2));
            Height = Math.Max(1, bottom + InnerPadding);
        }

        private void RenderLayeredOverlay()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            using (var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawOutlinedText(g, _cpuLabel.Text, _cpuLabel.Location, _cpuLabel.Font, _cpuLabel.ForeColor, _cpuLabel.OutlineColor, _cpuLabel.OutlineWidth);
                DrawOutlinedText(g, _gpuLabel.Text, _gpuLabel.Location, _gpuLabel.Font, _gpuLabel.ForeColor, _gpuLabel.OutlineColor, _gpuLabel.OutlineWidth);
                if (_statusLabel.Visible && !string.IsNullOrEmpty(_statusLabel.Text))
                {
                    DrawOutlinedText(g, _statusLabel.Text, _statusLabel.Location, _statusLabel.Font, _statusLabel.ForeColor, _statusLabel.OutlineColor, _statusLabel.OutlineWidth);
                }

                ApplyBitmapToLayeredWindow(bitmap);
            }
        }

        private void ApplyBitmapToLayeredWindow(Bitmap bitmap)
        {
            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            var oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                var size = new Size(bitmap.Width, bitmap.Height);
                var sourcePoint = new Point(0, 0);
                var topPos = new Point(Left, Top);
                var blend = new BlendFunction
                {
                    BlendOp = AcSrcOver,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AcSrcAlpha
                };

                UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref sourcePoint, 0, ref blend, UlwAlpha);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static void DrawOutlinedText(Graphics g, string text, Point location, Font font, Color fill, Color outline, int outlineWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            using (var outlineBrush = new SolidBrush(outline))
            using (var fillBrush = new SolidBrush(fill))
            {
                if (outlineWidth > 0)
                {
                    for (var ox = -outlineWidth; ox <= outlineWidth; ox++)
                    {
                        for (var oy = -outlineWidth; oy <= outlineWidth; oy++)
                        {
                            if (ox == 0 && oy == 0)
                            {
                                continue;
                            }

                            g.DrawString(text, font, outlineBrush, location.X + ox, location.Y + oy, StringFormat.GenericTypographic);
                        }
                    }
                }

                g.DrawString(text, font, fillBrush, location.X, location.Y, StringFormat.GenericTypographic);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref Point pptDst,
            ref Size psize,
            IntPtr hdcSrc,
            ref Point pprSrc,
            int crKey,
            ref BlendFunction pblend,
            int dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
