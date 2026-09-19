using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Anibel.App.Playback;

internal static class AdaptiveVideoQualities
{
    // Metadata only: Windows still owns variant selection and segment downloads.
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
}
