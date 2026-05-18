namespace ApartmentTriage.Application.Agents.Manifest;

// Minimal YAML parser — no external dependency (locked stack forbids new NuGet packages).
// Handles flat key:value pairs and a single "retry:" subsection.
public static class AgentManifestLoader
{
    public static AgentManifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Agent manifest not found: {manifestPath}");

        return Parse(File.ReadAllText(manifestPath));
    }

    internal static AgentManifest Parse(string yaml)
    {
        var root = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var retry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inRetry = false;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

            if (line.TrimStart() == "retry:")
            {
                inRetry = true;
                continue;
            }

            // A non-indented line other than "retry:" ends the retry block
            if (!line.StartsWith(' ') && !line.StartsWith('\t'))
                inRetry = false;

            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            if (inRetry)
                retry[key] = value;
            else
                root[key] = value;
        }

        return new AgentManifest(
            AgentId: Required(root, "agent_id"),
            Model: Required(root, "model"),
            PromptVersion: Required(root, "prompt_version"),
            SystemPromptPath: Required(root, "system_prompt_path"),
            Retry: new AgentManifestRetry(
                MaxAttempts: int.Parse(retry.GetValueOrDefault("max_attempts", "3")),
                BackoffBaseSeconds: int.Parse(retry.GetValueOrDefault("backoff_base_seconds", "1"))));
    }

    private static string Required(Dictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val)
            ? val
            : throw new InvalidOperationException($"Required field '{key}' missing in agent manifest");
}
