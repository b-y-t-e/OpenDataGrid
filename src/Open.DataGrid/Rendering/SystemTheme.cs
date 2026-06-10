using Microsoft.Win32;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// Utility for detecting the Windows application color mode (Light / Dark).
    /// Reads <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>.
    /// Missing or unreadable value → treats as Light.
    /// </summary>
    public static class SystemTheme
    {
        private const string RegistryPath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValue = "AppsUseLightTheme";

        /// <summary>Returns <c>true</c> when Windows is set to the Dark application theme.</summary>
        public static bool IsDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return false;
                    var value = key.GetValue(RegistryValue);
                    // 0 = dark, 1 = light, absent = light
                    return value is int intVal && intVal == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
