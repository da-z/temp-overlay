using System;
using System.Drawing;
using System.Windows.Forms;

namespace TempOverlay
{
    internal sealed class SettingsForm : Form
    {
        private readonly ComboBox _positionCombo;
        private readonly ComboBox _themeCombo;
        private readonly ComboBox _fontSizeCombo;
        private readonly NumericUpDown _verticalPaddingInput;
        private readonly NumericUpDown _horizontalPaddingInput;
        private readonly CheckBox _startupCheck;
        private readonly Button _okButton;
        private readonly Button _applyButton;
        private readonly Button _cancelButton;

        public OverlayPositionPreset SelectedPosition { get; private set; }
        public int SelectedVerticalPadding { get; private set; }
        public int SelectedHorizontalPadding { get; private set; }
        public bool RunAtStartup { get; private set; }
        public OverlayTheme SelectedTheme { get; private set; }
        public OverlayFontSize SelectedFontSize { get; private set; }
        public event EventHandler SettingsApplied;
        public event EventHandler PreviewChanged;

        public SettingsForm(OverlaySettings current)
        {
            Text = "Settings";
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 300);

            const int labelLeft = 16;
            const int labelWidth = 120;
            const int inputLeft = 150;
            const int rowGap = 38;
            const int firstRowTop = 18;

            var positionLabel = new Label
            {
                Text = "Position",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(labelWidth, 24),
                Location = new Point(labelLeft, firstRowTop)
            };

            _positionCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(inputLeft, firstRowTop - 2),
                Width = 180
            };
            _positionCombo.Items.AddRange(new object[]
            {
                "Top Right",
                "Top Left",
                "Bottom Right",
                "Bottom Left"
            });

            var verticalPaddingLabel = new Label
            {
                Text = "Vertical (px)",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(labelWidth, 24),
                Location = new Point(labelLeft, firstRowTop + rowGap)
            };

            _verticalPaddingInput = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 500,
                Location = new Point(inputLeft, firstRowTop + rowGap - 2),
                Width = 100
            };

            var horizontalPaddingLabel = new Label
            {
                Text = "Horizontal (px)",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(labelWidth, 24),
                Location = new Point(labelLeft, firstRowTop + (rowGap * 2))
            };

            _horizontalPaddingInput = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 500,
                Location = new Point(inputLeft, firstRowTop + (rowGap * 2) - 2),
                Width = 100
            };

            var themeLabel = new Label
            {
                Text = "Theme",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(labelWidth, 24),
                Location = new Point(labelLeft, firstRowTop + (rowGap * 3))
            };

            _themeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(inputLeft, firstRowTop + (rowGap * 3) - 2),
                Width = 180
            };
            _themeCombo.Items.AddRange(new object[]
            {
                "Neon Mint",
                "Ember",
                "Ice",
                "BW"
            });

            var fontSizeLabel = new Label
            {
                Text = "Font size",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(labelWidth, 24),
                Location = new Point(labelLeft, firstRowTop + (rowGap * 4))
            };

            _fontSizeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(inputLeft, firstRowTop + (rowGap * 4) - 2),
                Width = 120
            };
            _fontSizeCombo.Items.AddRange(new object[]
            {
                "Very Small",
                "Small",
                "Medium",
                "Large"
            });

            _startupCheck = new CheckBox
            {
                Text = "Run at startup",
                AutoSize = true,
                Location = new Point(inputLeft, firstRowTop + (rowGap * 5))
            };

            _okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(84, 252),
                Width = 88,
                Height = 32
            };

            _applyButton = new Button
            {
                Text = "Apply",
                Location = new Point(180, 252),
                Width = 88,
                Height = 32
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(276, 252),
                Width = 88,
                Height = 32
            };

            Controls.Add(positionLabel);
            Controls.Add(_positionCombo);
            Controls.Add(verticalPaddingLabel);
            Controls.Add(_verticalPaddingInput);
            Controls.Add(horizontalPaddingLabel);
            Controls.Add(_horizontalPaddingInput);
            Controls.Add(themeLabel);
            Controls.Add(_themeCombo);
            Controls.Add(fontSizeLabel);
            Controls.Add(_fontSizeCombo);
            Controls.Add(_startupCheck);
            Controls.Add(_okButton);
            Controls.Add(_applyButton);
            Controls.Add(_cancelButton);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            ApplyCurrentValues(current);

            _okButton.Click += (_, __) =>
            {
                CaptureSelections();
            };

            _applyButton.Click += (_, __) =>
            {
                CaptureSelections();
                SettingsApplied?.Invoke(this, EventArgs.Empty);
            };

            _positionCombo.SelectedIndexChanged += (_, __) => NotifyPreviewChanged();
            _verticalPaddingInput.ValueChanged += (_, __) => NotifyPreviewChanged();
            _horizontalPaddingInput.ValueChanged += (_, __) => NotifyPreviewChanged();
            _themeCombo.SelectedIndexChanged += (_, __) => NotifyPreviewChanged();
            _fontSizeCombo.SelectedIndexChanged += (_, __) => NotifyPreviewChanged();
            _startupCheck.CheckedChanged += (_, __) => NotifyPreviewChanged();
        }

        private void ApplyCurrentValues(OverlaySettings current)
        {
            _positionCombo.SelectedIndex = PresetToIndex(current.Position);
            _verticalPaddingInput.Value = Math.Max(0, Math.Min(500, current.VerticalPadding));
            _horizontalPaddingInput.Value = Math.Max(0, Math.Min(500, current.HorizontalPadding));
            _themeCombo.SelectedIndex = ThemeToIndex(current.Theme);
            _fontSizeCombo.SelectedIndex = FontSizeToIndex(current.FontSize);
            _startupCheck.Checked = current.RunAtStartup;
            SelectedPosition = current.Position;
            SelectedVerticalPadding = current.VerticalPadding;
            SelectedHorizontalPadding = current.HorizontalPadding;
            RunAtStartup = current.RunAtStartup;
            SelectedTheme = current.Theme;
            SelectedFontSize = current.FontSize;
        }

        private static int PresetToIndex(OverlayPositionPreset preset)
        {
            return preset switch
            {
                OverlayPositionPreset.TopRight => 0,
                OverlayPositionPreset.TopLeft => 1,
                OverlayPositionPreset.BottomRight => 2,
                OverlayPositionPreset.BottomLeft => 3,
                _ => 0
            };
        }

        private static OverlayPositionPreset IndexToPreset(int index)
        {
            return index switch
            {
                1 => OverlayPositionPreset.TopLeft,
                2 => OverlayPositionPreset.BottomRight,
                3 => OverlayPositionPreset.BottomLeft,
                _ => OverlayPositionPreset.TopRight
            };
        }

        private static int ThemeToIndex(OverlayTheme theme)
        {
            return theme switch
            {
                OverlayTheme.Ember => 1,
                OverlayTheme.Ice => 2,
                OverlayTheme.Bw => 3,
                _ => 0
            };
        }

        private static OverlayTheme IndexToTheme(int index)
        {
            return index switch
            {
                1 => OverlayTheme.Ember,
                2 => OverlayTheme.Ice,
                3 => OverlayTheme.Bw,
                _ => OverlayTheme.NeonMint
            };
        }

        private static int FontSizeToIndex(OverlayFontSize size)
        {
            return size switch
            {
                OverlayFontSize.Small => 1,
                OverlayFontSize.Medium => 2,
                OverlayFontSize.Large => 3,
                _ => 0
            };
        }

        private static OverlayFontSize IndexToFontSize(int index)
        {
            return index switch
            {
                1 => OverlayFontSize.Small,
                2 => OverlayFontSize.Medium,
                3 => OverlayFontSize.Large,
                _ => OverlayFontSize.VerySmall
            };
        }

        private void CaptureSelections()
        {
            SelectedPosition = IndexToPreset(_positionCombo.SelectedIndex);
            SelectedVerticalPadding = (int)_verticalPaddingInput.Value;
            SelectedHorizontalPadding = (int)_horizontalPaddingInput.Value;
            RunAtStartup = _startupCheck.Checked;
            SelectedTheme = IndexToTheme(_themeCombo.SelectedIndex);
            SelectedFontSize = IndexToFontSize(_fontSizeCombo.SelectedIndex);
        }

        private void NotifyPreviewChanged()
        {
            CaptureSelections();
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
