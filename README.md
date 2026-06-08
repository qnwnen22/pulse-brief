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

## 데이터 저장소

MongoDB 컬렉션은 다음 구조를 사용합니다.

- `articles`: RSS 기사, 원문 URL, 본문 추출 결과, 본문 수집 상태
- `articleGroups`: 유사 기사 그룹과 카테고리/중요도 정보
- `summaries`: 전날/주간 AI 요약 결과

SQLite 저장소는 제거되었고, 기존 SQLite 데이터는 MongoDB로 마이그레이션 완료된 상태입니다.

## 서버 파이프라인

1. RSS 수집
2. 기사 저장
3. 기사 URL 접근 및 본문 추출
4. 로컬 임베딩 생성
5. 유사 기사 그룹화
6. 전날/주간 요약 생성

## API

- `GET /api/health`: 서버 상태
- `GET /api/articles`: 저장된 기사 목록
- `GET /api/groups`: 유사 기사 그룹 목록
- `GET /api/briefs`: 프론트엔드 이슈 피드 데이터
- `GET /api/daily-summary`: 전날 이슈 요약
- `GET /api/weekly-summary`: 주간 이슈 요약
- `POST /api/refresh`: RSS 수집부터 요약 갱신까지 파이프라인 실행
