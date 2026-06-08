namespace PulseBrief;

public sealed class ArticleClusterer(IConfiguration configuration)
{
    private readonly double _threshold = configuration.GetValue("GroupSimilarityThreshold", 0.78);

    private static readonly (string Category, string[] Words)[] CategoryRules =
    [
        ("영상/포토", ["다시보기", "영상", "포토", "사진", "숏폼", "카드/한컷", "라이브", "풀영상", "videomug", "subusu", "tv 뉴스"]),
        ("문화/연예", ["연예", "문화", "영화", "음악", "방송", "드라마", "ott", "웹툰", "콘텐츠", "공연", "전시", "k-culture", "entertainment"]),
        ("스포츠", ["스포츠", "야구", "축구", "농구", "배구", "골프", "e스포츠", "격투기", "kbo", "lck", "월드컵", "프리미어리그", "올림픽"]),
        ("정치/정책", ["정치", "대통령", "국회", "정부", "정책", "브리핑", "보도자료", "연설문", "국무회의", "위원회", "장관", "청와대", "대통령실", "국무조정실", "고용노동부", "과학기술정보통신부", "교육부", "국가보훈부", "국방부", "국토교통부", "기획예산처", "농림축산식품부", "문화체육관광부", "법무부", "법제처", "보건복지부", "산업통상부", "성평등가족부", "식품의약품안전처", "외교부", "인사혁신처", "재정경제부", "중소벤처기업부", "통일부", "해양수산부", "행정안전부", "경찰청", "검찰청", "국세청", "기상청", "소방청", "질병관리청", "검찰", "선거", "여당", "야당"]),
        ("경제/산업", ["경제", "금융", "산업", "기업", "증시", "코스피", "코스닥", "환율", "금리", "물가", "투자", "부동산", "무역", "수출", "반도체", "자동차", "은행", "보험", "시장"]),
        ("IT/과학", ["it", "과학", "바이오", "ai", "인공지능", "기술", "데이터", "플랫폼", "보안", "모바일", "게임", "로봇", "우주", "엔비디아", "소프트웨어"]),
        ("국제", ["국제", "외교", "통일", "북한", "미국", "중국", "일본", "러시아", "유럽", "중동", "트럼프", "시진핑", "젤렌스키", "world"]),
        ("생활/건강", ["생활", "건강", "의료", "보건", "복지", "질병", "식품", "여행", "날씨", "환경", "기후", "교육", "노동", "고용"]),
        ("지역", ["지역", "지방", "수도권", "서울", "부산", "대구", "인천", "광주", "대전", "울산", "세종", "경기", "강원", "충북", "충남", "전북", "전남", "경북", "경남", "제주"]),
        ("사회", ["사회", "사건", "사고", "경찰", "소방", "법원", "재판", "범죄", "안전", "교통", "시민", "노조", "집회"])
    ];

    public List<ArticleGroup> GroupSimilarArticles(IEnumerable<Article> articles)
    {
        var workingGroups = new List<WorkingGroup>();

        foreach (var article in articles.Where(article => article.Embedding is { Length: > 0 }))
        {
            WorkingGroup? bestGroup = null;
            var bestScore = 0.0;

            foreach (var group in workingGroups)
            {
                var score = CosineSimilarity(article.Embedding!, group.Centroid);
                if (score > bestScore)
                {
                    bestGroup = group;
                    bestScore = score;
                }
            }

            if (bestGroup is not null && bestScore >= _threshold)
            {
                bestGroup.Articles.Add(article);
                bestGroup.Centroid = Centroid(bestGroup.Articles);
            }
            else
            {
                workingGroups.Add(new WorkingGroup
                {
                    Centroid = article.Embedding!,
                    Articles = [article]
                });
            }
        }

        return workingGroups.Select((group, index) =>
        {
            var groupArticles = group.Articles
                .OrderByDescending(article => article.PublishedAt)
                .ThenByDescending(article => article.FirstSeenAt)
                .ToArray();
            var sourceBonus = groupArticles.Select(article => article.Source).Distinct().Count() * 4;

            return new ArticleGroup
            {
                Id = $"group-{index + 1}",
                Category = CategoryFor(groupArticles),
                ArticleIds = groupArticles.Select(article => article.Id).ToArray(),
                ArticleCount = groupArticles.Length,
                Sources = groupArticles.Select(article => article.Source).Distinct().ToArray(),
                LatestPublishedAt = groupArticles.FirstOrDefault()?.PublishedAt ?? DateTimeOffset.UtcNow,
                Score = Math.Min(100, 55 + groupArticles.Length * 8 + sourceBonus),
                SeedTitle = groupArticles.FirstOrDefault()?.Title ?? "새 이슈",
                SeedSummary = groupArticles.FirstOrDefault()?.Summary ?? ""
            };
        }).ToList();
    }

    private static string CategoryFor(IEnumerable<Article> articles)
    {
        var text = string.Join(' ', articles.Select(article => $"{article.Source} {article.Title} {article.Summary}")).ToLowerInvariant();
        return CategoryRules
            .Select(rule => new
            {
                rule.Category,
                Score = rule.Words.Count(word => text.Contains(word.ToLowerInvariant(), StringComparison.Ordinal))
            })
            .OrderByDescending(rule => rule.Score)
            .FirstOrDefault(rule => rule.Score > 0)?.Category ?? "사회";
    }

    private static double[] Centroid(IReadOnlyCollection<Article> articles)
    {
        var size = articles.FirstOrDefault()?.Embedding?.Length ?? 0;
        var vector = new double[size];
        if (size == 0) return vector;

        foreach (var article in articles)
        {
            for (var i = 0; i < size; i++) vector[i] += article.Embedding![i];
        }

        for (var i = 0; i < size; i++) vector[i] /= articles.Count;
        return vector;
    }

    private static double CosineSimilarity(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count) return 0;

        var sum = 0.0;
        for (var i = 0; i < a.Count; i++) sum += a[i] * b[i];
        return sum;
    }

    private sealed class WorkingGroup
    {
        public double[] Centroid { get; set; } = [];
        public List<Article> Articles { get; set; } = [];
    }
}
