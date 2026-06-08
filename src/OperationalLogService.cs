using System.Text;
using System.Text.Json;

namespace PulseBrief;

/// <summary>운영 중 확인이 필요한 수집, 배포, 오류 이벤트를 파일과 메모리 버퍼에 기록합니다.</summary>
public sealed class OperationalLogService(IConfiguration configuration, IWebHostEnvironment environment)
{
    private readonly object _sync = new();
    private readonly Queue<OperationalEvent> _recentEvents = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _logDirectory = configuration["OperationalLog:Directory"]
        ?? Path.Combine(environment.ContentRootPath, "logs");
    private readonly int _maxRecentEvents = Math.Clamp(configuration.GetValue("OperationalLog:RecentEventCount", 100), 10, 500);

    /// <summary>운영 이벤트를 최근 이벤트 버퍼와 날짜별 로그 파일에 기록합니다.</summary>
    public Task RecordAsync(string level, string type, string message, object? details = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;

        var item = new OperationalEvent
        {
            CreatedAt = DateTimeOffset.UtcNow,
            Level = Normalize(level, "info"),
            Type = Normalize(type, "event"),
            Message = message.Trim(),
            Details = details
        };

        try
        {
            lock (_sync)
            {
                _recentEvents.Enqueue(item);
                while (_recentEvents.Count > _maxRecentEvents)
                {
                    _recentEvents.Dequeue();
                }

                Directory.CreateDirectory(_logDirectory);
                var logPath = Path.Combine(_logDirectory, $"pulsebrief-{item.CreatedAt:yyyyMMdd}.log");
                File.AppendAllText(logPath, JsonSerializer.Serialize(item, _jsonOptions) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"[operational-log] write failed: {error.GetType().Name} {error.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>현재 프로세스에서 기록한 최근 운영 이벤트를 최신순으로 반환합니다.</summary>
    public OperationalEvent[] ReadRecentEvents(int limit = 20)
    {
        var safeLimit = Math.Clamp(limit, 1, _maxRecentEvents);
        lock (_sync)
        {
            return _recentEvents
                .Reverse()
                .Take(safeLimit)
                .ToArray();
        }
    }

    /// <summary>빈 값이나 공백이 들어온 로그 속성을 안전한 기본값으로 변환합니다.</summary>
    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

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
