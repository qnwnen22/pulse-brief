# Pulse Brief

실시간 뉴스, 이슈, 트렌드를 RSS로 수집하고 카테고리별 이슈 피드로 정리하는 개인용 뉴스 브리핑 서버입니다.

## 실행

```powershell
dotnet run --urls http://localhost:4000
```

브라우저에서 `http://localhost:4000`으로 접속합니다.

## 구성

- ASP.NET Core 서버
- `wwwroot/` 정적 프론트엔드
- `config/rss-feeds.txt` RSS 목록
- `data/pulsebrief.db` SQLite 저장소
- 10분 주기 자동 RSS 갱신 `BackgroundService`

기존 `data/articles.json`, `data/groups.json` 데이터가 있고 SQLite DB가 비어 있으면 첫 실행 시 자동으로 `data/pulsebrief.db`로 이관합니다.

## 서버 파이프라인

1. RSS 수집
2. 기사 저장
3. 로컬 해시 기반 임베딩 생성
4. 유사 기사 그룹화
5. 대표 제목과 요약 생성

## API

- `GET /api/health`: 서버 상태
- `GET /api/articles`: 저장된 기사 목록
- `GET /api/groups`: 유사 기사 그룹 목록
- `GET /api/briefs`: 프론트엔드용 이슈 브리핑
- `POST /api/refresh`: RSS 수집부터 요약까지 파이프라인 실행
