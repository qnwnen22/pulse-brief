namespace PulseBrief;

/// <summary>로컬 개발 및 IIS 배포 환경에서 .env 파일의 환경 변수를 로드합니다.</summary>
public static class DotEnv
{
    /// <summary>지정한 .env 파일을 읽어 아직 설정되지 않은 환경 변수만 프로세스 환경에 추가합니다.</summary>
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
