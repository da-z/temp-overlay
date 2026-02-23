using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TempOverlay
{
    internal sealed class OverlayForm : Form
    {
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int WsExNoActivate = 0x08000000;
        private const int GwlExStyle = -20;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const int UlwAlpha = 0x2;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const int InnerPadding = 0;
        private const int HoverUnlockDelayMs = 2000;
        private const int HoverAwayLockDelayMs = 5000;
        private const int InteractionPollIntervalMs = 40;
        private const int ForegroundSettleIntervalMs = 16;
        private const int ForegroundSettleDurationMs = 900;
        private const int CloseButtonSize = 14;
        private const int CloseButtonOutsideOffset = 5;
        private const int FullscreenRectTolerancePx = 2;
        private const int FullscreenLooseTolerancePx = 16;
        private const double FullscreenCoverageThreshold = 0.97;
        private const int MonitorDefaultToNearest = 2;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const uint EventSystemForeground = 0x0003;
        private const uint WineventOutofcontext = 0x0000;
        private const uint WineventSkipOwnProcess = 0x0002;
        private const int TempValueFieldWidth = 5;
        private const int DragHitLayerAlpha = 1;
        private const string CpuWidthTemplate = "CPU: 888.8 C";
        private const string GpuWidthTemplate = "GPU: 888.8 C";

        private readonly OutlineLabel _cpuLabel;
        private readonly OutlineLabel _gpuLabel;
        private readonly OutlineLabel _statusLabel;
        private readonly Timer _timer;
        private readonly Timer _interactionTimer;
        private readonly Timer _foregroundSettleTimer;
        private readonly TemperatureReader _reader;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly string _fontFamily;
        private readonly string _valueFontFamily;
        private OverlaySettings _settings;
        private int _valueColumnWidth;
        private bool _interactionModeEnabled;
        private DateTime _hoverStartUtc;
        private bool _isHoverTiming;
        private DateTime _awayStartUtc;
        private bool _isAwayTiming;
        private Rectangle _textBounds;
        private bool _isDragging;
        private Point _dragCursorOffset;
        private bool _isFullscreenContextActive;
        private bool _isForegroundAppFullscreen;
        private bool _isDesktopForegroundApp;
        private string _fullscreenAppKey = string.Empty;
        private string _fullscreenAppInstanceId = string.Empty;
        private Rectangle _fullscreenMonitorBounds;
        private readonly HashSet<string> _hiddenFullscreenAppInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly CloseButtonForm _closeButtonForm;
        private static readonly HashSet<string> SuppressedForegroundProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SnippingTool",
            "ScreenClippingHost",
            "GameBar",
            "XboxGameBar",
            "NVIDIA Share",
            "Lightshot",
            "ShareX"
        };
        private IntPtr _foregroundEventHook = IntPtr.Zero;
        private readonly WinEventDelegate _foregroundEventHandler;
        private DateTime _foregroundSettleUntilUtc;
        private bool _isSuppressedForegroundApp;
        private bool _isSettingsPreviewActive;

        public OverlayForm()
        {
            _foregroundEventHandler = OnWinEvent;
            _reader = new TemperatureReader();
            _settings = OverlaySettings.Load();
            _valueFontFamily = EmbeddedFontLoader.GetPreferredFamilyName("Cascadia Mono");
            _fontFamily = _valueFontFamily;
            _closeButtonForm = new CloseButtonForm(_valueFontFamily, CloseButtonSize);
            _closeButtonForm.ButtonClicked += (_, __) => HideOverlayForCurrentFullscreenApp();
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
            _interactionTimer = new Timer { Interval = InteractionPollIntervalMs };
            _interactionTimer.Tick += (_, __) => PollInteractionState();
            _foregroundSettleTimer = new Timer { Interval = ForegroundSettleIntervalMs };
            _foregroundSettleTimer.Tick += (_, __) => PollForegroundSettle();

            Load += (_, __) =>
            {
                RefreshReadings();
                PositionOverlayForCurrentContext();
                EnsureTopMost();
                RenderLayeredOverlay();
                _timer.Start();
                _interactionTimer.Start();
                InstallForegroundHook();
            };

            FormClosing += (_, __) =>
            {
                _timer.Stop();
                _interactionTimer.Stop();
                _foregroundSettleTimer.Stop();
                UninstallForegroundHook();
                _closeButtonForm.Hide();
                _closeButtonForm.Dispose();
                _reader.Dispose();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayMenu.Dispose();
            };
            Resize += (_, __) =>
            {
                if (!_interactionModeEnabled)
                {
                    PositionOverlayForCurrentContext();
                }
                EnsureTopMost();
                RenderLayeredOverlay();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

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

        private void PositionOverlayForCurrentContext()
        {
            if (!_isSettingsPreviewActive &&
                _isFullscreenContextActive &&
                !string.IsNullOrEmpty(_fullscreenAppKey) &&
                _settings.FullscreenAppPositions.TryGetValue(_fullscreenAppKey, out var saved))
            {
                Left = saved.Left;
                Top = saved.Top;
                ClampOverlayToBounds(_fullscreenMonitorBounds);
                return;
            }

            PositionOverlay();
        }

        private void OpenSettings()
        {
            var originalSettings = CloneSettings(_settings);
            UninstallForegroundHook();
            try
            {
                using (var form = new SettingsForm(_settings))
                {
                    form.SettingsApplied += (_, __) => ApplySettingsFromForm(form);
                    form.PreviewChanged += (_, __) => ApplyPreviewFromForm(form);
                    _isSettingsPreviewActive = true;
                    UpdateContextVisibility();
                    EnsureTopMost();
                    RenderLayeredOverlay();

                    if (form.ShowDialog(this) != DialogResult.OK)
                    {
                        _settings = originalSettings;
                        ApplyVisualSettings();
                        UpdateOverlayBounds();
                        _isSettingsPreviewActive = false;
                        UpdateContextVisibility();
                        PositionOverlayForCurrentContext();
                        EnsureTopMost();
                        RenderLayeredOverlay();
                        return;
                    }

                    ApplySettingsFromForm(form);
                    _isSettingsPreviewActive = false;
                    UpdateContextVisibility();
                }
            }
            finally
            {
                InstallForegroundHook();
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
            UpdateFullscreenContext();
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

            var preserveTextAnchor = _interactionModeEnabled;
            var previousTextTopLeft = new Point(Left + _cpuLabel.Left, Top + _cpuLabel.Top);

            UpdateOverlayBounds();
            if (preserveTextAnchor)
            {
                Left = previousTextTopLeft.X - _cpuLabel.Left;
                Top = previousTextTopLeft.Y - _cpuLabel.Top;
            }
            else
            {
                PositionOverlayForCurrentContext();
            }
            EnsureTopMost();
            RenderLayeredOverlay();
        }

        private static string FormatTemp(string sensor, float? value)
        {
            return value.HasValue
                ? string.Format("{0}: {1," + TempValueFieldWidth + ":0.0} C", sensor, value.Value)
                : string.Format("{0}: {1," + TempValueFieldWidth + "} C", sensor, "--.-");
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

            _cpuLabel.Font = new Font(_valueFontFamily, valueFontSize, FontStyle.Regular);
            _gpuLabel.Font = new Font(_valueFontFamily, valueFontSize, FontStyle.Regular);
            _statusLabel.Font = new Font(_fontFamily, statusFontSize, FontStyle.Regular);

            var outlineColor = Color.FromArgb(150, 32, 32, 32);
            _cpuLabel.OutlineColor = outlineColor;
            _gpuLabel.OutlineColor = outlineColor;
            _statusLabel.OutlineColor = outlineColor;
            _cpuLabel.OutlineWidth = 1;
            _gpuLabel.OutlineWidth = 1;
            _statusLabel.OutlineWidth = 1;

            GetThemeColors(_settings.Theme, out var cpuColor, out var gpuColor, out var statusColor);
            _cpuLabel.ForeColor = cpuColor;
            _gpuLabel.ForeColor = gpuColor;
            _statusLabel.ForeColor = statusColor;

            _valueColumnWidth = Math.Max(
                MeasureOutlinedTextWidth(CpuWidthTemplate, _cpuLabel.Font, _cpuLabel.OutlineWidth),
                MeasureOutlinedTextWidth(GpuWidthTemplate, _gpuLabel.Font, _gpuLabel.OutlineWidth));
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
            // Slight negative spacing tightens the visual gap between CPU/GPU rows.
            _ = size;
            return -2;
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
                    gpuColor = Color.LimeGreen;
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
            PositionOverlayForCurrentContext();
            EnsureTopMost();
            RenderLayeredOverlay();
        }

        private void ApplyPreviewFromForm(SettingsForm form)
        {
            _settings.Position = form.SelectedPosition;
            _settings.VerticalPadding = form.SelectedVerticalPadding;
            _settings.HorizontalPadding = form.SelectedHorizontalPadding;
            _settings.RunAtStartup = form.RunAtStartup;
            _settings.Theme = form.SelectedTheme;
            _settings.FontSize = form.SelectedFontSize;

            ApplyVisualSettings();
            UpdateOverlayBounds();
            PositionOverlayForCurrentContext();
            EnsureTopMost();
            RenderLayeredOverlay();
        }

        private void UpdateOverlayBounds()
        {
            var rowSpacing = GetRowSpacing(_settings.FontSize);
            _cpuLabel.Location = new Point(InnerPadding, InnerPadding);
            _gpuLabel.Location = new Point(InnerPadding, _cpuLabel.Bottom + rowSpacing);

            var width = Math.Max(_valueColumnWidth, Math.Max(_cpuLabel.Width, _gpuLabel.Width));
            var bottom = _gpuLabel.Bottom;

            if (_statusLabel.Visible)
            {
                _statusLabel.Location = new Point(InnerPadding, _gpuLabel.Bottom + rowSpacing);
                width = Math.Max(width, _statusLabel.Width);
                bottom = _statusLabel.Bottom;
            }

            _textBounds = new Rectangle(InnerPadding, _cpuLabel.Top, Math.Max(1, width), Math.Max(1, bottom - _cpuLabel.Top));
            Width = Math.Max(1, width + (InnerPadding * 2));
            Height = Math.Max(1, bottom + InnerPadding);
        }

        private static int MeasureOutlinedTextWidth(string text, Font font, int outlineWidth)
        {
            var size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return Math.Max(1, size.Width + (outlineWidth * 2));
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
                if (_interactionModeEnabled)
                {
                    // Per-pixel hit-testing ignores fully transparent pixels; alpha=1 keeps it visually transparent.
                    using (var dragHitBrush = new SolidBrush(Color.FromArgb(DragHitLayerAlpha, 0, 0, 0)))
                    {
                        g.FillRectangle(dragHitBrush, 0, 0, Width, Height);
                    }
                }

                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawOutlinedText(g, _cpuLabel.Text, _cpuLabel.Location, _cpuLabel.Font, _cpuLabel.ForeColor, _cpuLabel.OutlineColor, _cpuLabel.OutlineWidth);
                DrawOutlinedText(g, _gpuLabel.Text, _gpuLabel.Location, _gpuLabel.Font, _gpuLabel.ForeColor, _gpuLabel.OutlineColor, _gpuLabel.OutlineWidth);
                if (_statusLabel.Visible && !string.IsNullOrEmpty(_statusLabel.Text))
                {
                    DrawOutlinedText(g, _statusLabel.Text, _statusLabel.Location, _statusLabel.Font, _statusLabel.ForeColor, _statusLabel.OutlineColor, _statusLabel.OutlineWidth);
                }

                ApplyBitmapToLayeredWindow(bitmap);
            }

            UpdateCloseButtonWindow();
        }

        private void UpdateCloseButtonWindow()
        {
            if (!_interactionModeEnabled || !_isFullscreenContextActive || !Visible || IsDisposed)
            {
                _closeButtonForm.HideButton();
                return;
            }

            var overlayLeft = Left;
            var overlayTop = Top;
            var overlayRight = Left + Width;
            var overlayBottom = Top + Height;

            var screenBounds = Screen.FromRectangle(new Rectangle(Left, Top, Width, Height)).Bounds;
            var textCenterX = overlayLeft + (Width / 2);
            var textCenterY = overlayTop + (Height / 2);
            var screenCenterX = screenBounds.Left + (screenBounds.Width / 2);
            var screenCenterY = screenBounds.Top + (screenBounds.Height / 2);

            var preferRight = textCenterX >= screenCenterX;
            var preferTop = textCenterY < screenCenterY;

            Rectangle BuildCandidate(bool top, bool right)
            {
                var x = right
                    ? overlayRight + CloseButtonOutsideOffset
                    : overlayLeft - CloseButtonSize - CloseButtonOutsideOffset;
                var y = top
                    ? overlayTop - CloseButtonSize - CloseButtonOutsideOffset
                    : overlayBottom + CloseButtonOutsideOffset;
                return new Rectangle(x, y, CloseButtonSize, CloseButtonSize);
            }

            var candidates = new[]
            {
                BuildCandidate(true, true),
                BuildCandidate(true, false),
                BuildCandidate(preferTop, preferRight),
                BuildCandidate(preferTop, !preferRight),
                BuildCandidate(!preferTop, preferRight),
                BuildCandidate(!preferTop, !preferRight)
            };

            var selected = candidates[0];
            foreach (var candidate in candidates)
            {
                if (screenBounds.Contains(candidate))
                {
                    selected = candidate;
                    break;
                }
            }

            if (!screenBounds.Contains(selected))
            {
                var clampedX = Math.Min(Math.Max(selected.Left, screenBounds.Left), screenBounds.Right - CloseButtonSize);
                var clampedY = Math.Min(Math.Max(selected.Top, screenBounds.Top), screenBounds.Bottom - CloseButtonSize);
                selected = new Rectangle(clampedX, clampedY, CloseButtonSize, CloseButtonSize);
            }

            _closeButtonForm.ShowAt(selected.Left, selected.Top);
        }

        private void PollInteractionState()
        {
            if (_isSettingsPreviewActive)
            {
                return;
            }

            UpdateFullscreenContext();
            UpdateContextVisibility();

            if (!Visible)
            {
                return;
            }

            if (!_isFullscreenContextActive)
            {
                _isHoverTiming = false;
                _isAwayTiming = false;
                if (_interactionModeEnabled && !_isDragging)
                {
                    SetInteractionMode(false);
                }

                return;
            }

            if (_isDragging)
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    _isDragging = false;
                    Capture = false;
                    SaveCurrentFullscreenAppPosition();
                    return;
                }

                return;
            }

            var isHoveringOverlay = new Rectangle(Left, Top, Width, Height).Contains(Cursor.Position);
            if (isHoveringOverlay)
            {
                _isAwayTiming = false;

                if (!_isHoverTiming)
                {
                    _isHoverTiming = true;
                    _hoverStartUtc = DateTime.UtcNow;
                    return;
                }

                if (!_interactionModeEnabled && (DateTime.UtcNow - _hoverStartUtc).TotalMilliseconds >= HoverUnlockDelayMs)
                {
                    SetInteractionMode(true);
                }

                return;
            }

            _isHoverTiming = false;
            if (_closeButtonForm.IsPointerOver)
            {
                _isAwayTiming = false;
                return;
            }

            if (!_interactionModeEnabled)
            {
                return;
            }

            if (!_isAwayTiming)
            {
                _isAwayTiming = true;
                _awayStartUtc = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - _awayStartUtc).TotalMilliseconds >= HoverAwayLockDelayMs)
            {
                _isAwayTiming = false;
                SetInteractionMode(false);
            }
        }

        private void UpdateFullscreenContext()
        {
            var wasFullscreen = _isFullscreenContextActive;
            var previousAppInstanceId = _fullscreenAppInstanceId;

            _isFullscreenContextActive = TryGetForegroundFullscreenApp(
                out _fullscreenAppKey,
                out _fullscreenAppInstanceId,
                out _fullscreenMonitorBounds,
                out _isForegroundAppFullscreen,
                out _isDesktopForegroundApp);
            _isSuppressedForegroundApp = false;
            if (!_isFullscreenContextActive)
            {
                _fullscreenAppKey = string.Empty;
                _fullscreenAppInstanceId = string.Empty;
                _isForegroundAppFullscreen = false;
                _isDesktopForegroundApp = false;
            }
            else if (IsSuppressedForegroundProcess(_fullscreenAppKey))
            {
                _isSuppressedForegroundApp = true;
                _isFullscreenContextActive = false;
                _fullscreenAppKey = string.Empty;
                _fullscreenAppInstanceId = string.Empty;
                _isForegroundAppFullscreen = false;
                _isDesktopForegroundApp = false;
            }

            var fullscreenAppChanged = _isFullscreenContextActive &&
                                       !string.Equals(previousAppInstanceId, _fullscreenAppInstanceId, StringComparison.OrdinalIgnoreCase);
            if ((!_isFullscreenContextActive && wasFullscreen) || fullscreenAppChanged)
            {
                _isHoverTiming = false;
                _isAwayTiming = false;
                if (!_isDragging)
                {
                    SetInteractionMode(false);
                    PositionOverlayForCurrentContext();
                    RenderLayeredOverlay();
                }
            }
        }

        private void UpdateContextVisibility()
        {
            var shouldHide = !_isSettingsPreviewActive &&
                             (_isSuppressedForegroundApp ||
                             (!_isForegroundAppFullscreen && !_isDesktopForegroundApp) ||
                             (_isFullscreenContextActive &&
                              !string.IsNullOrWhiteSpace(_fullscreenAppInstanceId) &&
                              _hiddenFullscreenAppInstances.Contains(_fullscreenAppInstanceId)));

            if (shouldHide)
            {
                if (Visible)
                {
                    _isHoverTiming = false;
                    _isAwayTiming = false;
                    SetInteractionMode(false);
                    Hide();
                    _closeButtonForm.HideButton();
                }

                return;
            }

            if (Visible)
            {
                return;
            }

            Show();
            PositionOverlayForCurrentContext();
            RenderLayeredOverlay();
        }

        private bool TryGetForegroundFullscreenApp(out string appKey, out string appInstanceId, out Rectangle monitorBounds, out bool isFullscreen, out bool isDesktopForeground)
        {
            appKey = string.Empty;
            appInstanceId = string.Empty;
            monitorBounds = Rectangle.Empty;
            isFullscreen = false;
            isDesktopForeground = false;

            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle || !IsWindowVisible(foreground))
            {
                return false;
            }

            if (!GetWindowRect(foreground, out var windowRect))
            {
                return false;
            }

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            var m = monitorInfo.rcMonitor;
            monitorBounds = Rectangle.FromLTRB(m.Left, m.Top, m.Right, m.Bottom);
            isFullscreen = IsWindowFullscreen(windowRect, m);
            isDesktopForeground = IsDesktopWindow(foreground);

            if (!TryGetWindowAppIdentity(foreground, out appKey, out appInstanceId))
            {
                return false;
            }

            return true;
        }

        private static bool IsDesktopWindow(IntPtr hwnd)
        {
            var className = GetWindowClassName(hwnd);
            return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetWindowClassName(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            var length = GetClassName(hwnd, sb, sb.Capacity);
            return length > 0 ? sb.ToString() : string.Empty;
        }

        private static bool IsWindowFullscreen(Rect windowRect, Rect monitorRect)
        {
            var monitorBounds = Rectangle.FromLTRB(monitorRect.Left, monitorRect.Top, monitorRect.Right, monitorRect.Bottom);
            var windowBounds = Rectangle.FromLTRB(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom);
            var intersection = Rectangle.Intersect(windowBounds, monitorBounds);
            var monitorArea = Math.Max(1L, (long)monitorBounds.Width * monitorBounds.Height);
            var coveredArea = Math.Max(0L, (long)intersection.Width * intersection.Height);
            var coveredRatio = (double)coveredArea / monitorArea;

            var nearEdges = windowRect.Left <= monitorRect.Left + FullscreenRectTolerancePx &&
                            windowRect.Top <= monitorRect.Top + FullscreenRectTolerancePx &&
                            windowRect.Right >= monitorRect.Right - FullscreenRectTolerancePx &&
                            windowRect.Bottom >= monitorRect.Bottom - FullscreenRectTolerancePx;
            var nearEdgesLoose = windowRect.Left <= monitorRect.Left + FullscreenLooseTolerancePx &&
                                 windowRect.Top <= monitorRect.Top + FullscreenLooseTolerancePx &&
                                 windowRect.Right >= monitorRect.Right - FullscreenLooseTolerancePx &&
                                 windowRect.Bottom >= monitorRect.Bottom - FullscreenLooseTolerancePx;
            return nearEdges || (nearEdgesLoose && coveredRatio >= FullscreenCoverageThreshold);
        }

        private static bool TryGetWindowAppIdentity(IntPtr hwnd, out string appKey, out string appInstanceId)
        {
            appKey = string.Empty;
            appInstanceId = string.Empty;
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            appKey = path;
                        }
                    }
                    catch
                    {
                        // Fall through to process name fallback.
                    }

                    if (!string.IsNullOrWhiteSpace(process.ProcessName))
                    {
                        appKey = process.ProcessName;
                    }

                    if (string.IsNullOrWhiteSpace(appKey))
                    {
                        return false;
                    }

                    long startTicks = 0;
                    try
                    {
                        startTicks = process.StartTime.ToUniversalTime().Ticks;
                    }
                    catch
                    {
                        // Keep 0 when start time is unavailable due permission.
                    }

                    appInstanceId = string.Format("{0}|pid:{1}|start:{2}", appKey, processId, startTicks);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSuppressedForegroundProcess(string appKey)
        {
            if (string.IsNullOrWhiteSpace(appKey))
            {
                return false;
            }

            var processName = appKey;
            try
            {
                processName = System.IO.Path.GetFileNameWithoutExtension(appKey);
            }
            catch
            {
                // Keep original app key if it's not a valid path.
            }

            return SuppressedForegroundProcesses.Contains(processName);
        }

        private static OverlaySettings CloneSettings(OverlaySettings source)
        {
            var clone = new OverlaySettings
            {
                Position = source.Position,
                VerticalPadding = source.VerticalPadding,
                HorizontalPadding = source.HorizontalPadding,
                RunAtStartup = source.RunAtStartup,
                Theme = source.Theme,
                FontSize = source.FontSize
            };

            foreach (var kvp in source.FullscreenAppPositions)
            {
                clone.FullscreenAppPositions[kvp.Key] = new OverlaySettings.SavedOverlayPosition
                {
                    Left = kvp.Value.Left,
                    Top = kvp.Value.Top
                };
            }

            return clone;
        }

        private void SetInteractionMode(bool enabled)
        {
            if (_interactionModeEnabled == enabled)
            {
                return;
            }

            var previousTextTopLeft = new Point(Left + _cpuLabel.Left, Top + _cpuLabel.Top);
            _interactionModeEnabled = enabled;
            _isAwayTiming = false;
            SetOverlayInputEnabled(enabled);
            Cursor = enabled ? Cursors.SizeAll : Cursors.Default;
            UpdateOverlayBounds();
            Left = previousTextTopLeft.X - _cpuLabel.Left;
            Top = previousTextTopLeft.Y - _cpuLabel.Top;
            RenderLayeredOverlay();
            if (!enabled)
            {
                _closeButtonForm.HideButton();
            }
        }

        private void SetOverlayInputEnabled(bool enabled)
        {
            var exStyle = GetWindowExStyle(Handle);
            var desired = enabled ? (exStyle & ~WsExTransparent) : (exStyle | WsExTransparent);
            if (desired == exStyle)
            {
                return;
            }

            SetWindowExStyle(Handle, desired);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        private void ClampOverlayToBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = Screen.PrimaryScreen.Bounds;
            }

            var maxLeft = bounds.Right - Width;
            var maxTop = bounds.Bottom - Height;
            Left = Math.Min(Math.Max(Left, bounds.Left), maxLeft);
            Top = Math.Min(Math.Max(Top, bounds.Top), maxTop);
        }

        private void SaveCurrentFullscreenAppPosition()
        {
            if (!_isFullscreenContextActive || string.IsNullOrWhiteSpace(_fullscreenAppKey))
            {
                return;
            }

            _settings.FullscreenAppPositions[_fullscreenAppKey] = new OverlaySettings.SavedOverlayPosition
            {
                Left = Left,
                Top = Top
            };
            _settings.Save();
        }

        private void HideOverlayForCurrentFullscreenApp()
        {
            if (!_isFullscreenContextActive || string.IsNullOrWhiteSpace(_fullscreenAppInstanceId))
            {
                return;
            }

            _hiddenFullscreenAppInstances.Add(_fullscreenAppInstanceId);
            _isHoverTiming = false;
            _isAwayTiming = false;
            SetInteractionMode(false);
            Hide();
            _closeButtonForm.HideButton();
        }

        private void InstallForegroundHook()
        {
            if (_foregroundEventHook != IntPtr.Zero)
            {
                return;
            }

            _foregroundEventHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _foregroundEventHandler,
                0,
                0,
                WineventOutofcontext | WineventSkipOwnProcess);
        }

        private void UninstallForegroundHook()
        {
            if (_foregroundEventHook == IntPtr.Zero)
            {
                return;
            }

            _ = UnhookWinEvent(_foregroundEventHook);
            _foregroundEventHook = IntPtr.Zero;
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
        {
            if (eventType != EventSystemForeground || IsDisposed || _isSettingsPreviewActive)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)HideForForegroundTransition);
                BeginInvoke((Action)HandleForegroundChanged);
                BeginInvoke((Action)StartForegroundSettleProbe);
            }
            catch
            {
                // Form is closing/disposed.
            }
        }

        private void HideForForegroundTransition()
        {
            if (IsDisposed || _isDragging)
            {
                return;
            }

            _isHoverTiming = false;
            _isAwayTiming = false;
            if (_interactionModeEnabled)
            {
                SetInteractionMode(false);
            }

            if (Visible)
            {
                Hide();
            }

            _closeButtonForm.HideButton();
        }

        private void StartForegroundSettleProbe()
        {
            if (IsDisposed || _isSettingsPreviewActive)
            {
                return;
            }

            _foregroundSettleUntilUtc = DateTime.UtcNow.AddMilliseconds(ForegroundSettleDurationMs);
            _foregroundSettleTimer.Start();
        }

        private void PollForegroundSettle()
        {
            if (IsDisposed || _isSettingsPreviewActive)
            {
                _foregroundSettleTimer.Stop();
                return;
            }

            if (DateTime.UtcNow > _foregroundSettleUntilUtc)
            {
                _foregroundSettleTimer.Stop();
                return;
            }

            HandleForegroundChanged();
        }

        private void HandleForegroundChanged()
        {
            if (IsDisposed || _isSettingsPreviewActive)
            {
                return;
            }

            UpdateFullscreenContext();
            UpdateContextVisibility();
            if (!Visible || _isDragging || _interactionModeEnabled)
            {
                return;
            }

            PositionOverlayForCurrentContext();
            EnsureTopMost();
            RenderLayeredOverlay();
        }

        private void EnsureTopMost()
        {
            if (!IsHandleCreated || _isSettingsPreviewActive)
            {
                return;
            }

            _ = SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
            _closeButtonForm.EnsureTopMost();
        }

        protected override void WndProc(ref Message m)
        {
            if (_interactionModeEnabled && _isFullscreenContextActive)
            {
                switch (m.Msg)
                {
                    case WmLButtonDown:
                    {
                        _isDragging = true;
                        _dragCursorOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
                        Capture = true;
                        return;
                    }
                    case WmMouseMove:
                    {
                        if (!_isDragging)
                        {
                            break;
                        }

                        var cursor = Cursor.Position;
                        Left = cursor.X - _dragCursorOffset.X;
                        Top = cursor.Y - _dragCursorOffset.Y;
                        ClampOverlayToBounds(_fullscreenMonitorBounds);
                        UpdateCloseButtonWindow();
                        Cursor = Cursors.SizeAll;
                        RenderLayeredOverlay();
                        return;
                    }
                    case WmLButtonUp:
                    {
                        if (!_isDragging)
                        {
                            break;
                        }

                        _isDragging = false;
                        Capture = false;
                        Cursor = Cursors.SizeAll;
                        SaveCurrentFullscreenAppPosition();
                        UpdateCloseButtonWindow();
                        return;
                    }
                }
            }

            base.WndProc(ref m);
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
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
        }

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime);

        private static int GetWindowExStyle(IntPtr hwnd)
        {
            if (IntPtr.Size == 8)
            {
                return (int)GetWindowLongPtr64(hwnd, GwlExStyle).ToInt64();
            }

            return GetWindowLong32(hwnd, GwlExStyle);
        }

        private static void SetWindowExStyle(IntPtr hwnd, int value)
        {
            if (IntPtr.Size == 8)
            {
                _ = SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(value));
                return;
            }

            _ = SetWindowLong32(hwnd, GwlExStyle, value);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
