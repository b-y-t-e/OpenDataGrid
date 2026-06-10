namespace Open.DataGrid.Models
{
    /// <summary>
    /// Controls the visual theme of the <see cref="Controls.OpenDataGrid"/>.
    /// </summary>
    public enum GridTheme
    {
        /// <summary>Light theme — default, identical to the look before this feature was added (white background, gray chrome).</summary>
        Light,

        /// <summary>Dark theme — professional dark palette inspired by VS Code (dark surfaces, light text, blue selection).</summary>
        Dark,

        /// <summary>
        /// Follows the Windows application color mode (AppsUseLightTheme registry key).
        /// Reacts to changes at runtime without requiring an app restart.
        /// Falls back to <see cref="Light"/> when the registry value is unavailable.
        /// </summary>
        System,
    }
}
