using QuestPDF;
using QuestPDF.Infrastructure;

namespace Airlock.Infrastructure.Reports;

/// <summary>QuestPDF's process-wide settings, applied once (IR-21, U3-NFR-11).</summary>
internal static class QuestPdfSettings
{
    private static readonly Lock Gate = new();
    private static bool applied;

    // IR-21: the Community licence; only the bundled Lato font, so the report renders the same in the container and on
    // Windows; a character the font lacks is drawn as a placeholder rather than failing the report.
    public static void Ensure()
    {
        lock (Gate)
        {
            if (applied)
            {
                return;
            }

            Settings.License = LicenseType.Community;
            Settings.UseSystemFonts = false;
            Settings.ThrowOnMissingTextGlyphs = false;
            applied = true;
        }
    }
}
