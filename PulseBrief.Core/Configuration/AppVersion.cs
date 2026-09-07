using System.Reflection;

namespace PulseBrief;

/// <summary>애플리케이션 빌드 산출물에 기록된 버전 정보를 읽어오는 도우미입니다.</summary>
public static class AppVersion
{
    /// <summary>현재 실행 중인 Pulse Brief 어셈블리의 표시 버전입니다.</summary>
    public static string Current =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
