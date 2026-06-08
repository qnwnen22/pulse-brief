# Pulse Brief

실시간 뉴스, 이슈, 트렌드를 빠르게 수집하고 요약하는 웹 대시보드입니다.

## 실행

```powershell
npm start
```

브라우저에서 `http://localhost:4000`을 엽니다.

## 서버 파이프라인

1. RSS 수집
2. 기사 저장
3. Embedding 생성
4. 유사 기사 그룹화
5. AI 대표 제목 생성
6. AI 요약 생성

`OPENAI_API_KEY`가 없으면 로컬 해시 임베딩과 규칙 기반 제목/요약으로 동작합니다.

## API

- `GET /api/health`: 서버 상태
- `GET /api/articles`: 저장된 기사 목록
- `GET /api/groups`: 유사 기사 그룹 목록
- `GET /api/briefs`: 프론트엔드용 이슈 브리핑
- `POST /api/refresh`: RSS 수집부터 요약까지 파이프라인 실행

