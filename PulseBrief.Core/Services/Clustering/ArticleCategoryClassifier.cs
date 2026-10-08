namespace PulseBrief;

public static class ArticleCategoryClassifier
{
    public const int StatisticsVersion = 1;
    public const int MetadataTitleLength = 800;
    public const int MetadataSummaryLength = 2000;

    private static readonly (string Category, string[] Words)[] CategoryRules =
    [
        ("정치/정책", ["정치", "대통령", "국회", "정부", "정책", "브리핑", "보도자료", "연설문", "국무회의", "위원회", "장관", "청와대", "대통령실", "국무조정실", "고용노동부", "과학기술정보통신부", "교육부", "국가보훈부", "국방부", "국토교통부", "기획예산처", "농림축산식품부", "문화체육관광부", "법무부", "법제처", "보건복지부", "산업통상부", "성평등가족부", "식품의약품안전처", "외교부", "인사혁신처", "재정경제부", "중소벤처기업부", "통일부", "해양수산부", "행정안전부", "경찰청", "검찰청", "국세청", "기상청", "소방청", "질병관리청", "검찰", "특검", "선거", "재선거", "대선", "총선", "지선", "보궐선거", "여당", "야당", "국민의힘", "민주당", "개혁신당", "조국혁신당", "의원", "원내대표", "당대표", "후보", "공천", "탄핵", "청문회", "국정조사", "법안", "개헌"]),
        ("문화/연예", ["연예", "문화", "영화", "음악", "방송", "드라마", "ott", "웹툰", "콘텐츠", "공연", "전시", "k-culture", "entertainment"]),
        ("스포츠", ["스포츠", "야구", "축구", "농구", "배구", "골프", "e스포츠", "격투기", "kbo", "lck", "월드컵", "프리미어리그", "올림픽"]),
        ("경제/산업", ["경제", "금융", "산업", "기업", "증시", "코스피", "코스닥", "환율", "금리", "물가", "투자", "부동산", "무역", "수출", "반도체", "자동차", "은행", "보험", "시장"]),
        ("IT/과학", ["it", "과학", "바이오", "ai", "인공지능", "기술", "데이터", "플랫폼", "보안", "모바일", "게임", "로봇", "우주", "엔비디아", "소프트웨어"]),
        ("국제", ["국제", "외교", "통일", "북한", "미국", "중국", "일본", "러시아", "유럽", "중동", "트럼프", "시진핑", "젤렌스키", "world"]),
        ("생활/건강", ["생활", "건강", "의료", "보건", "복지", "질병", "식품", "여행", "날씨", "환경", "기후", "교육", "노동", "고용"]),
        ("지역", ["지역", "지방", "수도권", "서울", "부산", "대구", "인천", "광주", "대전", "울산", "세종", "경기", "강원", "충북", "충남", "전북", "전남", "경북", "경남", "제주"]),
        ("사회", ["사회", "사건", "사고", "경찰", "소방", "법원", "재판", "범죄", "안전", "교통", "시민", "노조", "집회", "수사", "고발", "피의자", "구속", "압수수색", "공판", "선고", "혐의", "소송"])
    ];

    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(CategoryRules.Select(rule => rule.Category).ToArray());

    public static string ForMetadata(string source, string title, string summary) => ForArticles([new Article
    {
        Source = TextCleaner.Truncate(source, 300),
        Title = TextCleaner.Truncate(title, MetadataTitleLength),
        Summary = TextCleaner.Truncate(summary, MetadataSummaryLength)
    }]);

    public static string ForArticles(IEnumerable<Article> articles)
    {
        var articleList = articles.ToArray();
        var headlineText = string.Join(' ', articleList.Select(article => $"{article.Source} {article.Title}")).ToLowerInvariant();
        if (headlineText.Contains("et포토", StringComparison.OrdinalIgnoreCase)) return "문화/연예";

        var text = string.Join(' ', articleList.Select(article => $"{article.Source} {article.Title} {article.Summary} {article.Content}")).ToLowerInvariant();
        return CategoryRules
            .Select(rule => new { rule.Category, Score = rule.Words.Count(word => text.Contains(word.ToLowerInvariant(), StringComparison.Ordinal)) })
            .OrderByDescending(rule => rule.Score)
            .FirstOrDefault(rule => rule.Score > 0)?.Category ?? "사회";
    }
}
