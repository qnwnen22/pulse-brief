# Pulse Brief

Pulse Brief는 RSS 뉴스와 원문 기사 본문을 수집해 유사 이슈로 묶고, 카테고리별 일간/주간 이슈 요약을 제공하는 공개 뉴스 브리핑 서비스입니다.

공개 서비스는 [news.pulse-brief.co.kr](https://news.pulse-brief.co.kr)에서 운영합니다. 현재 배포 버전은 [VERSION](VERSION) 파일과 `/api/health` 응답을 기준으로 확인합니다.

## 현재 구조

소스 코드의 역할과 읽는 순서는 [소스 코드 안내](docs/architecture.md)를 참고하세요. 웹의 `Controllers`가 요청을 받고 `Services`가 업무 처리를 담당하며, 웹과 수집기는 `PulseBrief.Core`의 공통 서비스·모델·저장소를 참조합니다.

```text
사용자
-> Cloudflare DNS / Tunnel
-> AWS Lightsail Ubuntu
-> PulseBrief Web :8085
-> MongoDB localhost:27017
-> PulseBrief Collector
-> RSS / 원문 기사 / OpenAI API
```

운영 서버는 개인 PC가 아니라 AWS Lightsail Ubuntu 인스턴스입니다. 웹 서버와 수집기는 systemd 서비스로 분리되어 있고, 운영 MongoDB 데이터가 실제 서비스 기준 데이터입니다.

주요 운영 서비스:

- `pulsebrief-web`: ASP.NET Core 웹/API 서버
- `pulsebrief-collector`: RSS 수집, 본문 수집, 그룹화 작업자. 자동 요약 생성은 현재 중단 상태입니다.
- `mongod`: 운영 MongoDB
- `cloudflared`: Cloudflare Tunnel
- `pulsebrief-mongodb-backup.timer`: MongoDB 일일 백업

## 주요 기능

- RSS/Atom 피드 기반 뉴스 수집
- 기사 원문 본문과 대표 이미지 수집
- 유사 기사 그룹화와 카테고리 분류
- 뉴스 검색, 언론사 필터, 날짜/기사 수/정렬 필터
- 저장된 날짜를 선택해 확인하는 카테고리별 일간 이슈 요약
- 완료 주간 기준 주간 이슈 요약
- 관리자 페이지의 RSS 소스 관리, 기사/그룹 관리, 진단, 요약 재생성
- 운영 로그와 진단 API

## 요약 생성 기준

Windows 바탕화면의 **뉴스 요약 및 배포**를 실행하면 전날은 Codex로, 주간은 저장된 일간 요약을 로컬에서 합산하여 생성·검증한 뒤 운영 DB에 자동 반영합니다. 별도 초안 검토나 배포 승인은 없습니다. 뉴스 수집 파이프라인의 예약 요약 호출은 제거했으며, 공개 API는 사이트 접속 시 MongoDB에 저장된 요약만 반환합니다. 이슈 요약 화면에서는 최근 저장된 일간 요약 날짜를 선택해 과거 결과를 다시 볼 수 있습니다.

- 전날 요약 대상: 한국 시간 기준 전날 00시부터 당일 00시 직전까지의 기사.
- 주간 요약 대상: 최근 완료된 월요일부터 일요일까지의 일간 요약 7건. 대상은 월요일 00시에 바뀌며 주간 합산 자체는 AI를 호출하지 않습니다.
- 바탕화면 도구는 `summaries`에 기존 기간이 없을 때만 삽입하고 배포 상태를 확인합니다. 주간에 필요한 일간이 없으면 해당 날짜만 순차 생성·배포하며, 7일 자료를 확보하지 못하면 주간 배포를 중단합니다.
- 기존 서버 OpenAI 일간 및 주간 생성 코드는 관리자 호환을 위해 비활성 상태로 보존하지만 뉴스 수집 파이프라인에서는 호출하지 않습니다. 바탕화면 도구는 클릭했을 때만 실행하며 예약 작업을 설치하지 않습니다.

뉴스 수집은 `pulsebrief-collector`가 담당하며 기본 실행 주기는 `AutoRefreshMinutes=10`입니다.
수집 결과의 이슈 그룹은 새 계산 결과 저장이 성공한 뒤 이전 잔여 그룹을 정리하므로, 한 문서의 직렬화 오류가 뉴스 검색 전체를 비우지 않습니다. 운영 API가 빈 배열을 반환해도 사용자 화면에 개발용 샘플 뉴스는 표시하지 않습니다.

전날·주간 요약을 필요할 때 생성·배포하는 Windows 바탕화면 도구는 [뉴스 요약 실행 안내](docs/manual-summary-launcher.md)를 참고하세요. 배포된 기간은 건너뛰고, 저장된 미배포 결과는 재생성 없이 배포만 진행합니다. 기존 전날 전용 바로가기도 유지합니다.

## 로컬 실행

필요 조건:

- .NET 10 SDK
- MongoDB
- PowerShell

웹 서버 실행:

```powershell
dotnet run --urls http://localhost:4000
```

브라우저에서 `http://localhost:4000`으로 접속합니다.

Collector 1회 실행:

```powershell
.\tools\run-collector.ps1 -Once
```

Collector 주기 실행:

```powershell
.\tools\run-collector.ps1
```

기본 MongoDB 설정:

```text
mongodb://127.0.0.1:27017
database: pulsebrief
```

## 운영 배포

운영 배포는 `tools/cloud` 스크립트를 사용합니다.

배포 패키지 생성:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\cloud\publish-cloud.ps1
```

Lightsail 서버 반영:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\cloud\deploy-to-ubuntu.ps1 `
  -HostName SERVER_IP `
  -KeyPath C:\path\to\lightsail-key.pem `
  -SkipBootstrap `
  -StartServices
```

배포 패키지는 `.env`, `appsettings.Production.json`, API 키, SSH 키를 포함하지 않습니다. 운영 비밀값은 서버의 `/etc/pulsebrief/pulsebrief.env`에서 관리합니다.

대표 운영 환경 변수:

```bash
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:8085
Mongo__ConnectionString=mongodb://127.0.0.1:27017
Mongo__DatabaseName=pulsebrief
Collector__EnableInWebHost=false
Collector__AllowWebManualRefresh=false
OpenAI__ApiKey=REPLACE_ME
Security__AdminToken=REPLACE_ME
```

운영 서버 상태 확인:

```bash
curl -fsS http://127.0.0.1:8085/api/health
systemctl is-active pulsebrief-web pulsebrief-collector mongod cloudflared
```

## 운영 MongoDB 접근

운영 MongoDB는 서버 로컬에서만 열고, 필요할 때 SSH 터널로 접근합니다.

```powershell
.\tools\cloud\open-mongodb-tunnel.ps1 `
  -HostName SERVER_IP `
  -KeyPath C:\path\to\lightsail-key.pem
```

MongoDB Compass 접속 URI:

```text
mongodb://127.0.0.1:27018/pulsebrief
```

터널을 사용하는 동안에는 PowerShell 창을 열어 둬야 합니다.

## 백업과 복구

MongoDB 백업과 복구에는 MongoDB Database Tools의 `mongodump`, `mongorestore`가 필요합니다.

로컬 백업:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\backup-mongodb.ps1
```

로컬 복구:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\restore-mongodb.ps1 `
  -BackupPath .\backups\mongodb\pulsebrief_YYYYMMDD_HHMMSS `
  -ConfirmRestore
```

운영 서버에는 `pulsebrief-mongodb-backup.timer`를 통해 일일 백업을 두는 구성이 포함되어 있습니다.

## 버전 관리

서비스 버전은 SemVer 기준으로 관리합니다.

- `VERSION`
- `Directory.Build.props`
- `CHANGELOG.md`
- `/api/health`
- 사이트 푸터 버전 표시
- 사용자·관리자 HTML의 `js/*.js?v=...` 캐시 버스터

버전 갱신:

```powershell
.\tools\release-version.ps1 -Version 0.1.13 -Notes "변경 내용 1","변경 내용 2"
```

현재 운영 정책:

- 작업 완료 후 검증합니다.
- 서비스에 반영되는 변경은 SemVer 기준으로 버전을 올립니다.
- 한글 상세 커밋 메시지로 커밋하고 `master`에 push합니다.
- 배포가 필요한 변경은 Lightsail 서버에 배포합니다.
- Git 태그는 초기 개발 단계에서는 기본 생성하지 않고, 안정화 기준점이 필요할 때만 별도 확인 후 생성합니다.

## API

공개 API:

- `GET /api/health`: 서버 상태와 배포 버전
- `GET /api/briefs`: 프론트엔드 이슈 피드 데이터
- `GET /api/news-stats`: 수집기가 캐시한 한국 시간 기준 금일 기사 수
- `GET /api/daily-summary`: 저장된 전날 요약, `date=yyyy-MM-dd`로 특정 일간 요약 공개 조회
- `GET /api/daily-summary/dates`: 저장된 일간 요약 날짜를 최신순으로 최대 730개 조회
- `GET /api/weekly-summary`: 저장된 주간 요약

관리자 API:

- `GET /api/articles`: 저장 기사 목록과 본문 수집 상태
- `GET /api/groups`: 유사 기사 그룹 목록
- `POST /api/refresh`: 수집 파이프라인 수동 실행, 기본 운영 설정에서는 비활성
- `GET /api/admin/diagnostics`: 기사/그룹/요약/본문 수집/운영 로그 진단
- `POST /api/admin/fetch-missing-content`: 누락 본문 재수집
- `POST /api/admin/fetch-missing-images`: 누락 이미지 재수집
- `POST /api/admin/summaries/daily/regenerate`: 전날 요약 재생성
- `POST /api/admin/summaries/daily/preview`: 저장하지 않고 새 전날 요약 미리보기
- `POST /api/admin/summaries/weekly/regenerate`: 주간 요약 재생성
- `GET/POST/PATCH /api/admin/rss-feeds`: RSS 소스 관리

관리자 API는 `Security__AdminToken` 또는 관리자 세션 인증을 사용합니다.

## 문서

- [운영 관리 문서](docs/operations.md): 계정, 비용, 서버, MongoDB, 백업, 보안 운영 기준
- [클라우드 이전 런북](docs/cloud-migration.md): Lightsail Ubuntu 배포 절차
- [버전 관리 정책](docs/versioning.md): SemVer, 커밋, 배포, 태그 정책
- [개발 현황 문서](docs/development-summary.md): 초기 개발 과정 기록

## 보안 기준

- `.env`, API 키, SSH 키, `appsettings.Production.json`은 Git에 포함하지 않습니다.
- 운영 관리자 토큰은 서버 환경 변수로 관리합니다.
- 운영 MongoDB는 외부에 직접 공개하지 않습니다.
- Cloudflare Tunnel token과 OpenAI API key는 외부에 노출하지 않습니다.
- 배포 전후 `/api/health`와 systemd 서비스 상태를 확인합니다.
