# Pulse Brief

RSS 뉴스와 기사 원문 본문을 수집해 유사 이슈로 그룹화하고, 카테고리별 전날/주간 이슈 요약을 제공하는 개인용 뉴스 브리핑 서버입니다.

## 실행

```powershell
dotnet run --urls http://localhost:4000
```

브라우저에서 `http://localhost:4000`으로 접속합니다.

## 구성

- ASP.NET Core 서버
- `wwwroot/` 정적 프론트엔드
- `config/rss-feeds.txt` RSS 피드 목록
- MongoDB Community Server 로컬 저장소
- AngleSharp 기반 기사 본문 추출
- 10분 주기 자동 RSS 갱신 `BackgroundService`

기본 MongoDB 연결값은 `mongodb://127.0.0.1:27017`, 데이터베이스 이름은 `pulsebrief`입니다.

## 배포 보안

외부 공개 배포 전에는 관리자 토큰을 환경 변수로 설정하는 것을 권장합니다.

```powershell
setx PULSEBRIEF_ADMIN_TOKEN "긴-랜덤-토큰"
```

관리자 전용 API는 `X-Admin-Token` 헤더가 일치할 때 허용됩니다. 로컬 개발 편의를 위해 `Security__AllowLoopbackAdmin=true`를 설정하면 루프백 요청도 관리자 요청으로 인정할 수 있지만, Cloudflare Tunnel, 리버스 프록시, 외부 공개 배포에서는 반드시 `false`로 유지하세요.

프로젝트의 `.env` 파일은 Git에 포함하지 않습니다. IIS 운영 환경에서는 가능하면 `.env`를 배포 산출물로 복사하지 말고 Windows 환경 변수 또는 App Pool 환경 변수로 관리하세요.

IIS 배포 폴더의 `appsettings.Production.json`에 운영 전용 관리자 토큰을 둘 수 있습니다. 이 파일은 Git 추적 대상이 아니며, 배포 스크립트가 기존 파일을 보존합니다.

배포 후 기본 점검:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test-deployment.ps1
```

관리자 토큰까지 함께 확인하려면 다음처럼 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test-deployment.ps1 -AdminToken "운영-관리자-토큰"
```

## 데이터 저장소

MongoDB 컬렉션은 다음 구조를 사용합니다.

- `articles`: RSS 기사, 원문 URL, 본문 추출 결과, 본문 수집 상태
- `articleGroups`: 유사 기사 그룹과 카테고리/중요도 정보
- `summaries`: 전날/주간 AI 요약 결과

SQLite 저장소는 제거되었고, 기존 SQLite 데이터는 MongoDB로 마이그레이션 완료된 상태입니다.

## 백업과 복구

MongoDB 백업과 복구에는 MongoDB Database Tools의 `mongodump`, `mongorestore`가 필요합니다. 현재 PC의 PATH에 도구가 없다면 MongoDB Database Tools를 설치하거나 스크립트 실행 시 도구 경로를 직접 전달하세요.

백업:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\backup-mongodb.ps1
```

복구:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\restore-mongodb.ps1 -BackupPath .\backups\mongodb\pulsebrief_YYYYMMDD_HHMMSS -ConfirmRestore
```

백업 결과물은 `backups/` 아래에 생성되며 Git에는 포함하지 않습니다. 배포 전, 스크래핑 로직 변경 전, 대량 데이터 정리 전에는 백업을 먼저 만들어 두는 것을 권장합니다.

## 운영 로그

수집 파이프라인 시작, 성공, 실패 같은 운영 이벤트는 기본적으로 실행 폴더의 `logs/` 아래 날짜별 `.log` 파일에 JSON Lines 형식으로 저장됩니다. 관리자 전용 `/api/admin/diagnostics` 응답에서도 현재 프로세스에서 기록한 최근 이벤트를 확인할 수 있습니다.

로그 저장 위치는 `OperationalLog:Directory`, 진단 API에 보관할 최근 이벤트 수는 `OperationalLog:RecentEventCount` 설정으로 조정할 수 있습니다.

## 운영 진단

관리자 전용 `/api/admin/diagnostics`는 기사 수, 본문 수집 성공률, 요약 생성 상태, 마지막 파이프라인 실행 상태와 함께 `warnings` 목록을 반환합니다. 기본 경고 기준은 최신 기사 갱신 12시간 지연, 최신 요약 생성 36시간 지연, 본문 수집 실패율 50% 이상, 중복 기사 비율 35% 이상입니다.

경고 기준은 `Diagnostics:StaleArticleHours`, `Diagnostics:StaleSummaryHours`, `Diagnostics:LongRunningPipelineMinutes`, `Diagnostics:ContentFetchFailureRateWarning`, `Diagnostics:DuplicateArticleRateWarning`, `Diagnostics:MinimumArticlesForRateWarnings` 설정으로 조정할 수 있습니다.

## 서버 파이프라인

1. RSS 수집
2. 기사 저장
3. 기사 URL 접근 및 본문 추출
4. 로컬 임베딩 생성
5. 유사 기사 그룹화
6. 전날/주간 요약 생성

## API

- `GET /api/health`: 서버 상태
- `GET /api/briefs`: 프론트엔드 이슈 피드 데이터
- `GET /api/daily-summary`: 전날 이슈 요약
- `GET /api/weekly-summary`: 주간 이슈 요약

관리자 전용 API:

- `GET /api/articles`: 저장된 기사 목록과 본문 수집 상태
- `GET /api/groups`: 유사 기사 그룹 목록
- `POST /api/refresh`: RSS 수집부터 요약 갱신까지 파이프라인 실행
- `GET /api/admin/diagnostics`: 기사/그룹/요약 수, 본문 수집 상태, 마지막 파이프라인 실행 상태, 운영 경고, 최근 운영 이벤트
- `POST /api/admin/fetch-missing-content`: 누락된 기사 본문 재수집
- `POST /api/admin/fetch-missing-images`: 누락된 기사 이미지 재수집

`/api/daily-summary`의 `date` 또는 `force` 파라미터, `/api/weekly-summary`의 `endDate` 또는 `force` 파라미터도 관리자 권한이 필요합니다.
