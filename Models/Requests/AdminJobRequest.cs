namespace PulseBrief;

/// <summary>관리자 작업 실행 요청 본문입니다.</summary>
public sealed class AdminJobRequest
{
    /// <summary>본문/이미지 재수집 등 일괄 작업에서 처리할 최대 기사 수입니다.</summary>
    public int? Limit { get; set; }

    /// <summary>일간 요약을 재생성할 날짜입니다. yyyy-MM-dd 형식이며 비어 있으면 전날입니다.</summary>
    public string? Date { get; set; }

    /// <summary>주간 요약을 재생성할 종료 날짜입니다. yyyy-MM-dd 형식이며 비어 있으면 오늘입니다.</summary>
    public string? EndDate { get; set; }
}
