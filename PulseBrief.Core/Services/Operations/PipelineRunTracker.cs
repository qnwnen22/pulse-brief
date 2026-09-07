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
