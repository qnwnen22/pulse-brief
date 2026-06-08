namespace PulseBrief;

/// <summary>현재 프로세스에서 실행된 뉴스 수집 파이프라인의 마지막 실행 상태를 보관합니다.</summary>
public sealed class PipelineRunTracker
{
    private readonly object _sync = new();
    private PipelineRunSnapshot _current = PipelineRunSnapshot.NotStarted();

    /// <summary>마지막으로 기록된 파이프라인 실행 상태를 반환합니다.</summary>
    public PipelineRunSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    /// <summary>새 파이프라인 실행이 시작되었음을 기록하고 실행 식별자를 반환합니다.</summary>
    public Guid MarkStarted()
    {
        var runId = Guid.NewGuid();
        lock (_sync)
        {
            _current = new PipelineRunSnapshot
            {
                RunId = runId,
                Status = "running",
                IsRunning = true,
                StartedAt = DateTimeOffset.UtcNow
            };
        }

        return runId;
    }

    /// <summary>파이프라인 실행 성공 결과를 마지막 실행 상태로 기록합니다.</summary>
    public void MarkCompleted(Guid runId, PipelineResult result)
    {
        lock (_sync)
        {
            if (_current.RunId != runId) return;

            _current = new PipelineRunSnapshot
            {
                RunId = runId,
                Status = "success",
                IsRunning = false,
                StartedAt = _current.StartedAt,
                FinishedAt = result.UpdatedAt,
                FetchedCount = result.FetchedCount,
                ArticleCount = result.ArticleCount,
                GroupCount = result.GroupCount
            };
        }
    }

    /// <summary>파이프라인 실행 실패 또는 취소 정보를 마지막 실행 상태로 기록합니다.</summary>
    public void MarkFailed(Guid runId, Exception error, string status = "failed")
    {
        lock (_sync)
        {
            if (_current.RunId != runId) return;

            _current = new PipelineRunSnapshot
            {
                RunId = runId,
                Status = status,
                IsRunning = false,
                StartedAt = _current.StartedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                ErrorType = error.GetType().Name,
                ErrorMessage = error.Message
            };
        }
    }
}

/// <summary>관리자 진단 API에 노출하는 마지막 파이프라인 실행 상태입니다.</summary>
public sealed class PipelineRunSnapshot
{
    /// <summary>개별 파이프라인 실행을 구분하기 위한 임시 식별자입니다.</summary>
    public Guid? RunId { get; init; }

    /// <summary>파이프라인 상태입니다. not_started, running, success, failed, cancelled 값을 사용합니다.</summary>
    public string Status { get; init; } = "not_started";

    /// <summary>현재 파이프라인이 실행 중인지 여부입니다.</summary>
    public bool IsRunning { get; init; }

    /// <summary>마지막 파이프라인 실행이 시작된 UTC 시각입니다.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>마지막 파이프라인 실행이 종료된 UTC 시각입니다.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>마지막 실행에서 RSS로 가져온 기사 항목 수입니다.</summary>
    public int? FetchedCount { get; init; }

    /// <summary>마지막 실행 후 저장소에 남아 있는 전체 기사 수입니다.</summary>
    public int? ArticleCount { get; init; }

    /// <summary>마지막 실행 후 계산된 이슈 그룹 수입니다.</summary>
    public int? GroupCount { get; init; }

    /// <summary>실패 또는 취소 시 기록한 예외 유형입니다.</summary>
    public string ErrorType { get; init; } = "";

    /// <summary>실패 또는 취소 시 운영자가 확인할 수 있는 오류 설명입니다.</summary>
    public string ErrorMessage { get; init; } = "";

    /// <summary>아직 파이프라인 실행 기록이 없는 초기 상태를 만듭니다.</summary>
    public static PipelineRunSnapshot NotStarted()
    {
        return new PipelineRunSnapshot();
    }
}
