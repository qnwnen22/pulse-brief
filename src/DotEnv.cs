namespace PulseBrief;

public static class DotEnv
{
    public static void Load(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) continue;

            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
