namespace PulseBrief;

/// <summary>관리자 로그인 성공 후 클라이언트에 전달할 세션 정보입니다.</summary>
public sealed record AdminLoginResult(bool Authenticated, DateTimeOffset ExpiresAt, string CsrfToken);
