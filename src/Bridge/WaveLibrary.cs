using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DgLabSocketSpire2.Bridge;

internal static class WaveLibrary
{
    private static readonly Dictionary<string, string[]> FallbackWaves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["呼吸"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A14141414",
            "0A0A0A0A28282828",
            "0A0A0A0A3C3C3C3C",
            "0A0A0A0A50505050",
            "0A0A0A0A64646464",
            "0A0A0A0A64646464",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000"
        },
        ["潮汐"] = new[]
        {
            "0A0A0A0A00000000",
            "0B0B0B0B10101010",
            "0D0D0D0D21212121",
            "0E0E0E0E32323232",
            "1010101042424242",
            "1212121253535353",
            "1313131364646464",
            "151515155C5C5C5C",
            "1616161654545454",
            "181818184C4C4C4C",
            "1A1A1A1A44444444",
            "0A0A0A0A00000000"
        },
        ["连击"] = new[]
        {
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A42424242",
            "0A0A0A0A21212121",
            "0A0A0A0A00000000",
            "0A0A0A0A00000000",
            "0A0A0A0A00000000"
        },
        ["快速按捏"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464"
        },
        ["按捏渐强"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A1C1C1C1C",
            "0A0A0A0A00000000",
            "0A0A0A0A34343434",
            "0A0A0A0A00000000",
            "0A0A0A0A49494949",
            "0A0A0A0A00000000",
            "0A0A0A0A57575757",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464"
        },
        ["心跳节奏"] = new[]
        {
            "7070707064646464",
            "7070707064646464",
            "7070707064646464",
            "0A0A0A0A00000000",
            "0A0A0A0A4B4B4B4B",
            "0A0A0A0A53535353",
            "0A0A0A0A5B5B5B5B",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000"
        },
        ["压缩"] = new[]
        {
            "4A4A4A4A64646464",
            "4545454564646464",
            "4040404064646464",
            "3B3B3B3B64646464",
            "3636363664646464",
            "3232323264646464",
            "2D2D2D2D64646464",
            "2828282864646464",
            "2323232364646464",
            "1E1E1E1E64646464",
            "1A1A1A1A64646464",
            "0A0A0A0A64646464"
        },
        ["颗粒摩擦"] = new[]
        {
            "0A0A0A0A64646464",
            "0B0B0B0B64646464",
            "0D0D0D0D64646464",
            "0F0F0F0F00000000",
            "0F0F0F0F64646464",
            "1111111164646464",
            "1313131364646464",
            "1414141400000000",
            "1414141464646464",
            "1616161664646464",
            "1818181864646464",
            "1A1A1A1A00000000"
        },
        ["变速敲击"] = new[]
        {
            "1818181864646464",
            "1818181864646464",
            "1818181800000000",
            "1818181800000000",
            "1818181864646464",
            "1818181864646464",
            "1818181800000000",
            "1818181800000000",
            "7070707064646464",
            "7070707064646464",
            "7070707064646464"
        },
        ["节奏步伐"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A14141414",
            "0A0A0A0A28282828",
            "0A0A0A0A3C3C3C3C",
            "0A0A0A0A50505050",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A19191919",
            "0A0A0A0A32323232",
            "0A0A0A0A4B4B4B4B",
            "0A0A0A0A64646464"
        }
    };

    private static readonly object Gate = new();
    private static Dictionary<string, string[]> _waves = new(FallbackWaves, StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            ReloadInternal();
            _initialized = true;
        }
    }

    public static void Reload()
    {
        lock (Gate)
        {
            ReloadInternal();
        }
    }

    public static string[] GetFrames(string waveName)
    {
        lock (Gate)
        {
            return _waves.TryGetValue(waveName, out var frames)
                ? frames
                : _waves["连击"];
        }
    }

    public static IReadOnlyCollection<string> Names
    {
        get
        {
            lock (Gate)
            {
                return _waves.Keys.OrderBy(static name => name).ToArray();
            }
        }
    }

    private static void ReloadInternal()
    {
        _waves = new Dictionary<string, string[]>(FallbackWaves, StringComparer.OrdinalIgnoreCase);
        var rootDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        LoadOfficialFile(Path.Combine(rootDir, "official_waves.json"));
        LoadCustomDirectory(Path.Combine(rootDir, "waves"));
        ModLog.Info($"Wave library loaded: {_waves.Count} waves.");
    }

    private static void LoadOfficialFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<OfficialWaveEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || entry.ExpectedV3 == null || entry.ExpectedV3.Length == 0)
                {
                    continue;
                }

                _waves[entry.Name] = entry.ExpectedV3;
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to load official waves file: {ex.Message}");
        }
    }

    private static void LoadCustomDirectory(string dirPath)
    {
        try
        {
            Directory.CreateDirectory(dirPath);
            foreach (var file in Directory.GetFiles(dirPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    string name;
                    string[] frames;

                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        name = doc.RootElement.TryGetProperty("name", out var nameElement)
                            ? nameElement.GetString() ?? Path.GetFileNameWithoutExtension(file)
                            : Path.GetFileNameWithoutExtension(file);
                        frames = doc.RootElement.TryGetProperty("frames", out var framesElement)
                            ? framesElement.EnumerateArray().Select(static element => element.GetString() ?? string.Empty).Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray()
                            : Array.Empty<string>();
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        name = Path.GetFileNameWithoutExtension(file);
                        frames = doc.RootElement.EnumerateArray().Select(static element => element.GetString() ?? string.Empty).Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
                    }
                    else
                    {
                        continue;
                    }

                    if (frames.Length == 0)
                    {
                        continue;
                    }

                    _waves[name] = frames;
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"Failed to load custom wave file {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to load custom wave directory: {ex.Message}");
        }
    }

    private sealed class OfficialWaveEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("expectedV3")]
        public string[] ExpectedV3 { get; set; } = Array.Empty<string>();
    }
}
