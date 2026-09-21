# Pulse Brief

Pulse Brief는 여러 언론사의 RSS와 기사 원문을 수집하고, 유사한 보도를 하나의 이슈로 묶어 보여 주는 공개 뉴스 브리핑 서비스입니다. 뉴스 검색과 카테고리별 일간·주간 요약을 제공하며, 운영 데이터는 AWS Lightsail의 MongoDB를 기준으로 관리합니다.

- 공개 서비스: [https://news.pulse-brief.co.kr](https://news.pulse-brief.co.kr)
- 현재 버전: [`0.3.0`](VERSION)
- 변경 이력: [CHANGELOG.md](CHANGELOG.md)
- 기본 브랜치: `master`

## 현재 운영 상태

| 항목 | 현재 정책 |
| --- | --- |
| 뉴스 수집 | `pulsebrief-collector`가 시작 직후와 기본 10분 간격으로 자동 실행 |
| 일간 요약 | Windows 바탕화면 도구를 사용해 필요한 때 Codex로 수동 생성·검증·게시 |
| 주간 요약 | 완료된 월요일~일요일의 일간 요약 7건을 PC에서 합산해 게시. 합산 과정에는 AI를 사용하지 않음 |
| 자동 요약 | 서버와 뉴스 수집 파이프라인에서 비활성화. 사이트 접속도 요약을 생성하지 않음 |
| 운영 서버 | AWS Lightsail Ubuntu, 2GB RAM과 2GB swap |
| 운영 DB | 서버 내부 MongoDB `127.0.0.1:27017/pulsebrief` |
| 외부 공개 | Cloudflare DNS와 Tunnel을 통해 웹만 공개 |
| 배포 버전 | SemVer 사용. 초기 개발 단계이므로 Git 태그는 기본 생성하지 않음 |

버전 `0.3.0`에서는 저장된 일간 요약 날짜 목록을 공개 API로 제한 조회하고, 이슈 요약 화면에서 과거 날짜를 선택할 수 있게 했습니다. 날짜 목록은 요약 본문 없이 최대 730개만 읽어 운영 MongoDB 부하를 제한합니다.

## 주요 기능

- RSS/Atom 피드의 기사 제목, 링크, 발행 시각, 요약 수집
- 기사 원문의 본문과 대표 이미지 보강
- 로컬 임베딩 유사도를 이용한 관련 기사 그룹화
- 키워드, 카테고리, 언론사, 기간, 묶인 기사 수, 정렬 조건을 지원하는 뉴스 검색
- 수집기가 미리 계산해 둔 한국 시간 기준 금일 기사 수 표시
- 9개 카테고리의 일간 이슈 요약과 최근 완료 주간 요약
- 저장된 일간 요약 날짜 선택과 URL `summaryDate=yyyy-MM-dd` 상태 유지
- 요약 이슈에 기록된 기사 ID 기반 관련 원문 링크 제공
- RSS 소스, 기사, 그룹, 진단 정보를 관리하는 관리자 화면
- 모바일·데스크톱 반응형 사용자 화면
- MongoDB 일일 백업, systemd 자동 재시작, 운영 진단 로그

표준 카테고리는 다음 9개입니다.

`정치/정책`, `경제/산업`, `사회`, `국제`, `IT/과학`, `문화/연예`, `스포츠`, `생활/건강`, `지역`

## 시스템 구성

```text
사용자 브라우저
  -> Cloudflare DNS / Tunnel
  -> PulseBrief Web (ASP.NET Core, 127.0.0.1:8085)
  -> MongoDB (127.0.0.1:27017)

PulseBrief Collector
  -> RSS / 기사 원문
  -> 본문 보강 / 로컬 임베딩 / 이슈 그룹화
  -> MongoDB

Windows 요약 도구
  -> SSH로 운영 MongoDB의 대상 기사 제한 조회
  -> Codex CLI로 일간 요약 생성
  -> 로컬에서 주간 요약 합산
  -> 검증 후 MongoDB에 삽입
```

운영 서버의 주요 서비스는 다음과 같습니다.

| 서비스 | 역할 |
| --- | --- |
| `pulsebrief-web` | 정적 사용자·관리자 화면과 ASP.NET Core API 제공 |
| `pulsebrief-collector` | RSS 수집, 본문 보강, 임베딩, 그룹화, 금일 통계 갱신 |
| `mongod` | 기사, 그룹, 요약, 통계, 운영 로그 저장 |
| `cloudflared` | 공개 도메인을 로컬 웹 포트로 전달 |
| `pulsebrief-mongodb-backup.timer` | MongoDB 일일 백업 실행 |

웹과 수집기는 같은 `PulseBrief.Core`를 참조하지만 별도 프로세스로 실행됩니다. 웹 요청이 수집 작업을 직접 실행하지 않으며, 운영 설정에서는 웹의 수동 수집 API도 비활성화되어 있습니다.

## 기술 스택

| 영역 | 기술 |
| --- | --- |
| 백엔드 | .NET 10, ASP.NET Core Controller API |
| 공통 애플리케이션 | C# 14, `PulseBrief.Core` |
| 데이터베이스 | MongoDB 8.0, MongoDB .NET Driver 3.9 |
| HTML 분석 | AngleSharp 1.5 |
| 프론트엔드 | HTML, CSS, Vanilla JavaScript |
| 뉴스 분류 | 로컬 토큰 임베딩과 코사인 유사도 기반 그룹화 |
| 수동 AI 요약 | ChatGPT로 로그인한 Codex CLI |
| 운영 | AWS Lightsail Ubuntu, systemd, Cloudflare Tunnel |
| 도구 | PowerShell, Bash, Node.js |

서버에 남아 있는 OpenAI 기반 요약 구현과 관리자 재생성 API는 호환을 위해 보존되어 있지만 `Summary:EnableGeneration=false`로 중단되어 있습니다. 현재 일상적인 요약 생성은 OpenAI API 키를 사용하지 않습니다.

## 뉴스 수집 흐름

1. `config/rss-feeds.txt`에서 활성 RSS 주소를 읽습니다.
2. 피드의 기사를 `articles` 컬렉션에 upsert합니다.
3. 최근 기사 최대 5,000건만 수집 파이프라인의 처리 대상으로 읽습니다.
4. 실행당 최대 50건의 누락 본문·이미지를 최대 동시 4개 요청으로 보강합니다.
5. 로컬 임베딩을 계산하고 기본 유사도 임계값 `0.78`로 관련 기사를 묶습니다.
6. 새 그룹을 먼저 upsert한 뒤 저장에 성공한 경우에만 이전 실행의 잔여 그룹을 정리합니다.
7. 한국 시간 기준 금일 기사 수를 별도 캐시 문서로 갱신합니다.
8. 다음 실행까지 기본 10분을 기다립니다.

본문과 요약 텍스트는 UTF-16 문자 쌍을 보존해 잘라 이모지 등의 잘못된 서로게이트가 MongoDB 직렬화를 깨뜨리지 않도록 처리합니다. 공개 `/api/briefs`는 최근 이슈 그룹을 최대 1,000개만 반환하며 전체 기사 컬렉션을 매 요청마다 스캔하지 않습니다. 운영 API가 비어 있어도 사용자 화면에 개발용 샘플 데이터는 표시하지 않습니다.

RSS는 언론사별 전체뉴스 또는 대표 피드 하나를 우선합니다. 전체뉴스가 없는 언론사만 주요 섹션 피드를 제한적으로 등록하며, 전체뉴스와 여러 섹션을 함께 대량 등록하지 않습니다. 실제 목록과 주석은 [config/rss-feeds.txt](config/rss-feeds.txt)에서 관리합니다.

## 요약 운영 방식

### 일간 요약

- 대상은 한국 시간 전날 00:00 이상, 오늘 00:00 미만에 발행된 기사입니다.
- 운영 `summaries`에 같은 날짜가 있으면 기사 조회와 Codex 호출을 모두 건너뜁니다.
- 제목, RSS 요약 앞 350자, 저장 본문 앞 1,200자를 입력으로 사용합니다.
- 최대 150기사 또는 약 140,000자 단위로 나누어 Codex가 이슈를 분류·요약합니다.
- 기사 키 누락·중복, 날짜, 기사 수, 카테고리, 결과 크기를 검사합니다.
- 검증한 결과는 날짜 고유 인덱스가 있는 `summaries`에 `insertOne`으로만 저장하며 기존 요약을 덮어쓰지 않습니다.
- 게시 후 공개 API 응답과 저장 결과를 대조합니다.

### 주간 요약

- 대상은 가장 최근에 완료된 월요일 00:00부터 다음 월요일 00:00 직전까지입니다.
- 대상 주간은 일요일에서 월요일로 넘어가는 한국 시간 00:00에 바뀝니다.
- 일간 요약 7건을 운영 DB에서 날짜 인덱스로 한 건씩 읽습니다.
- 누락된 날짜만 일간 절차로 순차 생성하며, 7일 자료를 확보하지 못하면 주간 게시를 중단합니다.
- 주간 합산은 PC에서 수행하며 Codex와 OpenAI API를 호출하지 않습니다.
- 일간과 같은 삽입 전용 저장·중복 방지·복구·공개 API 검증 규칙을 사용합니다.

바탕화면의 **뉴스 요약 및 배포**는 `전날 확인 → 필요한 일간 생성·게시 → 주간 확인 → 누락 일간 보충 → 로컬 주간 합산 → 운영 DB 저장 → 공개 API 확인` 순서로 동작합니다. 여기서 **배포**는 요약 문서를 운영 DB에 게시한다는 뜻이며, 웹 코드를 빌드하거나 서버를 재시작하고 Git을 변경하는 작업은 아닙니다.

이 도구는 예약 작업이 아닙니다. PC에서 직접 실행할 때만 작동하며, 내부 복구 기록은 Git과 웹 배포에서 제외된 `data/` 아래에 저장합니다. 자세한 안전 상한, 복구 방식, 설치 방법은 [바탕화면 뉴스 요약 도구 안내](docs/manual-summary-launcher.md)를 참고하세요.

## 프로젝트 구조

```text
PulseBrief.sln
├─ PulseBrief.csproj              웹/API 서버
├─ Program.cs                     웹 진입점
├─ Controllers/                   HTTP 요청, 인증, 응답
├─ Services/                      웹 조회와 관리자 업무
├─ Models/                        웹 요청·응답과 세션 모델
├─ Infrastructure/                서버 구성, 보안 헤더, 시작 정보
├─ wwwroot/
│  ├─ js/                         사용자 화면 기능별 JavaScript
│  └─ admin/js/                   관리자 화면 기능별 JavaScript
├─ PulseBrief.Core/
│  ├─ Models/                     기사·그룹·요약 문서와 DTO
│  ├─ Repositories/               저장소 계약과 MongoDB 구현
│  ├─ Services/                   수집, 그룹화, 요약, 파이프라인, 운영
│  ├─ Mapping/                    저장 문서의 공개 API 변환
│  ├─ Configuration/              설정, 경로, RSS 읽기
│  └─ Utilities/                  날짜와 텍스트 처리
├─ PulseBrief.Collector/          독립 주기 수집기
├─ PulseBrief.Tests/              운영 DB 없는 API 회귀 테스트
├─ config/                        RSS 피드 목록
├─ manual-summaries/              과거 수동 요약 원본
├─ tools/
│  ├─ cloud/                      Ubuntu 배포, SSH 터널, 백업
│  └─ manual-summary/             Codex 일간·로컬 주간 요약 도구
└─ docs/                          아키텍처와 운영 문서
```

Controller, Service, Repository, Model/DTO, 정적 View의 책임과 기능별 읽는 순서는 [소스 코드 안내](docs/architecture.md)에 정리되어 있습니다. 서버 렌더링 Razor MVC가 아니라 Controller API와 정적 HTML/CSS/JavaScript를 결합한 구조입니다.

## 로컬 개발

### 필요 환경

- .NET 10 SDK
- MongoDB
- PowerShell 7 또는 Windows PowerShell
- Node.js: JavaScript 및 수동 요약 도구 테스트에 필요
- OpenSSH와 Codex CLI: 운영 수동 요약을 실행할 때만 필요

### 실행

저장소를 받은 뒤 빌드합니다.

```powershell
git clone https://github.com/qnwnen22/pulse-brief.git
cd pulse-brief
dotnet build .\PulseBrief.sln
```

MongoDB를 실행한 상태에서 웹 서버를 시작합니다.

```powershell
dotnet run --project .\PulseBrief.csproj --urls http://localhost:4000
```

브라우저에서 `http://localhost:4000`으로 접속합니다. 기본 DB는 `mongodb://127.0.0.1:27017/pulsebrief`입니다.

수집기를 한 번만 실행하거나 계속 실행할 수 있습니다.

```powershell
.\tools\run-collector.ps1 -Once
.\tools\run-collector.ps1
```

로컬 `.env`와 `appsettings.Production.json`은 Git에서 제외됩니다. 비밀값이나 개인 환경 설정은 이 파일 또는 환경 변수로 덮어쓰고 `appsettings.json`에는 넣지 않습니다.

### 핵심 설정

| 설정 | 기본값 | 설명 |
| --- | ---: | --- |
| `AutoRefreshMinutes` | `10` | 수집기 반복 간격(분) |
| `GroupSimilarityThreshold` | `0.78` | 유사 기사 그룹화 임계값 |
| `Collector:EnableInWebHost` | `false` | 웹 프로세스 내부 수집기 실행 여부 |
| `Collector:AllowWebManualRefresh` | `false` | 웹의 수동 수집 API 허용 여부 |
| `Collector:RunOnStartup` | `true` | 독립 수집기 시작 시 즉시 한 번 실행 |
| `Summary:EnableGeneration` | `false` | 서버 OpenAI 요약 생성 허용 여부 |
| `PublicBriefs:MaxGroups` | `1000` | 공개 이슈 피드 최대 그룹 수 |
| `Pipeline:ProcessingArticleLimit` | `5000` | 한 번에 재처리할 최근 기사 상한 |
| `ArticleContent:TimeoutSeconds` | `15` | 기사 원문 요청 제한 시간 |
| `ArticleContent:MaxArticlesPerRun` | `50` | 실행당 본문 보강 상한 |
| `ArticleContent:MaxConcurrency` | `4` | 본문 보강 동시 요청 수 |

전체 기본값은 [appsettings.json](appsettings.json)에서 확인합니다.

## 검증

기본 회귀 검증은 운영 MongoDB와 OpenAI API를 사용하지 않습니다.

```powershell
dotnet build .\PulseBrief.sln
dotnet run --project .\PulseBrief.Tests\PulseBrief.Tests.csproj --no-launch-profile
node .\tools\test-summary-rendering.cjs
node .\tools\test-static-assets.cjs
```

API 테스트는 메모리 저장소로 라우트, 모델 바인딩, 인증·CSRF, 요약 중단, 조회 상한, 과거 일간 요약과 BSON 호환성을 확인합니다. 화면 테스트는 일간·주간 요약, 관련 기사 링크, 정적 파일과 캐시 버전을 검사합니다.

수동 요약 도구는 별도로 검증합니다.

```powershell
node --test .\tools\manual-summary\summary-tool.test.cjs .\tools\manual-summary\weekly-tool.test.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\test-windows-launcher.ps1
```

MongoDB 없이 테스트 화면을 직접 확인하려면 다음 개발용 서버를 사용합니다.

```powershell
dotnet run --project .\PulseBrief.Tests\PulseBrief.Tests.csproj --no-launch-profile -- --serve
```

접속 주소는 `http://127.0.0.1:4187`입니다. 이 서버의 데이터와 관리자 토큰은 테스트 전용이며 운영 환경과 무관합니다.

## 수동 요약 도구

Windows 바탕화면 통합 바로가기를 설치합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\manual-summary\install-desktop-launcher.ps1 `
  -HostName SERVER_IP `
  -KeyPath C:\Users\YOUR_NAME\.ssh\pulse-brief-lightsail.pem
```

설치 전 `codex login status`에서 ChatGPT 로그인을 확인해야 합니다. 서버 IP나 SSH 키 경로가 바뀌면 설치 명령을 다시 실행합니다.

명령줄에서는 일간, 통합, 주간 또는 확인 전용 모드로 실행할 수 있습니다.

```powershell
# 특정 날짜의 일간 요약 생성·게시
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\start-daily-summary.ps1 -Date 2026-09-20

# 전날과 최근 완료 주간의 존재 여부만 확인
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\start-daily-summary.ps1 -Mode all -CheckOnly

# 최근 완료 주간과 필요한 누락 일간 처리
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\start-daily-summary.ps1 -Mode weekly
```

`-CheckOnly`는 기사 조회, Codex 호출, 결과 게시를 수행하지 않습니다.

## 운영 배포

### 패키지 생성

웹과 수집기를 함께 빌드하고 systemd 파일을 묶습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\cloud\publish-cloud.ps1
```

결과는 `publish/cloud/pulsebrief-cloud.zip`에 생성됩니다. 이 패키지는 `.env`, `appsettings.Production.json`, API 키, SSH 키를 포함하지 않습니다.

### Lightsail 반영

이미 구성된 운영 서버에는 다음 명령을 사용합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\cloud\deploy-to-ubuntu.ps1 `
  -HostName SERVER_IP `
  -UserName ubuntu `
  -KeyPath C:\path\to\pulse-brief-lightsail.pem `
  -SkipBootstrap `
  -StartServices
```

`-SkipBootstrap`은 Ubuntu, .NET, MongoDB, cloudflared와 서비스 계정이 이미 구성된 서버에서 사용합니다. 새 서버 최초 구성에만 bootstrap을 실행합니다. 배포 도구는 `/opt/pulsebrief/web`과 `/opt/pulsebrief/collector`를 갱신하고, `-StartServices`를 주면 두 서비스를 재시작한 뒤 내부 health API를 확인합니다.

### 운영 환경 변수

비밀값은 서버의 `/etc/pulsebrief/pulsebrief.env`에서 관리합니다.

```bash
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:8085
Mongo__ConnectionString=mongodb://127.0.0.1:27017
Mongo__DatabaseName=pulsebrief
Collector__EnableInWebHost=false
Collector__AllowWebManualRefresh=false
Collector__RunOnStartup=true
Summary__EnableGeneration=false
Security__AdminToken=REPLACE_WITH_A_STRONG_SECRET
Security__AllowLoopbackAdmin=false
```

현재 수동 Codex 요약 방식에는 `OPENAI_API_KEY`가 필요하지 않습니다. 기존 서버 OpenAI 요약을 명시적으로 재개할 때만 키와 비용·메모리 영향을 다시 검토해야 합니다. Cloudflare Tunnel 토큰은 애플리케이션 환경 파일과 분리해 cloudflared 서비스에서 관리합니다.

### 배포 확인

```bash
systemctl is-active pulsebrief-web pulsebrief-collector mongod cloudflared
curl -fsS http://127.0.0.1:8085/api/health
journalctl -u pulsebrief-web -n 100 --no-pager
journalctl -u pulsebrief-collector -n 100 --no-pager
```

외부에서는 `https://news.pulse-brief.co.kr/api/health`의 `ok`와 `version`, 사이트 푸터 버전을 확인합니다.

## 운영 MongoDB

운영 DB는 Lightsail 내부에서만 수신합니다. MongoDB의 `27017` 포트를 인터넷에 공개하지 않습니다.

PowerShell에서 SSH 터널을 엽니다.

```powershell
.\tools\cloud\open-mongodb-tunnel.ps1 `
  -HostName SERVER_IP `
  -KeyPath C:\path\to\pulse-brief-lightsail.pem
```

MongoDB Compass 연결 주소는 다음과 같습니다.

```text
mongodb://127.0.0.1:27018/pulsebrief
```

`27018`은 PC의 로컬 터널 포트입니다. Compass를 사용하는 동안 터널 PowerShell 창을 열어 두어야 하며, 실제 서버 MongoDB는 계속 `127.0.0.1:27017`에서 실행됩니다.

운영 데이터 조회는 인덱스, 날짜 범위, 투영 필드, `limit`, `maxTimeMS`를 사용해 범위를 제한합니다. 전체 기사나 그룹을 읽는 관리자 호환 API는 진단 목적 외에는 사용하지 않습니다.

## 백업과 복구

운영 서버의 `pulsebrief-mongodb-backup.timer`는 `mongodump`를 사용해 `/var/backups/pulsebrief/mongodb`에 일일 백업을 만들고 7일보다 오래된 디렉터리를 정리합니다.

```bash
systemctl status pulsebrief-mongodb-backup.timer --no-pager
sudo systemctl start pulsebrief-mongodb-backup.service
sudo find /var/backups/pulsebrief/mongodb -maxdepth 1 -mindepth 1 -type d
```

로컬 MongoDB는 다음 스크립트로 백업·복구할 수 있습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\backup-mongodb.ps1

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\restore-mongodb.ps1 `
  -BackupPath .\backups\mongodb\pulsebrief_YYYYMMDD_HHMMSS `
  -ConfirmRestore
```

복구는 기존 데이터를 바꿀 수 있으므로 대상 DB와 백업 경로를 확인한 뒤 실행합니다. 운영 절차와 정기 점검 항목은 [운영 관리 문서](docs/operations.md)를 참고하세요.

## 버전과 Git 정책

서비스 버전은 `MAJOR.MINOR.PATCH` 형식의 SemVer로 관리합니다.

- `VERSION`: 외부 표시 버전의 기준
- `Directory.Build.props`: NuGet·어셈블리·파일 버전
- `CHANGELOG.md`: 버전별 사용자·운영 변경 이력
- `wwwroot/index.html`, `wwwroot/admin/index.html`: CSS·JavaScript 캐시 버전
- `/api/health`, 사이트 푸터: 배포 결과 확인

다음 버전을 준비할 때 릴리스 도구를 사용합니다.

```powershell
.\tools\release-version.ps1 `
  -Version 0.3.1 `
  -Notes "수정 내용 1","수정 내용 2"
```

운영 정책은 다음과 같습니다.

- 사용자 기능이나 운영 코드 변경은 SemVer를 올리고 필요한 검증 후 배포합니다.
- 커밋 메시지는 한글 제목과 상세 본문으로 변경 이유와 검증 내용을 남깁니다.
- 완료한 변경은 `master`에 push합니다.
- 문서만 수정하고 서비스를 배포하지 않는 변경은 버전을 올리지 않습니다.
- Git 태그는 매 커밋·배포마다 만들지 않고 안정화, 외부 공유, 롤백 기준점이 필요할 때만 생성합니다.

세부 기준은 [버전 관리 정책](docs/versioning.md)을 따릅니다.

## API 개요

### 공개 API

| 메서드와 경로 | 설명 |
| --- | --- |
| `GET /api/health` | 서버 상태, 서비스 버전, 저장소 종류, RSS 수. 관리자만 OpenAI 키 존재 여부 확인 가능 |
| `GET /api/briefs` | 최근 이슈 그룹과 필요한 기사만 최대 설정 상한까지 조회 |
| `GET /api/news-stats` | 수집기가 캐시한 한국 시간 기준 금일 기사 수 조회 |
| `GET /api/daily-summary` | 한국 시간 기준 전날의 저장 일간 요약 조회. `date=yyyy-MM-dd`로 저장된 과거 날짜 조회 가능 |
| `GET /api/daily-summary/dates` | 저장된 일간 요약 날짜를 최신순 최대 730개 조회 |
| `GET /api/weekly-summary` | 한국 시간 기준 최근 완료 주간의 저장 요약 조회 |

공개 요약 API는 저장된 문서만 읽습니다. 자료가 없으면 `404`를 반환하며 요약을 만들거나 기존 문서를 변경하지 않습니다. `/api/weekly-summary?endDate=...`와 요약 API의 `force=true`는 관리자 인증이 필요합니다.

### 관리자 API

- `/admin`: 관리자 화면
- `/api/admin/login`, `/api/admin/session`, `/api/admin/logout`: 관리자 세션
- `/api/admin/articles`, `/api/admin/groups/{id}`: 기사 검색·상세와 기사/그룹 검수
- `/api/admin/rss-feeds`: RSS 추가·수정·삭제와 안내 페이지 관리
- `/api/admin/diagnostics`: 수집, 기사, 그룹, 요약, 운영 로그 진단
- `/api/admin/fetch-missing-content`, `/api/admin/fetch-missing-images`: 누락 데이터 제한 보강
- `/api/admin/refresh`: 수동 수집. 현재 운영 설정에서는 분리 수집기 정책으로 `409` 반환
- `/api/admin/summaries/*`: 서버 요약 재생성·미리보기. 현재 요약 중단 설정으로 `409` 반환
- `/api/articles`, `/api/groups`: 관리자 인증이 필요한 레거시 전체 조회 API

관리자 API는 `Security__AdminToken` 또는 관리자 세션을 사용합니다. 상태 변경 요청은 세션 인증과 함께 CSRF 검증을 거칩니다.

## 보안 원칙

- `.env`, `appsettings.Production.json`, SSH 키, API 키, 백업, `data/`, `publish/`를 Git에 포함하지 않습니다.
- 실제 서버 IP, 개인 PC 절대 경로, 비밀 토큰을 공개 문서와 로그에 기록하지 않습니다.
- MongoDB는 루프백에만 바인딩하고 필요할 때 SSH 터널로 접근합니다.
- 서버 방화벽은 SSH만 허용하고 웹은 Cloudflare Tunnel로 공개합니다.
- 관리자 토큰과 Cloudflare Tunnel 토큰은 서로 분리해 관리합니다.
- 기사 원문 본문은 공개 API와 사용자 화면에 노출하지 않습니다.
- 수동 요약의 Codex 실행에서는 셸, 웹 검색, 플러그인, 후크를 비활성화하고 기사 본문의 지시를 신뢰하지 않습니다.
- 비밀값이 노출되었다고 판단하면 Git 기록을 지우는 것만으로 끝내지 않고 해당 키를 즉시 폐기·재발급합니다.

## 관련 문서

- [소스 코드 안내](docs/architecture.md): 프로젝트 구조, 역할 경계, 기능별 코드 읽기 순서
- [바탕화면 뉴스 요약 도구](docs/manual-summary-launcher.md): 일간·주간 생성, 중복 방지, 복구, 안전 상한
- [운영 관리 문서](docs/operations.md): 계정, 비용, 서버, MongoDB, 백업, 장애 대응
- [클라우드 이전 런북](docs/cloud-migration.md): Lightsail Ubuntu 초기 구성과 이전 절차
- [버전 관리 정책](docs/versioning.md): SemVer, 커밋, 배포, 태그 기준
- [개발 현황 기록](docs/development-summary.md): 초기 개발 과정과 주요 결정

운영 설정이나 명령이 문서와 다를 때는 현재 소스의 `appsettings.json`, `tools/cloud`, systemd 파일과 실제 운영 서버 상태를 우선 확인합니다.
