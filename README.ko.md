# WiiFitToVRC

[日本語](README.md) | [English](README.en.md) | **한국어** | [简体中文](README.zh-Hans.md) | [繁體中文](README.zh-Hant.md)

Wii 밸런스 보드를 VRChat(또는 다른 Windows 애플리케이션)용 보행 컨트롤러로 바꿔주는 앱입니다. 보드 위에서 체중을 이동하기만 하면 전진·후진·회전·점프·웅크리기 동작을 키보드/마우스 입력, 가상 Xbox 360 컨트롤러 입력, 또는 VRChat 자체 OSC 입력으로 변환합니다.

## 간단한 설치(비전문가를 위한 안내)

프로그래밍 지식은 전혀 필요하지 않습니다. 아래 절차만으로 작동합니다.

1. 이 저장소 최상단의 `WiiFitToVRC.exe`를 클릭해 다운로드합니다(설치 과정이 필요 없습니다).
2. 다운로드한 파일을 더블클릭해서 실행합니다.
3. Wii 밸런스 보드의 배터리 케이스 안에 있는 **SYNC** 버튼을 누른 다음, 앱의 **接続(연결)** 버튼을 클릭합니다.
4. 화면 안내(**キャリブレーション(보정)** → 보드에서 내려와 대기 → 다시 보드에 올라가 대기)를 따르기만 하면 준비가 끝납니다. 이제 VRChat을 실행하고 보드 위에서 체중을 이동하면 걸을 수 있습니다.

더 자세한 절차는 아래 "빠른 시작"을, 잘 작동하지 않을 때는 [docs](docs/) 폴더(영어)의 상세 설명을 참고하세요.

## 특징

- **PIN 입력 없이 Bluetooth 페어링** — 원리는 [docs/BALANCE_BOARD.md](docs/BALANCE_BOARD.md)(영어)를 참고하세요.
- **2단계 보정**: 센서의 영점을 맞추는 1회성 보정(보드에서 내려와 실시)과, 백그라운드에서 계속 자동으로 갱신되는 "기준 체중"(다른 사람이 올라가도 바로 추종합니다).
- **전진·후진·대시·좌우 회전·점프·웅크리기 동작 감지** — 각 판정 로직과 조정 가능한 설정 항목은 [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md)(영어)를 참고하세요.
- **4가지 출력 모드**:
  - 키보드(회전은 Q/E 키)
  - 키보드+마우스(회전은 마우스 시점 이동 — 기본값)
  - 가상 Xbox 360 컨트롤러 — SendInput으로 만든 합성 키보드/마우스 입력을 거부하는 게임(VRChat 포함)용. 자세한 내용은 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)(영어)를 참고하세요.
  - VRChat OSC 기능 사용 — VR 기기에 입력이 잠겨 가상 컨트롤러를 포함한 모든 합성 입력을 받아들이지 않는 환경용. 자세한 내용은 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)(영어)를 참고하세요.
- 키 바인딩/컨트롤러 할당, 회전 감도, 하중 임계값, 각종 타이밍 등을 앱 내 설정 화면에서 세밀하게 조정 가능.
- 다국어 UI: Windows 표시 언어를 자동 감지. 일본어·영어·중국어 간체/번체·한국어·프랑스어·독일어·이탈리아어를 지원합니다.

## VRChat 외의 게임에서도 사용 가능합니다

이 앱의 출력은 일반적인 키보드 WASD(또는 마우스) 입력이므로, 공식적으로 지원하지 않더라도 WASD 이동을 지원하는 게임이라면 걷기 위주의 다른 게임에서도 동작합니다. 사용해 본 예:

- Death Stranding
- Resident Evil
- Monster Hunter
- Armored Core IV

## 동작 환경

- Windows 10/11
- Wii 밸런스 보드(Bluetooth) — 단종된 제품이지만 중고 시장에서 저렴하게 구할 수 있습니다
- HID 장치를 지원하는 Bluetooth 어댑터
- 가상 컨트롤러 출력 모드를 사용할 경우: [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)(실제 커널 드라이버입니다 — 이 앱이 자동으로 설치할 수 없으므로 직접 다운로드하여 설치해야 합니다)

## 빠른 시작

1. 이 저장소 루트에 있는 `WiiFitToVRC.exe`를 다운로드하여 실행합니다(자체 완결형 빌드이므로 .NET 런타임 설치가 필요 없습니다).
2. 밸런스 보드의 배터리 케이스 안에 있는 **SYNC** 버튼을 누른 다음, 앱의 **接続(연결)** 버튼을 클릭합니다.
3. 연결 후 **キャリブレーション(보정)**을 클릭하고, 10초간 보드에서 내려와 센서 보정을 진행합니다.
4. 다시 보드에 올라가 잠시 평소처럼 서 있으세요. 동작 감지가 시작되기 전에, 가만히 서 있는 상태가 일정 시간 이어져야 기준 체중을 학습합니다(그동안 상태 표시줄에 "체중 보정 중"이 표시됩니다).
5. **設定(설정)** 화면을 열어 출력 모드 선택이나 키 바인딩·감도 조정을 진행하세요.

## 소스에서 빌드하기

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

```
dotnet build WiiFitToVRC.sln
```

저장소 루트에 배치되는 자체 완결형 단일 exe를 생성하려면:

```
powershell -File publish.ps1
```

## 프로젝트 구조

```
WiiFitToVRC.exe          빌드된 자체 완결형 실행 파일(publish.ps1로 생성)
publish.ps1               WiiFitToVRC.exe를 다시 빌드하고 재배치하는 스크립트
src/
  WiiFitToVRC.Core/        도메인 로직: Bluetooth 페어링, HID 통신, 동작 감지,
                           설정, 다국어화, 출력(키보드/마우스/컨트롤러/OSC)
  WiiFitToVRC.App/         WinForms UI(모니터 화면·설정 대화상자)
tools/
  PairTool/                밸런스 보드 페어링을 단독으로 테스트하는 콘솔 도구
  ClassifyTest/             오프라인 재생 도구: 기록된 CSV 로그에 대해 판정 로직을
                           다시 실행하여, 실기기 없이 임계값을 조정하기 위한 도구
reference/
  WiiBalanceWalker_v0.4/    InTheHand.Net.Personal.dll(32feet.NET). Bluetooth 관리에 사용
                           — 저작권 표기는 동봉된 README.txt를 참고하세요
docs/                      (현재는 영어만 지원)
  BALANCE_BOARD.md          밸런스 보드의 Bluetooth/HID 프로토콜 상세
  GESTURE_DETECTION.md      각 동작의 판정 방법과 이를 조정하는 설정 항목
  VRCHAT_INPUT.md           일반 SendInput이 VRChat에서 작동하지 않는 이유와 3가지 해결책
```

## 설정 참조

모든 설정은 앱 내 설정 화면(⚙ 설정)에서 편집할 수 있으며, exe와 같은 폴더의 `settings.json`에 저장됩니다. 직접 편집할 필요는 없지만, 개요는 다음과 같습니다:

| 설정 항목 | 내용 |
|---|---|
| 출력 방식 | 키보드 / 키보드+마우스 / 가상 컨트롤러 / VRChat OSC(자세히는 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)) |
| 언어 | UI 표시 언어. 자동으로 Windows 설정을 따르는 것도 가능 |
| 회전 감도 | 마우스 이동량(키보드+마우스 모드) 또는 스틱 편향 %(컨트롤러 모드). 좌우 개별 설정 가능 |
| 반응 하중 임계값 | "보드에 사람이 올라와 있다"고 판정하는 보정 후 합계 하중 |
| 잠자기·복귀까지 초 | 출력이 잠금/해제되기까지 필요한 지속 시간(양방향 공통) |
| 발걸음 감지 임계값(%) | 학습된 기준 체중 대비, 코너가 얼마나 초과하면 발걸음으로 판정할지 — 자세히는 [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md) |
| 대시 판정(ms) | 발걸음 사이 간격이 이보다 짧으면 대시로 판정 |
| 보폭(ms) | 발걸음을 감지한 후, 다음 발걸음이 없는 채로 Idle로 돌아갈 때까지의 지속 시간 |
| 웅크리기/점프 사용 여부 | 각 동작을 개별적으로 끌 수 있습니다(키 출력·표시등 모두 완전히 비활성화) |
| 디버그 모드 | `ClassifyTest`용 로그를 기록하는 원시 데이터 기록 컨트롤을 표시 |
| 키 바인딩 탭 | 키보드 출력 모드에서 각 동작의 키(대시 보조 키 포함) |
| 컨트롤러 탭 | 가상 컨트롤러 모드에서 각 동작의 버튼과 스틱 편향량 |

## 라이선스

이 프로젝트 자체 코드는 [MIT](LICENSE)입니다. 동봉된 `InTheHand.Net.Personal.dll`은 서드파티 라이브러리(32feet.NET)입니다 — 저작권 표기는 [reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt](reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt)를 참고하세요.
