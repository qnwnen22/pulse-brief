# Pulse Brief Versioning Policy

Last reviewed: 2026-06-10

Pulse Brief는 배포 버전을 SemVer 기준으로 관리한다.

공개 서비스 버전은 `MAJOR.MINOR.PATCH` 형식을 사용한다. 예시는 `0.2.0`, `1.0.0`, `1.2.3`이다.

`.NET` 내부 `AssemblyVersion`과 `FileVersion`은 `MAJOR.MINOR.PATCH.0` 형식을 사용한다. 4번째 자리는 외부 서비스 버전으로 올리지 않고, 파일/어셈블리 식별용으로만 유지한다.

## Version Increment Rules

### MAJOR

호환성이 깨지는 변경이 있을 때 올린다.

예시:

- 기존 API 응답 구조를 호환되지 않게 변경
- MongoDB 스키마를 기존 데이터 마이그레이션 없이는 사용할 수 없게 변경
- 운영 배포 구조를 수동 조치 없이는 기존 방식과 함께 쓸 수 없게 변경
- 관리자 사용 방식이나 인증 방식을 크게 변경

### MINOR

기존 사용성을 깨지 않는 기능 추가나 운영 구조 개선이 있을 때 올린다.

예시:

- 새 관리자 기능 추가
- 뉴스 검색/필터 기능 개선
- RSS 소스 관리 기능 확장
- 운영 서버 구조 개선
- 백업, 모니터링, 진단 기능 추가

### PATCH

기존 기능의 버그 수정, 문구 수정, 작은 운영 보정이 있을 때 올린다.

예시:

- RSS URL 또는 안내 페이지 링크 수정
- 배포 스크립트 오류 수정
- UI 표시 오류 수정
- 수집/요약 실패 처리 보정
- 보안 헤더나 설정의 호환 가능한 보정

## Release Procedure

배포 작업에는 다음 순서를 적용한다.

1. 변경 내용 기준으로 다음 SemVer 버전을 결정한다.
2. `tools/release-version.ps1`로 `VERSION`, `Directory.Build.props`, `CHANGELOG.md`를 갱신한다.
3. 빌드와 필요한 검증을 실행한다.
4. 한글 커밋 메시지에 변경 내용을 상세히 적어 커밋한다.
5. 운영 서버에 배포한다.
6. `/api/health`와 사이트 푸터에서 배포 버전이 맞는지 확인한다.
7. 커밋을 GitHub에 push한다.

기본 명령 예시:

```powershell
.\tools\release-version.ps1 -Version 0.2.0 -Notes "운영 서버 이전 반영","RSS 검색 필터 정리"
dotnet build
git add VERSION Directory.Build.props CHANGELOG.md
git commit -m "버전 0.2.0 릴리즈"
git push
```

Git 태그는 릴리즈 이력이 필요할 때만 사용한다.

```powershell
.\tools\release-version.ps1 -Version 0.2.0 -Notes "운영 서버 이전 반영" -Tag
git push origin v0.2.0
```

## Current Project Rule

- 배포 가능한 사용자-facing 변경은 반드시 버전을 올린다.
- 기능 추가나 운영 구조 변경은 기본적으로 `MINOR` 후보로 본다.
- 단순 오류 수정과 링크/문구/스크립트 보정은 기본적으로 `PATCH` 후보로 본다.
- 배포 없이 문서만 수정하는 변경은 서비스 버전을 올리지 않는다.
- 현재 외부 표시 버전은 `VERSION` 파일을 기준으로 한다.
- 운영 배포 후 `/api/health`의 `version` 값과 사이트 푸터 버전을 확인한다.
