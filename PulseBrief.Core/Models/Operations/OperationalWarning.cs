namespace PulseBrief;

/// <summary>관리자 진단 API에서 운영자가 확인할 수 있는 상태 경고입니다.</summary>
public sealed class OperationalWarning
{
    /// <summary>경고 유형을 구분하는 안정적인 코드입니다.</summary>
    public string Code { get; init; } = "";

    /// <summary>경고 심각도입니다. info, warning, error 값을 사용합니다.</summary>
    public string Level { get; init; } = "warning";

    /// <summary>운영자가 읽을 수 있는 경고 설명입니다.</summary>
    public string Message { get; init; } = "";

    /// <summary>운영 경고 응답 모델을 생성합니다.</summary>
    public OperationalWarning(string code, string level, string message)
    {
        Code = code;
        Level = level;
        Message = message;
    }
}
