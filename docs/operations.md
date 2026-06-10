# Pulse Brief 운영 관리 문서

최종 확인일: 2026-06-10

이 문서는 Pulse Brief 서비스를 운영하기 위해 관리해야 하는 계정, 비용, 서버, 데이터, 배포, 보안 항목을 정리한다.

## 1. 현재 운영 구조

```text
사용자
-> https://news.pulse-brief.co.kr
-> Cloudflare DNS / Tunnel
-> AWS Lightsail Ubuntu 서버
-> PulseBrief Web :8085
-> MongoDB localhost:27017
-> PulseBrief Collector
-> OpenAI API 일일 요약
```

현재 운영 서버:

| 항목 | 값 |
| --- | --- |
| Public IPv4 | `13.124.97.153` |
| Private IPv4 | `172.26.15.242` |
| Public IPv6 | `2406:da12:5d6:3300:c280:133d:3808:afb4` |
| OS | Ubuntu 24.04 LTS |
| SSH alias | `pulse-brief-prod` |
| 공개 URL | `https://news.pulse-brief.co.kr` |
| Web origin | `http://127.0.0.1:8085` |
| MongoDB | `127.0.0.1:27017`, database `pulsebrief` |

운영 서비스:

| systemd 서비스 | 역할 |
| --- | --- |
| `pulsebrief-web` | ASP.NET Core 웹 서버 |
| `pulsebrief-collector` | RSS 수집 및 요약 생성 |
| `mongod` | 운영 MongoDB |
| `cloudflared` | Cloudflare Tunnel |
| `pulsebrief-mongodb-backup.timer` | MongoDB 일일 백업 |

## 2. 관리해야 하는 핵심 항목

비용과 계정 기준으로는 아래 5개가 핵심이다.

1. 프로젝트 소스 코드 / GitHub
2. OpenAI API 사용량 / 비용
3. Cloudflare DNS / Tunnel 계정
4. AWS Lightsail 서버 비용
5. 닷홈 도메인 연장 비용

운영 안정성 기준으로는 아래 항목도 반드시 관리 대상이다.

6. 운영 MongoDB 데이터 / 백업
7. 비밀값 / 접근 권한
8. 서버 상태 모니터링
9. 배포 / 롤백 절차
10. 보안 업데이트

## 3. 비용 관리

### 3.1 GitHub

현재 저장소:

```text
https://github.com/qnwnen22/pulse-brief.git
```

현재 브랜치:

```text
master
```

관리 포인트:

- 소스 코드가 GitHub에 push되어 있어야 한다.
- `.env`, SSH 키, API 키, `appsettings.Production.json`은 Git에 넣지 않는다.
- GitHub Actions를 과도하게 사용하지 않는 한 현재 규모에서는 별도 비용 가능성이 낮다.
- 유료 플랜 사용 여부는 GitHub Billing에서 별도 확인한다.

### 3.2 OpenAI API

현재 사용 목적:

- 전날 뉴스 요약 생성
- 주간 뉴스 요약은 저장된 일간 요약 기반 로컬 집계로 생성
- 현재 설정 모델: `gpt-5.4-nano`

현재 공식 가격 확인 기준:

| 모델 | Input | Cached input | Output |
| --- | ---: | ---: | ---: |
| `gpt-5.4-nano` Standard | `$0.20 / 1M tokens` | `$0.02 / 1M tokens` | `$1.25 / 1M tokens` |

관리 포인트:

- 비용은 웹 방문자 수보다 수집 기사 수와 요약 생성량에 더 영향을 받는다.
- 전날 요약은 선별된 대표 기사 본문 일부만 OpenAI에 전달하고, 주간 요약은 OpenAI를 호출하지 않는다.
- OpenAI Billing에서 monthly budget 또는 hard limit을 설정한다.
- API 키는 서버의 `/etc/pulsebrief/pulsebrief.env`에 있다.
- API 키를 교체하면 `pulsebrief-web`, `pulsebrief-collector`를 재시작한다.

공식 확인 링크:

- https://platform.openai.com/docs/pricing

### 3.3 Cloudflare

현재 역할:

- `pulse-brief.co.kr` DNS 관리
- `news.pulse-brief.co.kr`을 Cloudflare Tunnel로 라우팅
- 서버의 80/443 포트를 열지 않고 HTTPS 공개

현재 네임서버:

```text
addilyn.ns.cloudflare.com
alberto.ns.cloudflare.com
```

현재 비용 구조:

| 항목 | 현재 사용 | 비용 |
| --- | --- | --- |
| DNS | Cloudflare | Free 가능 |
| Tunnel | cloudflared | Free 가능 |
| Pro plan | 미사용 | 필요 시 `$20/mo` annually 또는 `$25/mo` monthly |

관리 포인트:

- Cloudflare 계정 MFA를 켠다.
- Tunnel token을 외부에 노출하지 않는다.
- `news.pulse-brief.co.kr -> http://127.0.0.1:8085` 설정을 유지한다.
- DNS가 닷홈이 아니라 Cloudflare에서 관리되는 상태인지 유지한다.

공식 확인 링크:

- https://www.cloudflare.com/plans/

### 3.4 AWS Lightsail

현재 사용:

| 항목 | 값 |
| --- | --- |
| 서비스 | AWS Lightsail |
| 플랜 | Linux/Unix 2GB with public IPv4 |
| 월 비용 | `$12 / month` |
| 사양 | 2GB RAM, 2 vCPU, 60GB SSD |
| 포함 전송량 | 3TB Transfer |

관리 포인트:

- AWS Billing Alert를 설정한다.
- Lightsail instance metric을 주기적으로 확인한다.
- 3TB transfer 초과 시 outbound overage가 발생할 수 있다.
- Static IP는 인스턴스에 붙어 있어야 한다.
- Snapshot을 켜면 별도 저장 비용이 발생할 수 있다.

공식 확인 링크:

- https://aws.amazon.com/lightsail/pricing/

### 3.5 닷홈 도메인

현재 도메인:

```text
pulse-brief.co.kr
```

현재 공개 호스트:

```text
news.pulse-brief.co.kr
```

닷홈에서 관리할 것:

- 도메인 만료일
- 연장 결제
- 네임서버가 Cloudflare로 유지되는지
- 도메인 소유자 연락처가 유효한지

닷홈 가격 페이지 확인 기준:

| 도메인 | 가격 표시 |
| --- | --- |
| `.co.kr` | 이벤트가 `14,000원/년`, 일반가 `20,000원/년`로 표시됨 |

가격은 이벤트/쿠폰/부가세/연장 조건에 따라 달라질 수 있으므로 결제 전 닷홈 마이페이지에서 최종 금액을 확인한다.

공식 확인 링크:

- https://www.dothome.co.kr/domain/

## 4. 비밀값과 접근 권한

관리 대상:

| 항목 | 위치 / 설명 |
| --- | --- |
| OpenAI API Key | 서버 `/etc/pulsebrief/pulsebrief.env` |
| 관리자 토큰 | 서버 `/etc/pulsebrief/pulsebrief.env` |
| Cloudflare Tunnel Token | 서버 cloudflared 서비스에 설치됨 |
| Lightsail SSH Key | 로컬 `C:\Users\User\.ssh\pulse-brief-lightsail.pem` |
| SSH alias | 로컬 `C:\Users\User\.ssh\config`, `pulse-brief-prod` |
| GitHub 계정 | 저장소 push 권한 |
| AWS 계정 | Lightsail, billing |
| Cloudflare 계정 | DNS, Tunnel |
| 닷홈 계정 | 도메인 연장 |

운영 원칙:

- 위 값은 채팅, GitHub, 문서에 원문으로 남기지 않는다.
- 계정에는 MFA를 설정한다.
- 키를 잃어버렸거나 노출됐다고 판단하면 즉시 재발급한다.

## 5. 서버 상태 확인 명령

기본 접속:

```powershell
ssh pulse-brief-prod
```

서비스 상태:

```bash
systemctl status pulsebrief-web --no-pager
systemctl status pulsebrief-collector --no-pager
systemctl status mongod --no-pager
systemctl status cloudflared --no-pager
systemctl status pulsebrief-mongodb-backup.timer --no-pager
```

한 번에 active 여부만 확인:

```bash
systemctl is-active pulsebrief-web pulsebrief-collector mongod cloudflared pulsebrief-mongodb-backup.timer
```

공개 URL 확인:

```powershell
Invoke-WebRequest -Uri "https://news.pulse-brief.co.kr/api/health" -UseBasicParsing
```

서버 내부 확인:

```bash
curl -fsS http://127.0.0.1:8085/api/health
```

## 6. 로그 확인

웹 서버 로그:

```bash
journalctl -u pulsebrief-web -n 100 --no-pager
```

수집기 로그:

```bash
journalctl -u pulsebrief-collector -n 100 --no-pager
```

Cloudflare Tunnel 로그:

```bash
journalctl -u cloudflared -n 100 --no-pager
```

MongoDB 로그:

```bash
journalctl -u mongod -n 100 --no-pager
```

## 7. MongoDB 운영

운영 DB는 Lightsail 서버 안의 MongoDB다.

```text
mongodb://127.0.0.1:27017/pulsebrief
```

MongoDB는 외부에 직접 공개하지 않는다. 현재 `bindIp: 127.0.0.1`이고 방화벽은 SSH만 허용한다.

MongoDB Compass 접속:

```text
Connection Name: Pulse Brief Production
URI: mongodb://127.0.0.1:27017/pulsebrief
Authentication: None
SSH Tunnel: Use Identity File
SSH Hostname: 13.124.97.153
SSH Port: 22
SSH Username: ubuntu
SSH Identity Key File: C:\Users\User\.ssh\pulse-brief-lightsail.pem
SSH Passphrase: 비움
```

DB 문서 수 확인:

```bash
mongosh pulsebrief --quiet --eval 'JSON.stringify({articles: db.articles.countDocuments(), articleGroups: db.articleGroups.countDocuments(), summaries: db.summaries.countDocuments()})'
```

## 7.1 RSS 수집 기준

RSS는 운영 안정성을 위해 언론사별 대표 feed 1개를 우선 사용한다.

기준:

1. 전체뉴스 RSS가 있으면 전체뉴스 RSS만 사용한다.
2. 전체뉴스가 없으면 최신뉴스, 속보, 헤드라인 중 하나를 대표 RSS로 사용한다.
3. 전체뉴스와 섹션별 RSS를 동시에 대량 등록하지 않는다.
4. 특정 분야가 명확히 부족할 때만 예외적으로 섹션 RSS를 추가한다.
5. HTML로 응답하거나 리다이렉트 처리가 불안정한 RSS는 운영 목록에서 제외한다.

현재 운영 RSS 목록은 `config/rss-feeds.txt`에서 관리한다.

현재 기준:

| Source | RSS policy |
| --- | --- |
| Yonhap News | all news |
| Yonhap News TV | latest |
| Newsis | breaking |
| Hankyung | all news |
| Maeil Business Newspaper | headline |
| ETNews | today |
| Donga | all news |
| Hankyoreh | all RSS |
| Kyunghyang | all news |
| SBS | latest |
| JTBC | latest |
| Korea.kr | policy |
| BBC | world |

## 8. 백업

현재 서버에 MongoDB 일일 백업 timer가 설치되어 있다.

| 항목 | 값 |
| --- | --- |
| timer | `pulsebrief-mongodb-backup.timer` |
| service | `pulsebrief-mongodb-backup.service` |
| 백업 위치 | `/var/backups/pulsebrief/mongodb` |
| 보존 기간 | 약 7일 |

백업 상태 확인:

```bash
systemctl status pulsebrief-mongodb-backup.timer --no-pager
sudo find /var/backups/pulsebrief/mongodb -maxdepth 1 -mindepth 1 -type d -printf '%f\n' | sort | tail
sudo du -sh /var/backups/pulsebrief/mongodb
```

수동 백업 실행:

```bash
sudo systemctl start pulsebrief-mongodb-backup.service
```

추가 권장:

- AWS Lightsail Snapshot을 주 1회 또는 주요 배포 전 생성한다.
- 백업이 실제로 복원 가능한지 분기별로 테스트한다.

## 9. 배포 절차

로컬에서 소스 수정 후:

```powershell
git status --short
dotnet build
powershell -ExecutionPolicy Bypass -File .\tools\cloud\publish-cloud.ps1
```

서버에 업로드:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\cloud\deploy-to-ubuntu.ps1 `
  -HostName 13.124.97.153 `
  -UserName ubuntu `
  -KeyPath C:\Users\User\.ssh\pulse-brief-lightsail.pem `
  -SkipBootstrap
```

서버 서비스 재시작:

```bash
sudo systemctl restart pulsebrief-web
sudo systemctl restart pulsebrief-collector
curl -fsS http://127.0.0.1:8085/api/health
```

배포 후 공개 확인:

```powershell
Invoke-WebRequest -Uri "https://news.pulse-brief.co.kr/api/health" -UseBasicParsing
```

Git 정리:

```powershell
git status --short
git add docs tools src wwwroot *.csproj appsettings.json
git commit -m "..."
git push
```

주의:

- `diagnostic-report.md`는 프로젝트 무관 파일이므로 커밋하지 않는다.
- `.env`, `publish/`, `backups/`, SSH 키는 커밋하지 않는다.

## 10. 장애 대응

### 사이트가 접속되지 않음

1. 공개 health 확인

```powershell
Invoke-WebRequest -Uri "https://news.pulse-brief.co.kr/api/health" -UseBasicParsing
```

2. 서버 서비스 확인

```bash
systemctl is-active pulsebrief-web cloudflared mongod
```

3. 로그 확인

```bash
journalctl -u pulsebrief-web -n 100 --no-pager
journalctl -u cloudflared -n 100 --no-pager
```

### 뉴스 수집이 멈춤

1. 수집기 상태 확인

```bash
systemctl status pulsebrief-collector --no-pager
```

2. 로그 확인

```bash
journalctl -u pulsebrief-collector -n 200 --no-pager
```

3. 재시작

```bash
sudo systemctl restart pulsebrief-collector
```

### OpenAI 요약 실패

확인할 것:

- `/etc/pulsebrief/pulsebrief.env`에 `OPENAI_API_KEY`가 있는지
- OpenAI Billing 한도가 초과되지 않았는지
- 모델명이 유효한지
- 수집기 로그에 JSON parse 실패나 rate limit이 있는지

재시작:

```bash
sudo systemctl restart pulsebrief-collector
```

### 디스크 용량 부족

```bash
df -h
sudo du -sh /var/backups/pulsebrief/mongodb
sudo journalctl --vacuum-time=14d
```

백업이 과도하게 쌓였는지 확인한다.

## 11. 정기 점검표

### 매주

- `https://news.pulse-brief.co.kr` 접속 확인
- `/api/health` 확인
- 수집기 로그에서 반복 에러 확인
- MongoDB 백업 생성 여부 확인
- AWS Lightsail CPU/메모리/트래픽 확인

### 매월

- AWS Billing 확인
- OpenAI Billing 확인
- Cloudflare Tunnel 상태 확인
- 디스크 용량 확인
- GitHub에 최신 변경사항 push 여부 확인

### 분기별

- Ubuntu 패키지 업데이트
- Lightsail Snapshot 확인
- 도메인 만료일 확인
- 주요 키와 계정 MFA 확인
- 백업 복원 테스트

## 12. 업데이트 및 보안

Ubuntu 업데이트:

```bash
sudo apt-get update
sudo apt-get upgrade -y
sudo reboot
```

재부팅 후 확인:

```bash
systemctl is-active pulsebrief-web pulsebrief-collector mongod cloudflared
curl -fsS http://127.0.0.1:8085/api/health
```

보안 원칙:

- MongoDB `27017`을 인터넷에 열지 않는다.
- 서버 방화벽은 SSH만 허용한다.
- Cloudflare Tunnel을 통해서만 웹을 공개한다.
- SSH 키를 공유하지 않는다.
- OpenAI API Key를 Git에 넣지 않는다.

## 13. 현재 로컬 PC 상태

개인 PC는 더 이상 운영 서버가 아니다.

정리된 항목:

- Windows `cloudflared`: stopped / disabled
- 로컬 MongoDB: stopped / manual
- 로컬 MongoDB 데이터: 삭제됨
- 로컬 IIS `PulseBrief` 사이트/앱 풀: 제거됨
- 로컬 수집기 예약 작업: 제거됨
- 로컬 `8085` 리스너: 없음

로컬 PC의 역할:

- 소스 코드 수정
- Git commit / push
- 클라우드 서버 배포
- MongoDB Compass로 실서버 DB 확인

## 14. 공식 가격 확인 링크

가격은 변동 가능성이 있으므로 결제 전 공식 페이지 또는 각 콘솔에서 재확인한다.

- AWS Lightsail: https://aws.amazon.com/lightsail/pricing/
- Cloudflare Plans: https://www.cloudflare.com/plans/
- OpenAI API Pricing: https://platform.openai.com/docs/pricing
- 닷홈 도메인: https://www.dothome.co.kr/domain/
