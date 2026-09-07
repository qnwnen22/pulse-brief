# 소스 코드 안내

이 문서는 Pulse Brief 소스를 처음 읽거나 수정할 때 사용하는 안내서입니다.

## 전체 구조

```text
PulseBrief.sln
├─ PulseBrief.csproj          웹/API 서버
│  ├─ Program.cs             웹 프로그램 실행
│  ├─ Infrastructure/        서버 설정, 보안 헤더, 시작 시각
│  ├─ Controllers/           HTTP 요청, 인증 확인, 응답
│  ├─ Services/              웹 전용 조회 및 관리자 업무 처리
│  ├─ Models/                관리자 요청·응답 및 세션 모델
│  └─ wwwroot/               사용자·관리자 HTML, CSS, JavaScript
├─ PulseBrief.Core/           웹과 수집기가 공유하는 라이브러리
│  ├─ DependencyInjection.cs 공통 기능 등록
│  ├─ Models/                기사·그룹·요약 문서 및 응답 모델
│  ├─ Repositories/          DB 조회·저장 규격과 MongoDB 구현
│  ├─ Services/              수집, 그룹화, 요약, 파이프라인, 운영
│  ├─ Mapping/               저장 모델을 뉴스 피드 응답으로 변환
│  ├─ Configuration/         설정 파일, RSS 출처, 환경 변수
│  ├─ Utilities/             한국 날짜, 텍스트 처리
│  └─ Hosting/               웹 내 수집을 위한 선택적 작업자
├─ PulseBrief.Collector/      별도로 실행되는 주기적 뉴스 수집기
├─ PulseBrief.Tests/          운영 DB 없이 실행하는 API 회귀 테스트
├─ config/                   RSS 설정
├─ manual-summaries/          검토·배포한 수동 요약 원본
├─ tools/                    실행, 검증, 버전, 배포, 백업 도구
└─ docs/                     운영·개발 안내
```

웹과 수집기는 `ProjectReference`로 같은 `PulseBrief.Core` 라이브러리를 사용합니다. 공통 소스를 각 실행 프로그램에 복사하여 컴파일하지 않습니다. 수집기는 웹 프로젝트나 Controller에 의존하지 않습니다.

## 역할별 기준

| 역할 | 책임 | 넣지 않을 내용 |
|---|---|---|
| Controller | 요청 값 해석, 인증·CSRF 확인, 상태 코드와 응답 | 뉴스 선별 알고리즘, MongoDB 쿼리 |
| Service | 조회 조합, 수집·요약·검수 등의 업무 처리 | 화면 DOM, HTTP 쿠키 처리 |
| Repository | DB 조회·저장, 인덱스, 조회 필드와 건수 제한 | HTML 생성, 뉴스 요약 문장 작성 |
| Model / DTO | 저장 문서 및 API 요청·응답의 데이터 구조 | DB 연결, 화면 이벤트 |
| View | HTML·CSS 및 JavaScript로 화면 표시와 사용자 입력 처리 | 운영 DB 직접 접근, 서버 비밀 값 |

`AdminAuthService`는 HTTP 쿠키와 토큰을 다루므로 웹 전용 `Services/Security`에 둡니다. `IArticleStore`와 `MongoArticleStore`는 기존 이름을 유지하면서 Repository 역할을 수행합니다.

이 구조는 Controller 기반 API와 정적 화면을 결합한 형태입니다. Razor View를 사용하는 서버 렌더링 MVC는 아닙니다. `PulseBrief.Core`에는 현재 MongoDB 구현과 외부 클라이언트도 포함되어 있으므로, 외부 의존성이 없는 순수 Domain 프로젝트를 뜻하지 않습니다.

## 기능을 따라 읽는 순서

### 뉴스 검색

1. `wwwroot/js/main.js`: 화면 초기화 시작.
2. `wwwroot/js/api.js`: `/api/briefs`, `/api/news-stats` 요청.
3. `Controllers/NewsController.cs`: 요청 접수.
4. `Services/NewsQueryService.cs`: 최근 그룹과 필요한 기사 조회를 조합.
5. `PulseBrief.Core/Repositories/MongoArticleStore.cs`: 실제 DB 접근.
6. `PulseBrief.Core/Mapping/ApiMapper.cs`: 화면용 응답 변환.
7. `wwwroot/js/news-feed.js`, `news-controls.js`: 뉴스 카드, 검색 필터, 페이지 표시.

### 이슈 요약 조회

1. `wwwroot/js/api.js`: 전날·주간 요약 API 요청.
2. `Controllers/SummariesController.cs`: 날짜·권한 확인.
3. `PulseBrief.Core/Services/Summaries/DailySummaryService.cs`: 저장된 요약 조회.
4. `SummaryLinkService.cs`: 기록된 기사 ID로 관련 원문 링크 조회.
5. `wwwroot/js/summaries.js`: 저장 요약의 카테고리와 본문 표시.

공개 화면 접속은 요약 생성 작업을 실행하지 않습니다. `Summary:EnableGeneration=false`를 유지하며, 현재 수동 요약은 사용자 검토 후 `tools/cloud/import-daily-summary.ps1`로 배포합니다. 생성 알고리즘 코드는 추후 재개를 위해 남아 있습니다.

`DailySummaryService`는 하나의 클래스를 `partial`로 분할했습니다. 기본 파일은 공개 메서드, `.Daily`는 일간 초안, `.Candidates`는 이슈 묶기, `.Scoring`은 후보 점수·근거 추출, `.Weekly`는 일간 요약 합산, `.Helpers`는 공통 보조 처리를 담습니다.

### 뉴스 수집

`PulseBrief.Collector/Program.cs` → `CollectorWorker.cs` → `PulseBrief.Core/Services/Pipeline/NewsPipeline.cs` 순서입니다. 파이프라인은 RSS 수집, 본문 보강, 로컬 임베딩, 유사 기사 그룹화, 저장, 금일 지표 갱신을 조정합니다.

### 관리자

| 기능 | Controller | 주요 Service |
|---|---|---|
| 로그인·세션 | `AdminSessionController` | `AdminAuthService` |
| 기사·그룹 검수 | `AdminContentController` | `AdminContentService` |
| RSS 관리 | `AdminFeedsController` | `AdminFeedService` |
| 진단·운영 작업 | `AdminOperationsController` | `OperationalDiagnosticsService`, `ArticleMaintenanceService` |
| 요약 재생성·미리보기 | `AdminSummariesController` | `DailySummaryService` |

기존 관리자 전체 조회 동작은 이번 파일 정리에서 변경하지 않았습니다. 운영 DB에 대해 전체 조회나 대량 재처리 테스트를 실행하지 않습니다.

## 화면 JavaScript

사용자 화면은 `wwwroot/js`, 관리자 화면은 `wwwroot/admin/js`에 있습니다. 두 화면 모두 `state.js`가 상태를 보관하고 `api.js`가 통신을 담당합니다. 나머지 파일은 기능별 화면 처리와 이벤트, 시작 코드를 나눕니다.

HTML의 `defer` 스크립트 목록이 실행 순서를 정의합니다. 현재는 일반 스크립트의 공통 스코프를 사용하므로, 파일을 추가할 때 중복 함수·변수 이름과 로드 순서를 확인합니다. 별도 번들러나 ES 모듈로 전환한 것은 아니며 기존 `file://` 미리보기도 유지합니다. 버전 도구는 사용자·관리자 HTML의 모든 JavaScript 캐시 버전을 함께 갱신합니다.

## 변경 및 검증

```powershell
dotnet build PulseBrief.sln
dotnet run --project PulseBrief.Tests/PulseBrief.Tests.csproj --no-launch-profile
node tools/test-summary-rendering.cjs
node tools/test-static-assets.cjs
```

API 테스트는 임시 설정 폴더와 메모리 저장소를 사용합니다. 운영 MongoDB나 OpenAI API를 호출하지 않고, 기존 28개 라우트, 요청 바인딩, 인증·CSRF, 요약 비활성화, 관련 링크와 BSON 호환성을 검증합니다.

DB 없이 화면을 살펴보는 개발용 서버:

```powershell
dotnet run --project PulseBrief.Tests/PulseBrief.Tests.csproj --no-launch-profile -- --serve
```

주소는 `http://127.0.0.1:4187`이며 테스트 뉴스만 표시합니다. 이 서버의 관리자 토큰은 테스트 코드에 정의된 전용 값이며 운영 인증과 무관합니다.

실제 웹 배포와 수집기 배포에는 각각 `PulseBrief.Core.dll`과 갱신된 의존성 파일이 필요합니다. `tools/cloud/publish-cloud.ps1`로 두 프로그램을 함께 빌드하면 참조 라이브러리가 포함됩니다.

## 운영 원칙

- 기존 API 주소, JSON 필드 및 MongoDB 문서 형식을 유지합니다.
- 운영 DB는 기간·필드·건수를 제한해서 읽고, 무거운 분석은 로컬에서 처리합니다.
- 구조를 바꾸는 작업에서도 수집 주기, 조회 상한, 자동 요약 중단 설정을 유지합니다.
- `.env`, 개인 키, 로컬 DB, 백업, 빌드 산출물을 Git에 올리지 않습니다.
- `publish`, `backups`, 다른 프로젝트 소스는 웹 프로젝트 컴파일 대상에서 제외합니다.
- 사용자 기능 변경 완료 후 한글 상세 커밋과 푸시를 진행하고, 배포 버전은 SemVer로 관리합니다. Git 태그는 현재 기본 생성하지 않습니다.
