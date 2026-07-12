using System.Text.Json;

namespace CodexTelegramCommon;

public sealed class RuntimeConfigStore(string path)
{
    public RuntimeConfig Load()
    {
        var config = JsonSerializer.Deserialize<RuntimeConfig>(AtomicFile.ReadUtf8(path), JsonDefaults.Options)
                     ?? throw new InvalidDataException("Runtime config is empty.");
        config.Validate();
        return config;
    }

    public void Save(RuntimeConfig config)
    {
        config.Validate();
        AtomicFile.WriteUtf8(path, JsonSerializer.Serialize(config, JsonDefaults.Options));
    }
}
