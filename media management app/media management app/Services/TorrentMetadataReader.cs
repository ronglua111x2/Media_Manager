using System.Text;
using media_management_app.Models;

namespace media_management_app.Services;

public static class TorrentMetadataReader
{
    public static TorrentMetadataProbeResult Read(byte[] torrentBytes)
    {
        var parser = new BencodeParser(torrentBytes);
        if (parser.Parse() is not Dictionary<string, object> root ||
            !root.TryGetValue("info", out var infoValue) ||
            infoValue is not Dictionary<string, object> info)
        {
            return new TorrentMetadataProbeResult
            {
                IsAvailable = false,
                Reason = "torrent metadata does not contain an info dictionary"
            };
        }

        if (!info.ContainsKey("files") && !info.ContainsKey("length"))
        {
            // BitTorrent v2-only ("file tree"): not read here; qBittorrent lists the files after the add.
            return new TorrentMetadataProbeResult
            {
                IsAvailable = false,
                Reason = "torrent metadata has no v1 file list"
            };
        }

        var torrentName = ReadString(info, "name");
        var files = ReadFiles(info, torrentName).ToList();
        return new TorrentMetadataProbeResult
        {
            IsAvailable = true,
            Reason = "torrent metadata parsed",
            TorrentName = torrentName,
            Files = files,
            TotalSize = files.Sum(file => file.Length),
            InfoHashV1 = parser.InfoEnd > parser.InfoStart
                ? Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    torrentBytes.AsSpan(parser.InfoStart, parser.InfoEnd - parser.InfoStart))).ToLowerInvariant()
                : null
        };
    }

    private static IEnumerable<TorrentMetadataFile> ReadFiles(Dictionary<string, object> info, string torrentName)
    {
        if (info.TryGetValue("files", out var filesValue) &&
            filesValue is List<object> fileEntries)
        {
            foreach (var entry in fileEntries.OfType<Dictionary<string, object>>())
            {
                // BEP 47 padding files are not real files; qBittorrent does not list them either.
                if (ReadString(entry, "attr").Contains('p', StringComparison.Ordinal))
                {
                    continue;
                }

                var path = ReadPath(entry);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                yield return new TorrentMetadataFile
                {
                    Path = path,
                    Length = ReadLong(entry, "length")
                };
            }

            yield break;
        }

        yield return new TorrentMetadataFile
        {
            Path = torrentName,
            Length = ReadLong(info, "length")
        };
    }

    private static string ReadPath(Dictionary<string, object> entry)
    {
        if (!entry.TryGetValue("path", out var pathValue) ||
            pathValue is not List<object> parts)
        {
            return string.Empty;
        }

        return string.Join(Path.DirectorySeparatorChar, parts.Select(ReadStringValue).Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ReadString(Dictionary<string, object> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value) ? ReadStringValue(value) : string.Empty;
    }

    private static string ReadStringValue(object value)
    {
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => string.Empty
        };
    }

    private static long ReadLong(Dictionary<string, object> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value) && value is long number ? number : 0;
    }

    private sealed class BencodeParser
    {
        private readonly byte[] _bytes;
        private int _position;
        private int _depth;

        /// <summary>Byte range of the top-level "info" value (its SHA-1 is the v1 infohash).</summary>
        public int InfoStart { get; private set; }

        public int InfoEnd { get; private set; }

        public BencodeParser(byte[] bytes)
        {
            _bytes = bytes;
        }

        public object Parse()
        {
            if (_position >= _bytes.Length)
            {
                throw new InvalidOperationException("Unexpected end of bencoded data.");
            }

            return _bytes[_position] switch
            {
                (byte)'d' => ParseDictionary(),
                (byte)'l' => ParseList(),
                (byte)'i' => ParseInteger(),
                >= (byte)'0' and <= (byte)'9' => ParseBytes(),
                _ => throw new InvalidOperationException("Unsupported bencoded value.")
            };
        }

        private Dictionary<string, object> ParseDictionary()
        {
            _position++;
            _depth++;
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            while (ReadCurrent() != (byte)'e')
            {
                var key = Encoding.UTF8.GetString(ParseBytes());
                var start = _position;
                values[key] = Parse();
                if (_depth == 1 && key == "info")
                {
                    InfoStart = start;
                    InfoEnd = _position;
                }
            }

            _position++;
            _depth--;
            return values;
        }

        private List<object> ParseList()
        {
            _position++;
            var values = new List<object>();
            while (ReadCurrent() != (byte)'e')
            {
                values.Add(Parse());
            }

            _position++;
            return values;
        }

        private long ParseInteger()
        {
            _position++;
            var start = _position;
            while (ReadCurrent() != (byte)'e')
            {
                _position++;
            }

            var text = Encoding.ASCII.GetString(_bytes, start, _position - start);
            _position++;
            return long.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        private byte[] ParseBytes()
        {
            var start = _position;
            while (ReadCurrent() != (byte)':')
            {
                _position++;
            }

            var lengthText = Encoding.ASCII.GetString(_bytes, start, _position - start);
            _position++;
            var length = int.Parse(lengthText, System.Globalization.CultureInfo.InvariantCulture);
            var bytes = _bytes.Skip(_position).Take(length).ToArray();
            _position += length;
            return bytes;
        }

        private byte ReadCurrent()
        {
            if (_position >= _bytes.Length)
            {
                throw new InvalidOperationException("Unexpected end of bencoded data.");
            }

            return _bytes[_position];
        }
    }
}
