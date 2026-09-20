using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Anibel.App.Playback;

internal static class AdaptiveVideoQualities
{
    // Read resolution metadata without downloading media segments.
    internal static Dictionary<uint, (uint Width, uint Height)> Parse(string manifest)
    {
        var result = new Dictionary<uint, (uint, uint)>();
        if (manifest.Length > 2 * 1024 * 1024) return result;
        void Add(string? rate, string? width, string? height)
        {
            if (uint.TryParse(rate, NumberStyles.None, CultureInfo.InvariantCulture, out var b) && b > 0
                && uint.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out var w) && w is > 0 and <= 32768
                && uint.TryParse(height, NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h is > 0 and <= 32768)
                result.TryAdd(b, (w, h));
        }
        if (manifest.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            foreach (var line in manifest.Split('\n'))
            {
                // Ignore I-frame playlists and audio renditions.
                if (!line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
                string? Attribute(string name) => Regex.Match(line, $@"(?:[:,])\s*{name}=([^,\r\n]+)", RegexOptions.CultureInvariant).Groups[1].Value;
                var size = Attribute("RESOLUTION")?.Split('x');
                if (size?.Length != 2) continue;
                Add(Attribute("BANDWIDTH"), size[0], size[1]);
            }
        }
        else
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(manifest), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
                var document = XDocument.Load(reader);
                foreach (var representation in document.Descendants().Where(e => e.Name.LocalName == "Representation"))
                {
                    var adaptation = representation.Parent;
                    string? Attribute(string name) => (string?)representation.Attribute(name) ?? (string?)adaptation?.Attribute(name);
                    var type = Attribute("contentType") ?? Attribute("mimeType");
                    if (type is not null && type != "video" && !type.StartsWith("video/", StringComparison.Ordinal)) continue;
                    Add((string?)representation.Attribute("bandwidth"), Attribute("width"), Attribute("height"));
                }
            }
            catch (XmlException) { /* Missing metadata must not prevent Windows playback. */ }
        }
        return result;
    }
    // A software decoder receives one video rendition plus every audio/subtitle rendition.
    // Absolute addresses keep segment resolution correct in the temporary local manifest.
    internal static string Select(string manifest, Uri address, uint bitrate)
    {
        if (!Parse(manifest).ContainsKey(bitrate)) throw new ArgumentOutOfRangeException(nameof(bitrate));
        if (manifest.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            var lines = new List<string>();
            var skipUri = false;
            foreach (var raw in manifest.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("#EXT-X-I-FRAME-STREAM-INF:", StringComparison.Ordinal)) continue;
                if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                {
                    var value = Regex.Match(line, @"(?:[:,])\s*BANDWIDTH=([0-9]+)(?:,|$)").Groups[1].Value;
                    skipUri = value != bitrate.ToString(CultureInfo.InvariantCulture);
                    if (skipUri) continue;
                }
                else if (line.Length > 0 && line[0] != '#')
                {
                    if (skipUri) { skipUri = false; continue; }
                    line = new Uri(address, line).AbsoluteUri;
                }
                line = Regex.Replace(line, "URI=\"([^\"]+)\"", m => "URI=\"" + new Uri(address, m.Groups[1].Value).AbsoluteUri + "\"");
                lines.Add(line);
            }
            return string.Join("\n", lines);
        }
        using var reader = XmlReader.Create(new StringReader(manifest), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        foreach (var representation in document.Descendants().Where(e => e.Name.LocalName == "Representation").ToArray())
        {
            var parent = representation.Parent;
            var type = (string?)representation.Attribute("mimeType") ?? (string?)parent?.Attribute("mimeType")
                ?? (string?)representation.Attribute("contentType") ?? (string?)parent?.Attribute("contentType");
            var video = type is "video" || type?.StartsWith("video/", StringComparison.Ordinal) == true
                || representation.Attribute("width") is not null || parent?.Attribute("width") is not null;
            if (video && (string?)representation.Attribute("bandwidth") != bitrate.ToString(CultureInfo.InvariantCulture))
                representation.Remove();
        }
        var root = document.Root!;
        var bases = root.Elements().Where(e => e.Name.LocalName == "BaseURL").ToArray();
        if (bases.Length == 0) root.AddFirst(new XElement(root.Name.Namespace + "BaseURL", new Uri(address, ".").AbsoluteUri));
        else foreach (var element in bases) element.Value = new Uri(address, element.Value).AbsoluteUri;
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
