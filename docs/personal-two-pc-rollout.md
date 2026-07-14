# Two-PC personal rollout

## 목적과 경계

이 배포 경로는 한 사용자가 소유한 Windows PC 두 대에만 같은 Codex 완료 알림 브리지를 설치하기 위한 것이다. 공개 신뢰를 얻는 코드 서명이나 대량 배포를 대체하려는 경로가 아니다. SignPath 신청은 무료 백업 경로로 남기되 PC2 배포의 선행 조건으로 삼지 않는다. 전달되는 final/rollback 패키지는 검증된 두 EXE와 새 manifest만 포함하는 최소 payload이며, 과거 canary 문서와 로컬 기록은 포함하지 않는다.

PC1과 PC2의 Codex 작업·대화는 서로 로컬이며 같은 계정으로 로그인해도 이 인수인계 작업이 자동으로 보이지 않는다고 가정한다. 따라서 전달물은 실행 파일, 정책, 검증 스크립트와 독립 문서만 포함한다.

## 보안 모델

표준 Smart App Control 강제 정책의 Base Policy ID는 `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`이다. Windows에 포함된 정책은 unsigned policy와 supplemental policy를 허용한다. 개인용 보조 정책은 이 Base 하나만 확장하며 다음 규칙만 가진다.

- 최종판 `CodexTelegramBridge.exe`와 `CodexTelegramCtl.exe`
- 검증된 롤백판 `CodexTelegramBridge.exe`와 `CodexTelegramCtl.exe`
- 파일별 Authenticode SHA-1, SHA-256, page SHA-1, page SHA-256 해시: 총 16 allow 규칙

Publisher, 파일 이름, 경로, wildcard, signer, deny 또는 allow-all 규칙은 허용하지 않는다. Supplemental 정책에 유효한 `Enabled:Unsigned System Integrity Policy` 외의 옵션도 제거한다. 이 방식은 SAC Base 정책과 Defender를 그대로 유지하면서 정확히 네 PE 이미지에만 신뢰를 더한다.

정책 생성은 Microsoft의 ConfigCI cmdlet을 사용하고, 배포와 제거는 Windows inbox `CiTool.exe`를 사용한다. 정책 XML은 Windows Code Integrity XSD로 검증하고, CIP 파일명은 XML의 실제 Policy ID와 정확히 일치시킨다.

## 상태별 동작

- SAC 강제 모드: 표준 Base와 supplemental 허용을 확인하고 보조 정책을 설치한 뒤 실행 점검한다.
- SAC 꺼짐: SAC 상태를 바꾸지 않고 보조 정책 설치를 건너뛴 뒤 직접 실행 점검한다.
- SAC 평가/불명: 향후 강제 전환 후 동작을 보장할 수 없으므로 중지한다.
- 추가 비시스템 Base 정책: 여러 Base 정책은 교집합으로 동작하므로 이 보조 정책만으로 실행을 보장할 수 없다. 중지하고 별도 진단한다.

## 무결성과 비밀정보

묶음의 모든 파일은 `bundle.manifest.sha256`에 있고, 그 파일의 SHA-256은 `bundle.manifest.outer.sha256`에 기록한다. 스크립트는 누락 파일뿐 아니라 manifest에 없는 추가 파일과 reparse point도 거부한다. ZIP에는 다음을 포함하지 않는다.

- Telegram bot token 또는 chat ID
- DPAPI 암호문
- Codex DB, bridge DB, 로그와 작업 제목
- PC 이름, 사용자 프로필 경로 또는 설치 계획

PC2의 Telegram 자격증명은 PC2 사용자 컨텍스트에서 새로 입력하고 DPAPI로 보호한다. 같은 bot을 사용해도 토큰 파일은 PC 간 복사하지 않는다.

## 실패와 롤백

정책 설치 후 활성 확인이 실패하면 설치 스크립트는 이번 실행에서 추가한 정확한 Policy ID만 즉시 제거한다. 후보 실행이 실패하면 제거 스크립트가 Policy ID, Base ID, friendly name과 비시스템 정책 여부를 모두 다시 확인한 다음 그 보조 정책만 제거한다. Windows 11 2024 Update 이전 버전에서는 unsigned 정책 제거 완료에 재부팅이 필요할 수 있다.

브리지 설치기는 Codex/ChatGPT를 강제 종료하지 않는다. 별도 설치 도우미가 최대 25분 동안 앱 종료를 기다린 뒤, 30분 유효 계획을 적용한다. 설치 트랜잭션 자체의 기존 보상 롤백도 그대로 유지한다.

## 근거

- [Use multiple App Control for Business Policies](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/deploy-multiple-appcontrol-policies)
- [Deploy App Control policies using script](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/deployment/deploy-appcontrol-policies-with-script)
- [CiTool technical reference](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/citool-commands)
- [Remove App Control policies](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/deployment/disable-appcontrol-policies)
- [App Control policy and file rules](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/select-types-of-rules-to-create)
