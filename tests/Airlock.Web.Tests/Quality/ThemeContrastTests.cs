using System.Globalization;
using System.Text.RegularExpressions;
using Airlock.Web.Tests.Ui;
using Airlock.Web.Theme;
using MudBlazor;
using MudBlazor.Utilities;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-07, Q-U5-N1: every text and background pair the pages use meets WCAG 2.2 AA (4.5:1), and every part of a
// control 3:1, measured from the theme and the stylesheet themselves.
public sealed partial class ThemeContrastTests
{
    private static readonly PaletteDark Palette = (PaletteDark)AirlockTheme.Create().PaletteDark;
    private static readonly Lazy<IReadOnlyDictionary<string, Rgb>> Stylesheet = new(ReadStylesheet);

    private static Rgb Surface => Of(Palette.Surface);

    // The highlighted drawer item, a selected row, the CRITICAL badge and a selected findings count (NFR requirements).
    private static Rgb DrawerItem => Rgb.Parse("#2A2A2C");

    private static Rgb SelectedRow => Of(Palette.Primary).Over(Surface, 0.10);

    private static Rgb CriticalBadge => Of(Palette.Error).Over(Surface, 0.10);

    private static Rgb SelectedCount => Of(Palette.Primary).Over(Surface, 0.20);

    public static TheoryData<string, string> TextPairs => new()
    {
        { "TextPrimary", "Background" }, { "TextPrimary", "Surface" }, { "TextPrimary", "AppbarBackground" }, { "TextPrimary", "DrawerBackground" },
        { "TextSecondary", "Background" }, { "TextSecondary", "Surface" }, { "TextSecondary", "DrawerBackground" }, { "TextSecondary", "drawer item" },
        { "TextSecondary", "selected row" }, { "TextSecondary", "CRITICAL badge" },
        { "Info", "Background" }, { "Info", "Surface" }, { "Info", "drawer item" }, { "Info", "selected row" }, { "Info", "selected count" },
        { "--airlock-error-text", "Background" }, { "--airlock-error-text", "Surface" }, { "--airlock-error-text", "CRITICAL badge" },
        { "--airlock-high", "Surface" }, { "--airlock-high", "high badge" },
        { "Warning", "Background" }, { "Warning", "Surface" }, { "Warning", "warning tint" },
        { "Success", "Background" }, { "Success", "Surface" }, { "Success", "clean tint" },
        { "--airlock-terminal-text", "terminal" }, { "--airlock-label-text", "terminal" }, { "--airlock-label-text", "Surface" },
        { "AppbarText", "AppbarBackground" }, { "DrawerText", "DrawerBackground" },
        { "PrimaryContrastText", "Primary" }, { "InfoContrastText", "Info" }, { "SuccessContrastText", "Success" },
        { "WarningContrastText", "Warning" }, { "ErrorContrastText", "Error" }, { "SecondaryContrastText", "Secondary" },
    };

    public static TheoryData<string, string> ControlPairs => new()
    {
        { "LinesInputs", "Background" }, { "LinesInputs", "Surface" },
        { "focus ring", "Background" }, { "focus ring", "Surface" }, { "focus ring", "DrawerBackground" }, { "focus ring", "AppbarBackground" },
        { "focus ring", "selected row" }, { "focus ring", "terminal" },
    };

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void Text_meets_four_and_a_half_to_one(string text, string background) =>
        AssertRatio(text, background, 4.5);

    [Theory]
    [MemberData(nameof(ControlPairs))]
    public void Parts_of_controls_meet_three_to_one(string part, string background) =>
        AssertRatio(part, background, 3.0);

    [Fact]
    public void The_original_grey_is_kept_away_from_small_text()
    {
        // Q-U5-N1: #71717A falls short on surfaces, so it is no longer the secondary text colour.
        Assert.True(Rgb.Parse("#71717A").Ratio(Surface) < 4.5);
        Assert.Equal(Rgb.Parse("#A1A1AA"), Of(Palette.TextSecondary));
    }

    [Fact]
    public void The_stylesheet_carries_the_themes_tokens()
    {
        Assert.Equal(Of(Palette.Background), Stylesheet.Value["--airlock-background"]);
        Assert.Equal(Of(Palette.Surface), Stylesheet.Value["--airlock-surface"]);
        Assert.Equal(Of(Palette.Primary), Stylesheet.Value["--airlock-primary"]);
        Assert.Equal(Of(Palette.Info), Stylesheet.Value["--airlock-primary-text"]);
        Assert.Equal(Of(Palette.TextSecondary), Stylesheet.Value["--airlock-secondary-text"]);
        Assert.Equal(Of(Palette.Error), Stylesheet.Value["--airlock-error"]);
        Assert.Equal(Rgb.Parse(AirlockTheme.ErrorText), Stylesheet.Value["--airlock-error-text"]);
    }

    private static void AssertRatio(string foreground, string background, double minimum)
    {
        var ratio = Colour(foreground).Ratio(Colour(background));
        Assert.True(ratio >= minimum, $"{foreground} on {background}: {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1, below {minimum}:1.");
    }

    private static Rgb Colour(string name) => name switch
    {
        "drawer item" => DrawerItem,
        "selected row" => SelectedRow,
        "CRITICAL badge" => CriticalBadge,
        "selected count" => SelectedCount,
        "high badge" => Stylesheet.Value["--airlock-high"].Over(Surface, 0.10),
        "warning tint" => Of(Palette.Warning).Over(Surface, 0.10),
        "clean tint" => Of(Palette.Success).Over(Surface, 0.10),
        "terminal" => Rgb.Parse("#000000"),
        "focus ring" => Stylesheet.Value["--airlock-primary-text"],
        _ when name.StartsWith("--", StringComparison.Ordinal) => Stylesheet.Value[name],
        _ => Of((MudColor)Palette.GetType().GetProperty(name)!.GetValue(Palette)!),
    };

    private static Rgb Of(MudColor colour) => new(colour.R, colour.G, colour.B);

    private static Dictionary<string, Rgb> ReadStylesheet()
    {
        var css = File.ReadAllText(Path.Combine(Repository.Root, "src", "Airlock.Web", "wwwroot", "css", "airlock.css"));
        return CustomProperty().Matches(css).ToDictionary(match => match.Groups["name"].Value, match => Rgb.Parse(match.Groups["value"].Value));
    }

    [GeneratedRegex(@"(?<name>--airlock-[a-z-]+):\s*(?<value>#[0-9A-Fa-f]{6});")]
    private static partial Regex CustomProperty();

    private readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb Parse(string hex) =>
            new(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

        /// <summary>This colour at <paramref name="alpha"/> over <paramref name="background"/>.</summary>
        public Rgb Over(Rgb background, double alpha)
        {
            byte Mix(byte top, byte bottom) => (byte)Math.Round((top * alpha) + (bottom * (1 - alpha)));
            return new(Mix(R, background.R), Mix(G, background.G), Mix(B, background.B));
        }

        public double Ratio(Rgb other)
        {
            var (lighter, darker) = Luminance() >= other.Luminance() ? (Luminance(), other.Luminance()) : (other.Luminance(), Luminance());
            return (lighter + 0.05) / (darker + 0.05);
        }

        private double Luminance() => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

        private static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}