using MudBlazor;

namespace Airlock.Web.Theme;

/// <summary>
/// UR-36, U5-NFR-07: the original identity as one dark MudBlazor theme — the palette with Q-U5-N1's text shades, Inter
/// and JetBrains Mono, and the original's layout (U5 NFR Design, theme mapping). Every text colour meets WCAG 2.2 AA on
/// the background it is used on; <c>ThemeContrastTests</c> measures them from here.
/// </summary>
internal static class AirlockTheme
{
    public const string Background = "#09090B";
    public const string Surface = "#18181B";
    public const string TopBar = "#131315";
    public const string Drawer = "#1C1B1D";
    public const string Primary = "#3B82F6";
    public const string PrimaryText = "#60A5FA";
    public const string OnPrimary = "#09090B";
    public const string Secondary = "#71717A";
    public const string SecondaryText = "#A1A1AA";
    public const string Error = "#EF4444";
    public const string ErrorText = "#F87171";
    public const string Warning = "#EAB308";
    public const string Clean = "#10B981";
    public const string Text = "#E2E8F0";
    public const string Heading = "#FFFFFF";

    private static readonly string[] Inter = ["Inter", "system-ui", "-apple-system", "Segoe UI", "Roboto", "sans-serif"];

    public static MudTheme Create() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = Primary,
            PrimaryContrastText = OnPrimary,
            Secondary = Secondary,
            SecondaryContrastText = Heading,
            Info = PrimaryText,
            InfoContrastText = OnPrimary,
            Success = Clean,
            SuccessContrastText = OnPrimary,
            Warning = Warning,
            WarningContrastText = OnPrimary,
            Error = Error,
            ErrorContrastText = OnPrimary,
            TextPrimary = Text,
            TextSecondary = SecondaryText,
            TextDisabled = Secondary,
            ActionDefault = SecondaryText,
            ActionDisabled = Secondary,
            Background = Background,
            Surface = Surface,
            AppbarBackground = TopBar,
            AppbarText = Text,
            DrawerBackground = Drawer,
            DrawerText = SecondaryText,
            DrawerIcon = SecondaryText,
            LinesInputs = Secondary,
            LinesDefault = "rgba(255,255,255,0.05)",
            Divider = "rgba(255,255,255,0.05)",
            TableLines = "rgba(255,255,255,0.05)",
            TableHover = "rgba(59,130,246,0.05)",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Inter },
            H1 = new H1Typography { FontFamily = Inter, FontWeight = "900", TextTransform = "uppercase" },
            H2 = new H2Typography { FontFamily = Inter, FontWeight = "700" },
            Button = new ButtonTypography { FontFamily = Inter, FontWeight = "700", LetterSpacing = "0.1em", TextTransform = "uppercase" },
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "56px",
            DrawerWidthLeft = "256px",
            DefaultBorderRadius = "8px",
        },
    };
}