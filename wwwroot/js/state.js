// Shared page state and DOM references; loaded before feature scripts.
const sampleIssues = [
  {
    title: "반도체 공급망 투자 경쟁이 다시 가속",
    category: "경제",
    source: "Market Daily",
    minutes: 8,
    impact: 92,
    heat: "hot",
    summary: "미국과 아시아 주요 기업의 설비 투자 발표가 이어지며 장비·소재 섹터까지 기대감이 확산되고 있습니다.",
    keywords: ["반도체", "공급망", "설비투자", "소재"],
  },
  {
    title: "생성형 AI 검색 서비스, 뉴스 유통 구조 흔든다",
    category: "기술",
    source: "Tech Signal",
    minutes: 13,
    impact: 88,
    heat: "hot",
    summary: "검색 결과가 링크 목록에서 답변형 요약으로 이동하면서 언론사와 플랫폼의 트래픽 배분 논의가 커지고 있습니다.",
    keywords: ["AI검색", "뉴스", "플랫폼", "저작권"],
  },
  {
    title: "폭염 대비 전력 수급 점검 이슈 부상",
    category: "사회",
    source: "Civic Wire",
    minutes: 21,
    impact: 76,
    heat: "normal",
    summary: "냉방 수요 증가가 예상되면서 지역별 전력 예비율, 취약계층 지원, 공공시설 냉방 운영이 함께 주목받고 있습니다.",
    keywords: ["폭염", "전력", "에너지", "안전"],
  },
  {
    title: "OTT 신작 공개 후 원작 IP 검색량 급등",
    category: "문화",
    source: "Culture Beat",
    minutes: 28,
    impact: 69,
    heat: "normal",
    summary: "시리즈 공개 직후 원작 웹툰과 배우 인터뷰 검색량이 동시에 뛰며 2차 콘텐츠 소비가 빠르게 늘고 있습니다.",
    keywords: ["OTT", "웹툰", "IP", "인터뷰"],
  },
  {
    title: "환율 변동성 확대에 수입 물가 우려",
    category: "경제",
    source: "Finance Now",
    minutes: 36,
    impact: 81,
    heat: "hot",
    summary: "달러 강세와 원자재 가격 흐름이 겹치며 기업 비용과 소비자 물가에 미칠 영향이 주요 관심사로 떠올랐습니다.",
    keywords: ["환율", "물가", "원자재", "수입"],
  },
  {
    title: "모바일 보안 업데이트 권고 확산",
    category: "기술",
    source: "Security Desk",
    minutes: 47,
    impact: 73,
    heat: "normal",
    summary: "주요 제조사가 긴급 패치를 배포하면서 피싱 문자, 악성 앱 권한, 업무용 단말 관리가 함께 언급되고 있습니다.",
    keywords: ["보안", "패치", "모바일", "피싱"],
  },
];

let issues = [...sampleIssues];
let dailyBrief = null;
let weeklyBrief = null;
let newsStats = null;
let activeFilter = "전체";
let currentPage = 1;
let activeWeeklyCategory = "전체";
const pageSize = 10;

const newsList = document.querySelector("#newsList");
const searchInput = document.querySelector("#searchInput");
const dateFilter = document.querySelector("#dateFilter");
const publisherFilter = document.querySelector("#publisherFilter");
const articleCountFilter = document.querySelector("#articleCountFilter");
const sortSelect = document.querySelector("#sortSelect");
const resetFiltersButton = document.querySelector("#resetFiltersButton");
const todayKeywords = document.querySelector("#todayKeywords");
const todayCount = document.querySelector("#todayCount");
const impactScore = document.querySelector("#impactScore");
const updateTime = document.querySelector("#updateTime");
const newsMetricGrid = document.querySelector("#newsMetricGrid");
const menuEyebrow = document.querySelector("#menuEyebrow");
const menuTitle = document.querySelector("#menuTitle");
const categoryFilters = document.querySelector("#categoryFilters");
const paginationControls = document.querySelector("#paginationControls");
const topPaginationControls = document.querySelector("#topPaginationControls");
const paginationContainers = [topPaginationControls, paginationControls].filter(Boolean);
const categorySummary = document.querySelector("#categorySummary");
const weeklySummary = document.querySelector("#weeklySummary");
const weeklyCategoryTabs = document.querySelector("#weeklyCategoryTabs");
const weeklyStats = document.querySelector("#weeklyStats");
const navItems = document.querySelectorAll(".nav-item[data-view]");
const viewPanels = document.querySelectorAll(".view-panel[data-panel]");
const appLoading = document.querySelector("#appLoading");
const refreshButton = document.querySelector("#refreshButton");
const appVersion = document.querySelector("#appVersion");

const koreaDateFormatter = new Intl.DateTimeFormat("en-CA", {
  timeZone: "Asia/Seoul",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

const preferredCategories = [
  "정치/정책",
  "경제/산업",
  "사회",
  "국제",
  "IT/과학",
  "문화/연예",
  "스포츠",
  "생활/건강",
  "지역",
];

const viewTitles = {
  briefing: {
    eyebrow: "Briefing",
    title: "카테고리별 이슈 흐름 요약",
  },
  feed: {
    eyebrow: "News Search",
    title: "뉴스 검색과 원문 출처 확인",
  },
  notice: {
    eyebrow: "Service Notice",
    title: "서비스 고지와 운영 기준",
  },
};
