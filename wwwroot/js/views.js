// Page metadata and shared controls belong here, not in the navigation renderer.
const viewDefinitions = [
  {
    id: "briefing",
    label: "이슈 요약",
    icon: "⌁",
    eyebrow: "Briefing",
    title: "카테고리별 이슈 흐름 요약",
  },
  {
    id: "feed",
    label: "뉴스 검색",
    icon: "⌕",
    eyebrow: "News Search",
    title: "뉴스 검색과 원문 출처 확인",
    showNewsMetrics: true,
  },
  {
    id: "statistics",
    label: "통계",
    icon: "▥",
    eyebrow: "Statistics",
    title: "통계",
    showRefresh: false,
  },
  {
    id: "notice",
    label: "서비스 고지",
    icon: "ⓘ",
    eyebrow: "Service Notice",
    title: "서비스 고지와 운영 기준",
  },
];
