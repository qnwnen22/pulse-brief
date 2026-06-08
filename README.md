# Pulse Brief

실시간 뉴스, 이슈, 트렌드를 수집하고 카테고리별로 정리하는 개인용 뉴스 브리핑 서버입니다.

## 실행

```powershell
dotnet run --urls http://localhost:4000
```

브라우저에서 `http://localhost:4000`을 엽니다.

## 구성

- ASP.NET Core 서버
- `wwwroot/` 정적 프론트엔드
- `config/rss-feeds.txt` RSS 목록
- `data/articles.json`, `data/groups.json` 로컬 저장소
- 10분 주기 자동 RSS 갱신 `BackgroundService`

## 서버 파이프라인

1. RSS 수집
2. 기사 저장
3. 로컬 해시 임베딩 생성
4. 유사 기사 그룹화
5. 대표 제목/요약 폴백 생성

## API

- `GET /api/health`: 서버 상태
- `GET /api/articles`: 저장된 기사 목록
- `GET /api/groups`: 유사 기사 그룹 목록
- `GET /api/briefs`: 프론트엔드용 이슈 브리핑
- `POST /api/refresh`: RSS 수집부터 요약까지 파이프라인 실행
