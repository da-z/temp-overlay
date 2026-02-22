using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempOverlay
{
    internal enum OverlayPositionPreset
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft
    }

    internal enum OverlayTheme
    {
        NeonMint,
        Ember,
        Ice,
        Bw
    }

    internal enum OverlayFontSize
    {
        VerySmall = 0,
        Small = 1,
        Medium = 2,
        Large = 3
    }

    internal sealed class OverlaySettings
    {
        public OverlayPositionPreset Position { get; set; } = OverlayPositionPreset.TopRight;
        public int VerticalPadding { get; set; } = 20;
        public int HorizontalPadding { get; set; } = 20;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Padding { get; set; }
        public bool RunAtStartup { get; set; } = false;
        public OverlayTheme Theme { get; set; } = OverlayTheme.NeonMint;
        public OverlayFontSize FontSize { get; set; } = OverlayFontSize.Medium;

        private static string SettingsPath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "TempOverlay", "settings.json");
            }
        }

        public static OverlaySettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new OverlaySettings();
                }

                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<OverlaySettings>(json);
                if (settings == null)
                {
                    return new OverlaySettings();
                }

                if (settings.Padding.HasValue)
                {
                    var legacyPadding = Math.Max(0, settings.Padding.Value);
                    settings.VerticalPadding = legacyPadding;
                    settings.HorizontalPadding = legacyPadding;
                }

                settings.VerticalPadding = Math.Max(0, settings.VerticalPadding);
                settings.HorizontalPadding = Math.Max(0, settings.HorizontalPadding);
                settings.FontSize = NormalizeFontSize(settings.FontSize);
                settings.Padding = null;
                return settings;
            }
            catch
            {
                return new OverlaySettings();
            }
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Padding = null;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }

        private static OverlayFontSize NormalizeFontSize(OverlayFontSize size)
        {
            // Legacy migration: old "Micro" value (4) maps to new smallest value.
            if ((int)size == 4)
            {
                return OverlayFontSize.VerySmall;
            }

            return size switch
            {
                OverlayFontSize.VerySmall => OverlayFontSize.VerySmall,
                OverlayFontSize.Small => OverlayFontSize.Small,
                OverlayFontSize.Medium => OverlayFontSize.Medium,
                OverlayFontSize.Large => OverlayFontSize.Large,
                _ => OverlayFontSize.Medium
            };
        }
    }
}
