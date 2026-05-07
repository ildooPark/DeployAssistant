using System;
using System.IO;
using System.Text.Json;
using DeployAssistant.Model;

namespace DeployAssistant.CLI.Engine;

/// <summary>
/// Reads the GUI's persisted config to discover the last-opened destination path.
/// Writes are handled by <c>MetaDataManager</c> via its internal <c>SettingManager</c>;
/// the CLI does not need to write this file directly.
/// </summary>
internal static class ConfigStore
{
    private const string ConfigFileName = "DeployAssistant.config";

    private static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ConfigFileName);

    public static string? LoadLastOpened()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var data = JsonSerializer.Deserialize<LocalConfigData>(File.ReadAllText(ConfigPath));
            return data?.LastOpenedDstPath;
        }
        catch
        {
            return null;
        }
    }
}
