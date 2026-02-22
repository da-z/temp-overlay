using System;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TempOverlay
{
    internal static class EmbeddedFontLoader
    {
        private static readonly PrivateFontCollection PrivateFonts = new PrivateFontCollection();
        private static bool _loaded;

        public static string GetPreferredFamilyName(string preferredFamily)
        {
            EnsureLoaded();

            if (!string.IsNullOrWhiteSpace(preferredFamily))
            {
                if (PrivateFonts.Families.Any(f => string.Equals(f.Name, preferredFamily, StringComparison.OrdinalIgnoreCase)))
                {
                    return preferredFamily;
                }

                using (var installed = new InstalledFontCollection())
                {
                    if (installed.Families.Any(f => string.Equals(f.Name, preferredFamily, StringComparison.OrdinalIgnoreCase)))
                    {
                        return preferredFamily;
                    }
                }
            }

            if (PrivateFonts.Families.Length > 0)
            {
                return PrivateFonts.Families[0].Name;
            }

            return "Segoe UI";
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames()
                .Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));

            foreach (var resourceName in names)
            {
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        continue;
                    }

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var bytes = ms.ToArray();
                        var mem = Marshal.AllocCoTaskMem(bytes.Length);
                        try
                        {
                            Marshal.Copy(bytes, 0, mem, bytes.Length);
                            PrivateFonts.AddMemoryFont(mem, bytes.Length);
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(mem);
                        }
                    }
                }
            }
        }
    }
}
