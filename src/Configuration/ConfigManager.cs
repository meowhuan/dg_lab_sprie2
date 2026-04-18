using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DgLabSocketSpire2.Configuration;

public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly object _gate = new();
    private readonly string _configPath;
    private readonly string _legacyConfigPath;
    private ModConfig _config = new();

    public ConfigManager()
    {
        var rootDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        _configPath = Path.Combine(rootDir, "dglab_socket_spire2.cfg");
        _legacyConfigPath = Path.Combine(rootDir, "config.json");
    }

    public ModConfig Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_configPath) && File.Exists(_legacyConfigPath))
            {
                try
                {
                    var legacyJson = File.ReadAllText(_legacyConfigPath);
                    _config = JsonSerializer.Deserialize<ModConfig>(legacyJson, JsonOptions) ?? new ModConfig();
                    SaveUnsafe();
                    File.Delete(_legacyConfigPath);
                    ModLog.Info("Migrated legacy config.json to dglab_socket_spire2.cfg.");
                    return _config;
                }
                catch (Exception ex)
                {
                    ModLog.Error("Failed to migrate legacy config.json.", ex);
                }
            }

            if (!File.Exists(_configPath))
            {
                _config = new ModConfig();
                SaveUnsafe();
                return _config;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<ModConfig>(json, JsonOptions) ?? new ModConfig();
            }
            catch (Exception ex)
            {
                ModLog.Error("Failed to load config. Falling back to defaults.", ex);
                _config = new ModConfig();
            }

            if (_config.Presets.Count == 0)
            {
                _config.Presets = DefaultConfigFactory.CreatePresets();
            }
            else
            {
                var defaults = DefaultConfigFactory.CreatePresets();
                foreach (var (presetKey, defaultPreset) in defaults)
                {
                    if (_config.Presets.TryGetValue(presetKey, out var loadedPreset))
                    {
                        if (string.IsNullOrWhiteSpace(loadedPreset.DisplayName))
                        {
                            loadedPreset.DisplayName = defaultPreset.DisplayName;
                        }

                        if (string.IsNullOrWhiteSpace(loadedPreset.Description))
                        {
                            loadedPreset.Description = defaultPreset.Description;
                        }

                        foreach (var (eventKey, defaultRule) in defaultPreset.Rules)
                        {
                            if (loadedPreset.Rules.TryGetValue(eventKey, out var loadedRule))
                            {
                                if (loadedRule.Waves.Count == 0 && !string.IsNullOrWhiteSpace(loadedRule.Wave))
                                {
                                    loadedRule.Waves = new List<string> { loadedRule.Wave };
                                }

                                if (loadedRule.ChannelUsage == default && loadedRule.Channel == ChannelRef.B)
                                {
                                    loadedRule.ChannelUsage = ChannelUsageMode.BOnly;
                                }

                                if (loadedRule.Waves.Count == 0 && defaultRule.Waves.Count > 0)
                                {
                                    loadedRule.Waves = defaultRule.Waves.ToList();
                                }
                            }
                            else
                            {
                                loadedPreset.Rules[eventKey] = defaultRule;
                            }
                        }
                    }
                    else
                    {
                        _config.Presets[presetKey] = defaultPreset;
                    }
                }
            }

            return _config;
        }
    }

    public ModConfig Snapshot()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            return JsonSerializer.Deserialize<ModConfig>(json, JsonOptions) ?? new ModConfig();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            SaveUnsafe();
        }
    }

    public void Update(Action<ModConfig> mutator)
    {
        lock (_gate)
        {
            mutator(_config);
            SaveUnsafe();
        }
    }

    private void SaveUnsafe()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath) ?? ".");
        File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, JsonOptions));
    }
}
