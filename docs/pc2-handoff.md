# PC2 인수인계 시작점

이 묶음은 첫 번째 PC의 Codex 작업이나 대화를 전제로 하지 않는다. 두 번째 PC에서는 반드시 새 Codex 작업을 만들고, 압축을 푼 이 폴더의 절대 경로와 함께 아래 지시문을 전달한다.

> 이 PC는 Codex Telegram Bridge의 두 번째 개인용 PC다. 원래 PC의 작업에는 접근할 수 없으므로 그 작업을 열거나 이어가려 하지 마라. 이 폴더의 `START-HERE.md`, `rollout.json`, `docs/personal-two-pc-rollout.md`를 읽고, 묶음 무결성 검사부터 진행하라. Smart App Control, Defender, 방화벽, Secure Boot 또는 VBS를 끄거나 완화하지 마라. 관리자 권한/UAC, Codex 종료, Telegram 봇 토큰 입력처럼 실제 사용자 동작이 필요한 순간에만 나를 불러라. 차단 조건이 나오면 임의 우회하지 말고 코드와 진단 결과를 그대로 보고하라.

## PC2 Codex가 수행할 순서

모든 명령은 이 폴더를 현재 위치로 둔 PowerShell에서 실행한다. 관리자 권한이 아닌 셸에서는 정책 목록을 완전히 읽을 수 없으므로, 1단계부터 관리자 PowerShell이 필요하다. UAC는 사용자가 직접 승인한다.

1. 읽기 전용 준비 검사:

   ```powershell
   & .\scripts\Test-PersonalRolloutReadiness.ps1
   ```

2. 결과가 `mode=SAC_ENFORCED_READY`이면 정확한 해시 보조 정책만 설치한다. `mode=SAC_OFF_DIRECT_TEST`이면 이 단계는 건너뛴다. 그 밖의 결과에서는 중지한다.

   ```powershell
   & .\scripts\Install-PersonalSupplementalPolicy.ps1 -Apply
   ```

3. 최종판과 롤백판의 Bridge/Ctl 네 실행 파일을 부작용 없는 인수로 실행한다. 성공 기준은 `smoke=pass`이다.

   ```powershell
   & .\scripts\Test-PersonalCandidateExecution.ps1
   ```

4. 2단계 뒤 3단계가 실패하면 다른 보안 정책을 건드리지 말고 이 묶음의 보조 정책만 제거한다. 제거 결과가 `reboot_required=yes`이면 재부팅 후 다시 준비 검사를 수행한다.

   ```powershell
   & .\scripts\Remove-PersonalSupplementalPolicy.ps1 -Apply
   ```

5. 3단계가 통과하면 PC 별칭을 정해 설치 대기 도우미를 시작한다. 도우미의 별도 창이 열린 것을 확인한 뒤에만 사용자에게 Codex 데스크톱을 모두 닫으라고 요청한다. 도우미는 앱을 강제 종료하지 않으며, 닫힌 뒤 30분 유효 설치 계획을 적용한다.

   ```powershell
   & .\scripts\Start-PersonalBridgeInstall.ps1 -PcAlias $env:COMPUTERNAME
   ```

6. 별도 창에 설치 완료가 표시되면 Codex를 다시 열고 이 PC에서 만든 새 작업으로 돌아온다. 먼저 온라인 상태를 점검한다. 기존 설치의 업그레이드라면 Telegram 자격증명, capture 모드와 pause 상태가 보존되어야 하므로 `telegram bootstrap`을 다시 실행하지 않는다. 자격증명 누락이나 모드 변경이 보이면 새 설정으로 덮지 말고 중지해 보고한다. 신규 설치일 때만 첫 번째 PC의 DPAPI 파일이나 토큰 파일을 복사하지 않고, 사용자가 BotFather 토큰을 직접 입력해 동일 봇을 이 PC 사용자 컨텍스트에 별도로 설정한다.

   ```powershell
   $ctl = "$env:LOCALAPPDATA\CodexTelegramBridge\bin\CodexTelegramCtl.exe"
   & $ctl doctor --online
   # 신규 설치에서만 실행:
   & $ctl telegram bootstrap
   ```

7. 신규 설치에서는 Shadow 확인과 live 전환을 수행한다. 업그레이드에서는 보존된 과거 Shadow 행이 있으면 새 Ctl로 다시 검증할 수 있다. Shadow 제목이 12자소 미만이면 전체 제목을 입력하고, 12자소 이상이면 앞 12자소 이상의 prefix를 입력한다. 이어서 실제 완료 알림, 제목 변경 전후 알림, 재부팅 뒤 같은 작업의 변경 제목 유지, 잠금 화면과 Android Remote를 이 PC에서 독립적으로 검증한다. 문제가 생기면 `doctor --online` 결과와 `rollout.json`의 정책 ID만 첫 번째 PC 작업에 전달하고 토큰·DPAPI·DB·로그 원문은 전달하지 않는다.

## 강제 중지 조건

- 묶음 또는 정책 해시 불일치
- Smart App Control 평가 모드 또는 상태 불명
- 표준 SAC 외의 추가 강제 Base 정책
- SAC 강제 모드인데 `VerifiedAndReputableDesktop` Base 정책이 정확히 하나 활성 상태가 아님
- 표준 SAC 예제 정책에서 unsigned supplemental 허용을 확인할 수 없음
- 보조 정책의 ID, Base ID, 이름 또는 활성 상태 불일치
- 후보 실행 시 Code Integrity 차단

이 조건에서는 보안 설정 변경, `Unblock-File`, Defender 예외, 경로 허용 규칙, 다른 App Control 정책 제거를 시도하지 않는다.
