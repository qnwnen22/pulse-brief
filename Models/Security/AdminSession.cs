namespace PulseBrief;

/// <summary>검증된 관리자 세션 쿠키에서 복원한 내부 세션 값입니다.</summary>
public readonly record struct AdminSession(string SessionId, DateTimeOffset ExpiresAt);
