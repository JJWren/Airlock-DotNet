using System.Globalization;
using System.Text;
using Airlock.Domain.Intelligence;

namespace Airlock.Infrastructure.Nvd;

/// <summary>One NVD question: a CPE lookup (<c>cpeName</c>) or a product match (<c>virtualMatchString</c>) (IR-07).</summary>
internal sealed record NvdQuery(string Parameter, string Value)
{
    public static NvdQuery ForCpe(CandidateCpe cpe) => new("cpeName", cpe.Value);

    public static NvdQuery ForProduct(string product, string version) =>
        new("virtualMatchString", CpeMatchString.AnyVendor(product, version));

    // IR-07: rejected records left out, pages of up to 2,000 records from startIndex.
    public Uri ToUri(Uri baseUrl, int startIndex, int pageSize) =>
        new UriBuilder(baseUrl)
        {
            Query = string.Create(
                CultureInfo.InvariantCulture,
                $"{Parameter}={Uri.EscapeDataString(Value)}&noRejected&resultsPerPage={pageSize}&startIndex={startIndex}"),
        }.Uri;
}

/// <summary>The match string of a product match from any vendor (IR-07, S0 N12).</summary>
internal static class CpeMatchString
{
    public static string AnyVendor(string product, string version) =>
        $"cpe:2.3:a:*:{Escape(product)}:{Escape(version)}:*:*:*:*:*:*:*";

    // IR-07: escaped as a CPE 2.3 formatted string, as NVD writes its names: letters, digits, underscores, hyphens and
    // periods stand as they are, and every other character is quoted with a backslash.
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
