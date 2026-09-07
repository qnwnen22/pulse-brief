namespace PulseBrief;

/// <summary>운영 로그와 관리자 진단 API에서 사용하는 단일 이벤트 항목입니다.</summary>
public sealed class OperationalEvent
{
    /// <summary>이벤트가 기록된 UTC 시각입니다.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>이벤트 심각도입니다. info, warning, error 같은 값을 사용합니다.</summary>
    public string Level { get; init; } = "info";

    /// <summary>이벤트를 분류하기 위한 짧은 유형 문자열입니다.</summary>
    public string Type { get; init; } = "event";

    /// <summary>운영자가 읽을 수 있는 이벤트 설명입니다.</summary>
    public string Message { get; init; } = "";

    /// <summary>기사 본문이나 토큰을 제외한 이벤트 관련 보조 정보입니다.</summary>
    public object? Details { get; init; }
}
