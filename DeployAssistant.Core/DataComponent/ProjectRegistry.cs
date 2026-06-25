using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeployAssistant.DataComponent;

/// <summary>
/// Persists a list of previously configured projects (name + path) to a JSON file
/// in the user's Documents folder. Used by "Switch project" to offer quick selection.
/// </summary>
public static class ProjectRegistry
{
    private static string RegistryFileName = "DeployAssistant.projects.json";

    // Allow overriding for tests
    public static string RegistryDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static string RegistryPath => Path.Combine(RegistryDirectory, RegistryFileName);

    /// <summary>Load all saved project entries. Returns empty list on error.</summary>
    public static List<ProjectEntry> Load()
    {
        try
        {
            if (!File.Exists(RegistryPath)) return new List<ProjectEntry>();
            string json = File.ReadAllText(RegistryPath);
            var entries = JsonSerializer.Deserialize<List<ProjectEntry>>(json);
            return entries ?? new List<ProjectEntry>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"ProjectRegistry.Load: failed to load project registry from {RegistryPath}. Error: {ex.Message}");
            return new List<ProjectEntry>();
        }
    }

    /// <summary>
    /// Add or update a project entry. If a project with the same path already exists,
    /// its name and last-accessed time are updated.
    /// </summary>
    public static void SaveOrUpdate(string path, string name)
    {
        var entries = Load();
        string normalized = NormalizePath(path);

        var existing = entries.FirstOrDefault(e =>
            string.Equals(NormalizePath(e.Path), normalized, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Name = name;
            existing.LastAccessedUtc = DateTime.UtcNow;
        }
        else
        {
            entries.Add(new ProjectEntry
            {
                Name = name,
                Path = path,
                LastAccessedUtc = DateTime.UtcNow
            });
        }

        Persist(entries);
    }

    /// <summary>Remove a project entry by path.</summary>
    public static void Remove(string path)
    {
        var entries = Load();
        string normalized = NormalizePath(path);
        entries.RemoveAll(e =>
            string.Equals(NormalizePath(e.Path), normalized, StringComparison.OrdinalIgnoreCase));
        Persist(entries);
    }

    private static void Persist(List<ProjectEntry> entries)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(entries, options));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"ProjectRegistry.Persist: failed to write project registry to {RegistryPath}. Error: {ex.Message}");
        }
    }

    private static string NormalizePath(string p) =>
        p.TrimEnd('\\', '/');
}

public sealed class ProjectEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime LastAccessedUtc { get; set; }
}
