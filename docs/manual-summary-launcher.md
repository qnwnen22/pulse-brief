# 바탕화면 전날 뉴스 요약 도구

## 사용 방법

바탕화면의 **전날 뉴스 요약**을 더블클릭합니다. 실행 시점의 한국 시간을 기준으로 전날을 선택하며, 작업이 끝나면 검토용 HTML 문서를 엽니다. PC와 인터넷 연결은 실행 중에만 필요합니다.

이 도구는 **요약 초안 생성까지만** 수행합니다. 운영 DB에 요약을 쓰거나 사이트를 배포하지 않습니다. 결과를 확인한 뒤 Codex 채팅에서 대상 날짜의 초안을 배포해 달라고 요청합니다.

특정 날짜를 지정하거나 결과 창을 열지 않고 점검할 수도 있습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\start-daily-summary.ps1 -Date 2026-09-06
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\start-daily-summary.ps1 -CheckOnly -NoOpen
```

## 중복 방지

| 상황 | 처리 |
|---|---|
| 같은 Windows 로그인 세션에서 같은 날짜를 동시에 실행 | 이름 있는 Mutex로 두 번째 실행 차단 |
| `manual-summaries/YYYY-MM-DD.json`이 이미 존재 | 기존 파일 유지, Codex 호출 없이 검토 화면만 표시 |
| 실행 도구가 만든 해당 날짜 `draft.json`이 이미 존재 | 기존 초안 유지, 재생성 생략 |
| 운영 `summaries`에 해당 `Date` 문서가 존재 | 기사 조회와 Codex 호출 생략 |
| 서버 확인 실패, 인덱스 누락 | 요약이 없다고 추정하지 않고 중단 |
| 다른 작업이 기사 조회 중 또는 최종 저장 전에 서버 요약을 생성 | 존재 여부를 다시 확인하고 새 초안 저장 중단 |
| 이전에 완료된 Codex 처리 단계 | 입력과 정책의 해시가 일치하면 검증한 결과 재사용 |
| 이전 Codex 요청이 응답 없이 중단 | 중복 요청을 막기 위해 자동 재시도하지 않음. 로그 확인 필요 |

운영 DB에는 작업 예약이나 잠금 문서를 쓰지 않습니다. 따라서 서로 다른 PC에서 정확히 동시에 시작하는 경우까지 전역적인 단일 실행을 보장하지는 않습니다. 현재 바탕화면 도구가 설치된 PC 한 곳에서 사용합니다.

기존 파일을 무시하는 `force` 기능은 제공하지 않습니다. 파일이 손상되었거나 다시 만들 필요가 있으면 기존 결과와 실행 기록을 확인한 뒤 별도 처리합니다.

## 서버 조회 원칙

- SSH로 서버 내부 `mongosh`를 실행합니다. MongoDB 포트를 외부에 공개하지 않습니다.
- 먼저 날짜 인덱스로 요약 존재 여부를 최대 1건, 최대 3초 조건으로 확인합니다.
- 기사 발행 시각 `PublishedAt.DateTime`에 한국 시간 하루 범위를 적용합니다. 관리자 제외 기사는 읽지 않습니다.
- 실제 날짜 인덱스를 지정하고, 서버에서 별도 대량 정렬이나 그룹 집계를 실행하지 않습니다.
- 기사 ID, 제목, 언론사, 작성자, 발행·최초 수집 시각, URL, RSS 요약, 저장된 본문만 읽습니다. 임베딩은 가져오지 않습니다.
- 단일 커서로 200건씩 가져옵니다. DB 조회 시간 제한은 15초, 외부 프로세스 제한은 180초입니다.
- 하루 10,000건과 전송량 128MB를 기본 안전 상한으로 둡니다. 상한을 초과하면 일부 기사로 요약하지 않고 중단합니다. 기사 상한은 로컬 설정에서 최대 50,000건까지 조정할 수 있습니다.
- 완료 표식·기사 수·날짜를 검증하고 파일 해시를 저장합니다. 불완전한 자료로 요약을 시작하지 않습니다.
- DB 데이터, 인덱스, 수집기 설정을 수정하지 않습니다.

## 요약 작성 방식

1. PC에서 같은 출처·정규화 제목·작성자의 반복 기사를 정리합니다. 다른 언론사의 보도는 별도로 유지합니다.
2. 제목, RSS 요약 앞 350자, 저장된 본문 앞 1,200자를 Codex 입력으로 만듭니다. **본문 전체를 모두 읽는 방식은 아닙니다.** 원본 본문은 로컬 조회 파일에 보관합니다.
3. 최대 150기사·약 140,000자씩 순차 분류합니다. 모든 기사 키가 정확히 한 이슈에 포함되었는지 검사합니다.
4. 카테고리별로 같은 사건의 후보들을 다시 묶고 대표 이슈 1~3개를 선정합니다. 모든 후보 키의 누락·중복도 검사합니다.
5. 기사 수와 출처 수는 실제 근거 ID로 계산합니다. 중요도 점수와 문장은 Codex가 작성하므로 검토가 필요합니다.

기사 수에 따라 여러 차례 Codex를 호출하며 **Codex 계정 사용량을 소모**합니다. 서버의 OpenAI API 자동 요약을 재개하거나 API 키 인증을 사용하는 방식이 아닙니다. 실행에 필요한 로그인은 `codex login status`에서 ChatGPT 로그인인지 확인합니다.

Codex에는 기사 텍스트만 전달하며, CLI의 셸·웹 검색·플러그인·후크·하위 에이전트 기능을 비활성화합니다. 기사 내용 안의 지시는 신뢰하지 않도록 명시합니다. 출력 JSON을 별도로 검증한 뒤 저장합니다.

대상 날짜의 전체 이슈 수는 이번 로컬 분류·통합 결과이며, 서버의 기존 임베딩 그룹 개수와 같다는 의미는 아닙니다. 공개 화면의 관련 링크 조회 상한은 기존 서버 정책을 따릅니다.

## 생성 파일

| 위치 | 내용 |
|---|---|
| `data/summary-launcher/config.json` | 이 PC의 실행 파일·서버·SSH 키 경로 |
| `data/manual-summary-runs/YYYY-MM-DD/articles.jsonl` | 조회한 기사 원본 |
| `data/manual-summary-runs/YYYY-MM-DD/export.json` | 조회 완료·건수·해시 |
| `data/manual-summary-runs/YYYY-MM-DD/cache/` | 단계별 Codex 결과·실행 기록 |
| `data/manual-summary-runs/YYYY-MM-DD/draft.json` | 검토 후 import 가능한 요약 초안 |
| `data/manual-summary-runs/YYYY-MM-DD/review.html` | 사람이 읽는 요약·관련 기사 검토본 |
| `data/manual-summary-runs/YYYY-MM-DD/manifest.json` | 생성 완료 및 입력 기준 |

`data`는 Git 추적과 웹 빌드·배포 대상 모두에서 제외합니다. 실제 서버 IP나 개인 키를 소스에 포함하지 않습니다. 키 파일은 기존 `.ssh` 위치에서 사용하며 복사하지 않습니다.

## 설치와 검증

Windows, Node.js, OpenSSH, ChatGPT로 로그인한 Codex CLI가 필요합니다. 실행 파일 위치나 서버 IP가 변경되면 설치 도구를 다시 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\install-desktop-launcher.ps1 -HostName SERVER_IP -KeyPath C:\Users\YOUR_NAME\.ssh\pulse-brief-lightsail.pem
node --test tools/manual-summary/summary-tool.test.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\manual-summary\test-windows-launcher.ps1
```

SSH 호스트 키는 이미 신뢰된 키만 사용합니다. 인스턴스 교체로 키가 바뀌면 서버 신원을 확인한 뒤 SSH 설정을 갱신해야 합니다.

참고: [Codex 비대화형 실행 공식 문서](https://learn.chatgpt.com/docs/non-interactive-mode).
