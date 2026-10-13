# Capture

Windows 트레이 상주형 화면 캡쳐·녹화 도구 (WPF, .NET 8, x64). 멀티 모니터·혼합 DPI 환경을 지원한다.

## 기능

- **캡쳐** — PrintScreen, 트레이 아이콘 더블클릭 또는 트레이 메뉴 `Capture` 로 모든 모니터 위에 오버레이를 띄운다.
  - 모드: 영역 선택 / 마지막 영역 / 창 선택 / 색상 추출 — 오버레이에서 `Alt+Tab` 으로 순환, `Esc` 취소, 방향키로 커서 이동.
  - 결과는 클립보드에 복사되고 원래 위치에 떠 있는 창으로 표시된다. 창 우클릭 메뉴: 저장(`Ctrl+S`), 원래 크기, 투명도,
    채널 추출(RGB/YUV/HSV/HLS), 히스토그램, 펜 색. `Ctrl` 을 누른 채 드래그하면 그리기.
  - 색상 추출 모드는 `#RRGGBB` 값을 클립보드에 복사한다.
- **영역 녹화** — 트레이 메뉴 `Record region` 으로 영역을 고르면 테두리와 컨트롤 바가 뜬다. 바에서 시작·일시정지·중단,
  바를 드래그해 영역 이동(녹화 중 포함). 중단하면 MP4 저장 위치를 묻는다. (ScreenRecorderLib)
- **이미지 열기** — 트레이 메뉴 `Load from image file`.

## 빌드·배포

| 용도 | 명령 | 출력 |
|---|---|---|
| 개발 빌드 | `dotnet build Capture/Capture.csproj -c Release` | `build/Release/` |
| 포터블 단일 EXE | `dotnet publish Capture/Capture.csproj -p:PublishProfile=Portable` | `build/Portable/Capture.exe` |

- 출력 경로는 루트 `Directory.Build.props` 가 정한다(하위 모든 프로젝트에 적용).
- 포터블 EXE 는 실행 시 `%TEMP%\.net\Capture\<id>\` 에 풀려서 실행된다(C++/CLI 인 ScreenRecorderLib 때문에 필요).
  임시파일 정리에 지워지지 않도록 실행 중에는 그 파일들을 삭제 금지로 잠근다.
- CI(`.github/workflows/ci.yml`): master 대상 PR 에서 포터블 EXE 를 빌드하고, 머지되면 `vYYYY.MM.DD-<sha7>` 태그와
  GitHub Release(Capture.exe)를 만든다.

## 실행 시 파일

- 설정: `%LOCALAPPDATA%\BitWiz\Capture\appsettings.json`
- 크래시 로그: `%LOCALAPPDATA%\BitWiz\Capture\crash.log` (처리되지 않은 예외, 1MB 초과 시 새로 시작)
- 단일 인스턴스 — 이미 실행 중이면 새로 실행한 프로세스는 바로 종료된다.
