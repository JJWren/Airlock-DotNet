using Microsoft.Extensions.Configuration.CommandLine;

namespace Airlock.Web;

/// <summary>Places a configuration source in its rank (Infrastructure Design, configuration sources).</summary>
internal static class ConfigurationOrder
{
    /// <summary>
    /// Moves the last source — U3's original variable names, which their extension appends — in front of the last command
    /// line source, so the command line still wins over everything. The host has an earlier one for its own settings,
    /// which comes before the JSON files.
    /// </summary>
    public static void MoveLastBeforeCommandLine(IList<IConfigurationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        for (var i = sources.Count - 2; i >= 0; i--)
        {
            if (sources[i] is CommandLineConfigurationSource)
            {
                var last = sources[^1];
                sources.RemoveAt(sources.Count - 1);
                sources.Insert(i, last);
                return;
            }
        }
    }
}
