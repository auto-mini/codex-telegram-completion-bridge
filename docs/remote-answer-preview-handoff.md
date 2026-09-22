# 연결 PC에 답변 앞 50자 알림 이식

이 문서는 기존 Codex Telegram Bridge 설치를 유지하면서 원본 PC에서 검증한 동일 실행 파일로 업그레이드하는 절차다. 모델이나 reasoning 설정은 변경하지 않는다. 완료 알림은 PC 표시와 작업 제목 앞 12자를 유지하고 최종 답변 앞 50자를 추가한다. 50자를 넘으면 `…`를 붙인다.

## 고정 전달물

- GitHub 저장소: `auto-mini/codex-telegram-completion-bridge`
- 브랜치: `codex/answer-preview-50` (실행 시 전달 메시지에 지정된 커밋 SHA로 고정할 것)
- 초안 릴리스 태그: `v1.0.3-personal.1`
- ZIP: `CodexTelegramBridge-1.0.3-personal.1-win-x64.zip`
- ZIP SHA-256: `c597c6e7c5f974b1b518f658bbdb339741bfd0522df98adb95bbc1d171eedaca`
- 압축 해제한 `manifest.sha256`의 SHA-256: `687379a3041caafb23ca713b395f87d15b8b122337d1d13c413885a65a733a95`
- 설치 EXE 파일 버전: `1.0.3.0`. 버전만으로 50자판인지 판정하지 말고 반드시 패키지와 EXE 해시를 비교한다.

이 초안은 사용자 소유 PC 사이의 전달용이다. 공개 릴리스로 게시하거나 latest로 설정하지 않는다. 일반 공개 배포의 CI/서명 자격 심사를 대체하지 않는다. 실행 파일은 미서명이다.

**필수 보완:** ZIP의 실행 파일만 설치하면 긴 대화에서 Windows 명령줄 제한 때문에 알림이 누락될 수 있다. 최신 고정 커밋의 `Invoke-CodexStopHook.ps1`과 아래 표준입력 훅 설정까지 함께 적용해야 한다. 원본 PC에서는 기존에 실패하던 같은 작업에서 재시작 후 자동 전송, 앞 50자 일치, 재시도 0회를 확인했다. 현재 전달물은 하트비트 필터, ACL 파일 정리 경합 재검사, 별도 npm CLI를 구분하는 설치 대기 수정을 포함한 1.0.3 패키지다. 이전 1.0.1-personal.2 파일을 설치하지 말 것. `DONT_NOTIFY`는 전송하지 않고, `NOTIFY`는 실제 message의 앞 50자만 보낸다.

## 1. 실제 대상 PC 사전 점검

1. Windows x64, 현재 사용자, `%LOCALAPPDATA%\CodexTelegramBridge` 설치 유무를 확인한다.
2. 기존 `bin\CodexTelegramCtl.exe doctor --online --json`을 실행한다. 기존 모드, pause, PC alias, 자격증명 유무, 알림 hook, 예약 작업, package manifest, 기존 오류 코드를 보호된 로컬 기록으로 남긴다. 채팅/커밋/GitHub에는 토큰, chat ID, PC 이름, 사용자 경로, 전체 DB/로그/대화를 올리지 않는다.
3. 이 PC의 실제 Smart App Control/App Control 상태를 확인한다. 원본 PC의 SAC-off 결과를 그대로 적용하지 않는다. 보안 기능을 끄거나, 예외/허용 정책을 새로 추가하거나, 파일 차단을 우회하지 않는다. 미서명 실행이 차단되면 설치 전에 중지하고 코드와 원인을 보고한다.
4. GitHub 초안 릴리스는 인증된 저장소 소유자/협업자 접근이 필요하다. `gh` 인증을 확인하되 토큰을 출력하거나 원본 PC의 인증을 복사하지 않는다.
5. 기존 설치와 인증이 없으면 새 봇 설정을 임의로 하지 말고 보고한다. 기존 설치라면 `telegram bootstrap`, `enable-live`, `pause`, `resume`를 임의로 실행하지 않는다. 다른 작업이나 미커밋 파일을 덮어쓰지 않는 별도 다운로드/checkout 폴더를 사용한다.

## 2. 원본 그대로 다운로드하고 검증

현재 작업에서 충돌 없는 별도 checkout에 저장소를 clone한 뒤 전달 메시지의 정확한 커밋으로 checkout한다. 기존 사용자 checkout을 강제로 reset하지 않는다. `.codex-remote-attachments` 같은 로컬 첨부 자료는 필요 없다.

```powershell
gh release download v1.0.3-personal.1 --repo auto-mini/codex-telegram-completion-bridge --pattern 'CodexTelegramBridge-1.0.3-personal.1-win-x64.zip*' --pattern 'manifest.outer.sha256' --dir $downloadDir
```

1. ZIP SHA-256을 위의 고정값과 비교하고 일치할 때만 새 `$packageDir`로 압축 해제한다. ZIP 안의 최상위에는 `bin`, `scripts`, `manifest.sha256` 등이 직접 존재한다.
2. `manifest.sha256` 자체 해시를 고정값과 비교한다. 그 파일의 각 항목과 상대 경로를 확인하고 실제 파일 해시를 모두 비교한다. manifest에 없는 파일, 누락 파일, 경로 이탈, reparse point가 있으면 중지한다. ZIP에는 15개 파일(14개 manifest 항목 + manifest 자체)이 있다.
3. 보안 조건이 허용할 때만 candidate Bridge를 인수 없이 실행해 종료 코드 2, Ctl을 `help`로 실행해 종료 코드 0인지 확인한다. Bridge는 WinExe이므로 PowerShell의 직접 호출 후 `$LASTEXITCODE`를 믿지 말고 `Start-Process -WindowStyle Hidden -Wait -PassThru`의 `ExitCode`를 사용한다.
4. 다운로드한 바이너리를 다시 빌드하거나 수정하지 않는다. 이식에는 SDK/새 NuGet 설치가 필요 없다.

## 3. 표준입력 Stop 훅 먼저 설정

이 PC에 실제 설치된 PowerShell **7.2 이상**과 Codex CLI를 사용한다. 다음 스크립트는 기존의 다른 훅을 보존하며, 이전 Codex 설정을 현재 Windows 사용자 DPAPI로 백업하고 이 프로젝트의 Stop 훅 하나만 추가·신뢰한다. 전체 훅 신뢰 우회나 보안 설정 변경은 하지 않는다. 기존 설치 EXE를 교체하지 않는다.

```powershell
& .\scripts\answer-preview-upgrade\Configure-CodexStopHook.ps1 `
    -CodexPath (Get-Command codex -ErrorAction Stop).Source `
    -PowerShellPath (Get-Command pwsh -ErrorAction Stop).Source
```

성공 기준은 `stdin_stop_hook=configured_and_trusted` 또는 `already_configured_and_trusted`이다. `config/value/write`의 버전 검사를 유지해야 한다. **훅 설정 후에** 다음 단계의 새 설치 계획을 만든다. 반대 순서로 하면 Codex 설정 해시가 달라져 기존 계획이 무효가 된다. 코드/설정 오류를 임의로 우회하지 말고 고정 오류를 보고한다.

`config/stdin-stop-hook.json`은 해당 PC의 보호된 로컬 설치 기록이다. 복사하거나 GitHub에 올리지 않는다. 이 파일이 있는 경우 설치 후에도 해당 훅을 `hooks/list`로 확인한다. 원본 PC의 절대 경로나 trust hash를 이 PC에 복사하지 않는다. 명령에 쓰이는 절대 경로와 정확한 신뢰 해시는 이 PC에서 새로 계산한다.

## 4. 검증된 예약 작업으로 설치 대기

정확한 커밋의 저장소 루트에서 실행한다.

```powershell
& .\scripts\answer-preview-upgrade\Start-AnswerPreviewUpgrade.ps1 `
    -PackageRoot $packageDir `
    -ExpectedManifestSha256 '687379a3041caafb23ca713b395f87d15b8b122337d1d13c413885a65a733a95'
```

이 스크립트는 기존 설치의 정상 여부를 확인하고, 보호된 `backups` 아래에 설치 계획/상태 기록을 만들고, 현재 사용자의 Interactive/Limited 예약 작업을 등록해 즉시 시작한다. 계획 유효기간은 30분, 앱 종료 대기는 25분이다. 직접 `Start-Process`로만 도우미를 띄우지 않는다. 원본 PC에서 그 방식의 도우미가 앱 재시작 때 설치 전에 종료됐고 Windows 예약 작업 방식으로 정상 완료했다.

출력의 `upgrade_stage`와 `upgrade_task`를 해당 PC의 보호된 로컬 기록에 보관한다. 다음을 실제 확인한 뒤에만 사용자에게 앱 종료가 필요하다고 안내한다.

- 예약 작업 상태 `Running`.
- `verification.json`의 `status=waiting_for_desktop_close`, `launcher=windows_task_scheduler`.
- 실행 프로세스 소유자 SID가 현재 사용자 SID와 동일함. 작업 Principal.UserId는 이름으로 정규화될 수 있으므로 문자열과 SID를 바로 비교하지 말고 이름을 SID로 변환한다.
- 도우미 프로세스가 Codex 자식이 아니라 Windows 작업 스케줄러에서 실행됨.
- `installer.log`에 앱 종료 대기 문구가 기록됨.

사용자에게 "25분 안에 이 PC의 Codex와 ChatGPT를 완전히 종료하고 약 1분 후 다시 실행"하도록 안내한다. 설치기가 실행 중인 앱을 허용하지 않는 이유는 설정과 실행 파일을 안전하게 교체하기 위해서다. 다른 실행 중인 작업을 임의로 끊거나 앱을 강제 종료하지 않는다. 이 상태는 **설치 대기**, 설치 완료가 아니다. 재부팅부터 하라고 요구하지 않는다.

## 5. 재실행 후 반드시 실제 확인

1. 보호된 `upgrade_stage`의 `install-result.json`: `status=complete`, `exitCode=0`.
2. `verification.json`: `status=installed_and_verified`, 자격증명/설정 보존, Telegram online OK.
3. 설치된 Bridge/Ctl 양쪽 EXE SHA-256이 다운로드 패키지의 파일과 각각 동일함.
4. 설치된 Ctl로 `doctor --online --json`: manifest/hook/예약 작업/자격증명/Telegram 연결 정상, 기존 capture/pause/alias 유지.
5. 임시 `UpgradeAnswerPreview50-*` 작업이 제거됨. 기존 Drain/Repair는 유지됨.
6. 검증 기록의 고정 코드와 설치 전 상태를 비교한다. 기존 `EVENT_QUARANTINED`만 남은 경우 임의로 삭제·acknowledge·재전송하지 않는다. 새로운 차단 오류나 설정 변경은 완료로 보고하지 않는다.
7. 이 PC의 실제 작업 완료에서 50자 답변 줄이 포함된 전송을 확인한다. API 접수, 로컬 sent 기록, 사용자의 휴대폰 표시 확인을 구분한다. 미확인 사항을 성공으로 쓰지 않는다.
8. Stop 훅이 활성/신뢰 상태이며, 재시작한 Codex에서 자동 호출됐는지 확인한다. 수동으로 알림 payload를 넣어 보내는 테스트만으로 완료를 판정하지 않는다. 원본에서 확인한 것처럼 이미 열린 작업은 옛 훅을 캐시할 수 있으므로 앱 재시작 후 새 완료 턴의 큐와 전송 결과가 필요하다.

실패하면 고정 오류 코드/실패 단계만 보고한다. 다른 PC의 토큰/DPAPI/DB/로그를 복사하지 않는다. 실패를 이유로 보안 설정이나 새 봇 설정을 바꾸지 않는다. 원본 설치기는 보상 롤백을 제공하며 기존 상태·백업을 임의로 삭제하지 않는다.

완료 보고: 실제 설치 버전과 패키지 해시 일치 여부, 기존 설정 보존 여부, Telegram 연결/실제 50자 알림 확인 여부, 남은 차단 또는 사용자 동작을 간결하게 구분한다.
