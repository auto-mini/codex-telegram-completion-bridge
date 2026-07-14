# Two-PC personal rollout

## 목적과 경계

이 배포 경로는 한 사용자가 소유한 Windows PC 두 대에만 같은 Codex 완료 알림 브리지를 설치하기 위한 것이다. 공개 신뢰를 얻는 코드 서명이나 대량 배포를 대체하지 않는다. PC2는 SAC가 꺼진 상태에서 `personal.4`를 검증 완료했다. PC1은 활성 Smart App Control이 미서명 보조 정책을 승인하지 않아 처음에는 canary.9를 유지했다. 이후 소유자가 보안 차이를 검토하고 그 개인 PC에서 Smart App Control만 수동으로 끄기로 명시적으로 결정한 뒤, 같은 `personal.4`를 직접 실행 모드로 검증했다. 묶음은 SAC 상태를 바꾸거나 보조 정책을 설치하지 않는다. 전달되는 final/rollback 패키지는 검증된 두 EXE와 새 manifest만 포함하는 최소 payload이며, 과거 canary 문서와 로컬 기록은 포함하지 않는다.

PC1과 PC2의 Codex 작업·대화는 서로 로컬이며 같은 계정으로 로그인해도 이 인수인계 작업이 자동으로 보이지 않는다고 가정한다. 따라서 전달물은 실행 파일, 정책, 검증 스크립트와 독립 문서만 포함한다.

## 보안 모델

표준 Smart App Control 강제 정책의 Base Policy ID는 `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`이다. 저장소가 생성한 개인용 보조 정책은 이 Base를 대상으로 하며 다음 규칙만 가진다.

- 최종판 `CodexTelegramBridge.exe`와 `CodexTelegramCtl.exe`
- 검증된 롤백판 `CodexTelegramBridge.exe`와 `CodexTelegramCtl.exe`
- 파일별 Authenticode SHA-1, SHA-256, page SHA-1, page SHA-256 해시: 총 16 allow 규칙

Publisher, 파일 이름, 경로, wildcard, signer, deny 또는 allow-all 규칙은 허용하지 않는다. Supplemental 정책에 유효한 `Enabled:Unsigned System Integrity Policy` 외의 옵션도 제거한다. 그러나 규칙이 좁다는 사실은 inbox SAC Base가 그 미서명 보조 정책을 실제로 승인한다는 증거가 아니다.

PC1의 실제 진단에서는 정확한 정책 ID, Base ID, 이름, 버전과 옵션이 일치하고 `IsOnDisk=true`였지만 `IsAuthorized=false`, `IsEnforced=false`였다. 진단 정책은 즉시 완전히 제거했고 후보 EXE는 실행하지 않았다. 별도의 소형 Base 정책도 대안이 아니다. 여러 Base 정책은 합집합이 아니라 교집합으로 적용되므로 관련 없는 프로그램까지 차단할 수 있다.

정책 생성은 Microsoft의 ConfigCI cmdlet을 사용하고, 배포와 제거는 Windows inbox `CiTool.exe`를 사용한다. 정책 XML은 Windows Code Integrity XSD로 검증하고, CIP 파일명은 XML의 실제 Policy ID와 정확히 일치시킨다.

## 상태별 동작

- SAC 강제 모드: 정확한 보조 정책이 이미 `CiTool` 인벤토리에 하나만 존재하고 ID·Base ID·이름·버전·옵션이 일치하며 on-disk, authorized, enforced 상태가 모두 참일 때만 `SAC_ENFORCED_READY`이다. 묶음의 설치 스크립트는 새 정책을 추가하지 않고 이 상태만 재확인한다. 정책이 없거나 승인되지 않았으면 중지한다.
- SAC 꺼짐: 묶음은 SAC 상태를 바꾸지 않는다. 소유자가 Windows 보안 UI에서 별도로 선택한 상태와 보조 정책 부재를 확인한 뒤 직접 실행 점검한다.
- SAC 평가/불명: 향후 강제 전환 후 동작을 보장할 수 없으므로 중지한다.
- 추가 비시스템 Base 정책: 여러 Base 정책은 교집합으로 동작하므로 이 보조 정책만으로 실행을 보장할 수 없다. 중지하고 별도 진단한다.

## 최종 두 PC 결과

- PC2는 exact `personal.4`를 `SAC_OFF_DIRECT_TEST`로 검증했다. 온라인 진단, 재부팅, 잠금 화면, Android Remote, Telegram 전송과 변경된 12자소 제목 유지가 모두 통과했다.
- PC1에서는 활성 SAC가 exact 보조 정책을 unauthorized/unenforced로 판정했고, 해당 정책을 완전히 제거했다. 소유자가 이후 Smart App Control만 수동으로 끈 뒤 최종·제어·롤백 EXE 네 개의 직접 실행 점검, 트랜잭션 설치, 실행 파일 해시, 예약 작업, Telegram 온라인 연결과 실제 수신을 검증했다.
- PC1의 Defender 백신·실시간·행동·다운로드 검사와 세 방화벽 프로필은 활성 상태였고, VBS와 UAC도 실행 상태를 유지했다. 설치기는 이 설정들을 변경하지 않았다.
- PC1 온라인 진단의 `DEGRADED`는 새 설치 실패가 아니라 구버전에서 남은 7개 `THREAD_STATE_TIMEOUT` 격리 행과 5개 오래된 pending 행 때문이다. 새 전송은 성공했고, Telegram에는 PC 식별자와 12자소 제목 접두사가 정상 표시됐다. 이 과거 행들은 검토 없이 자동 삭제하거나 재전송하지 않았다.
- 이 결과는 소유자 개인 PC 두 대의 검증 증거일 뿐이다. SAC를 끄는 것을 공개 사용자에게 권장하거나 미서명 바이너리를 일반 배포할 수 있다는 근거가 아니며, 공개 배포에는 여전히 신뢰된 Authenticode 서명과 전체 재검증이 필요하다.

## 무결성과 비밀정보

묶음의 모든 파일은 `bundle.manifest.sha256`에 있고, 그 파일의 SHA-256은 `bundle.manifest.outer.sha256`에 기록한다. 스크립트는 누락 파일뿐 아니라 manifest에 없는 추가 파일과 reparse point도 거부한다. ZIP에는 다음을 포함하지 않는다.

- Telegram bot token 또는 chat ID
- DPAPI 암호문
- Codex DB, bridge DB, 로그와 작업 제목
- PC 이름, 사용자 프로필 경로 또는 설치 계획

PC2의 Telegram 자격증명은 PC2 사용자 컨텍스트에서 새로 입력하고 DPAPI로 보호한다. 같은 bot을 사용해도 토큰 파일은 PC 간 복사하지 않는다.

## 실패와 롤백

`Install-PersonalSupplementalPolicy.ps1`은 호환성을 위해 파일명을 유지하지만 이제 읽기·검증 전용이며 `CiTool --update-policy` 또는 제거를 호출하지 않는다. 과거 진단 등으로 정확한 프로젝트 정책이 이미 남아 있을 때만 제거 스크립트가 Policy ID, Base ID, friendly name과 비시스템 정책 여부를 다시 확인한 다음 그 정책만 제거한다. Windows 11 2024 Update 이전 버전에서는 unsigned 정책 제거 완료에 재부팅이 필요할 수 있다.

브리지 설치기는 Codex/ChatGPT를 강제 종료하지 않는다. 별도 설치 도우미가 최대 25분 동안 앱 종료를 기다린 뒤, 30분 유효 계획을 적용한다. 설치 트랜잭션 자체의 기존 보상 롤백도 그대로 유지한다.

## 근거

- [Use multiple App Control for Business Policies](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/deploy-multiple-appcontrol-policies)
- [Deploy App Control policies using script](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/deployment/deploy-appcontrol-policies-with-script)
- [CiTool technical reference](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/citool-commands)
- [Remove App Control policies](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/deployment/disable-appcontrol-policies)
- [App Control policy and file rules](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/select-types-of-rules-to-create)
