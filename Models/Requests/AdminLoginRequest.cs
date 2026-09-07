namespace PulseBrief;

/// <summary>관리자 로그인 요청 본문입니다.</summary>
public sealed class AdminLoginRequest
{
    /// <summary>운영 설정에 저장된 관리자 토큰입니다.</summary>
    public string Token { get; set; } = "";
}
