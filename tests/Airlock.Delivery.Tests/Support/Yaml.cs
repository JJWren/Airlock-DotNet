using YamlDotNet.RepresentationModel;

namespace Airlock.Delivery.Tests.Support;

/// <summary>YAML files as YamlDotNet's representation model, with paths of keys to reach into them.</summary>
internal static class Yaml
{
    public static YamlMappingNode Load(string relativePath)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(Repository.Read(relativePath));
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    /// <summary>The node at a path of mapping keys, or null where a key is missing.</summary>
    public static YamlNode? Find(this YamlNode node, params string[] path)
    {
        var current = node;
        foreach (var key in path)
        {
            if (current is not YamlMappingNode mapping)
            {
                return null;
            }

            current = mapping.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode scalar && scalar.Value == key).Value;
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public static string? Text(this YamlNode node, params string[] path) => (node.Find(path) as YamlScalarNode)?.Value;

    public static YamlMappingNode Mapping(this YamlNode node, params string[] path) =>
        node.Find(path) as YamlMappingNode ?? throw new InvalidOperationException($"No mapping at {string.Join('.', path)}");

    public static IReadOnlyList<YamlNode> Items(this YamlNode node, params string[] path) =>
        node.Find(path) is YamlSequenceNode sequence ? [.. sequence.Children] : [];

    /// <summary>A sequence's scalars, or a lone scalar as a list of one.</summary>
    public static IReadOnlyList<string> Texts(this YamlNode node, params string[] path) => node.Find(path) switch
    {
        YamlSequenceNode sequence => [.. sequence.Children.OfType<YamlScalarNode>().Select(scalar => scalar.Value ?? string.Empty)],
        YamlScalarNode scalar => [scalar.Value ?? string.Empty],
        _ => [],
    };

    public static IReadOnlyList<string> Keys(this YamlNode node, params string[] path) =>
        node.Find(path) is YamlMappingNode mapping ? [.. mapping.Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value ?? string.Empty)] : [];

    /// <summary>A mapping's entries, keyed by their scalar keys, in file order.</summary>
    public static IEnumerable<KeyValuePair<string, YamlNode>> Entries(this YamlNode node, params string[] path) =>
        node.Find(path) is YamlMappingNode mapping
            ? mapping.Children.Select(pair => new KeyValuePair<string, YamlNode>(((YamlScalarNode)pair.Key).Value ?? string.Empty, pair.Value))
            : [];
}
