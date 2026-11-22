# ⛵ 서울수상레포츠센터 요트교육 시뮬레이터 (`yacht-education-sim`)

> **Keel boat (J80) & ILCA 7 딩기요트 통합 공기역학·인체공학 교육 시뮬레이터 (Ver 12.2)**  
> 2D 교육용 각도계와 3D 요트 뷰(Three.js)를 오가며 바람(진풍/겉보기바람), 세일 트림, 텔테일 유동, 2단계 힘 벡터 분해, 선체 힐링(Heel) 및 선원 하이킹 복원력을 실시간으로 학습할 수 있는 인터랙티브 요트 교육 프로그램입니다.  
> 단일 Windows 실행파일(`.exe`), 오프라인 자립형 PC 웹서버 HTML, 모바일 반응형 HTML 3종 배포 패키지를 원클릭 자동 빌드합니다.

---

## 🌟 주요 핵심 기능

### 1. 듀얼 선종(Boat Mode) 물리 시뮬레이션
- **Keel boat (J80 킬보트 모드)**:
  - 4~5인승, 메인세일 + 집세일 슬루프(Sloop) 리그.
  - 650kg 납 벌브 킬(Bulb Keel) 및 520kg 선원 체중 기반의 안정적 복원력 모델.
  - 메인세일-집세일 간 **슬롯 효과(Slot Effect / Venturi)** 양력 증폭 반영.
- **ILCA 7 (레이저 딩기 모드)**:
  - 1인승 단독, 단일 메인세일 캣 리그(Cat Rig), 무 밸러스트 대거보드(Daggerboard).
  - 80kg 선원 체중의 **하이킹(Hiking) 위치에 민감하게 반응하는 롤링/복원력 물리** 구현.

### 2. 2D 교육용 각도계 ↔ 3D 요트 뷰 전환 (`V` 키)
- **2D 공기역학 스테이지**: 풍향·선수각·세일각, 텔테일(Telltale) 리본 유동 상태, 유선(Streamline) 및 힘 벡터 화살표를 직관적으로 표시.
- **3D 요트 뷰 (`Three.js`)**: 실시간 수면 렌더링과 선체 힐링·세일 캠버를 입체적으로 관찰.
- **전체화면(`F11` / `ESC`) 및 모바일 3-탭 UI**: 강의실 대형 스크린 투사와 스마트폰·태블릿(`조종 패널` / `계기 · 그래프` / `화면 크게`) 환경 모두 지원.

### 3. 인터랙티브 범주 조종 및 교육 프리셋
- **바람 방향 & 풍속 제어**: 풍향·풍속 변화에 따른 겉보기바람(Apparent Wind) 및 힘의 변화 즉시 확인.
- **범주 코스 원클릭 프리셋**:
  - 클로즈홀드(우현/좌현 ±45°), 빔리치(±90°), 브로드리치(±135°), 러닝(180°), 인아이언스(In Irons, 0°).
- **선회(Manoeuvre) 실습 도구**:
  - 풍상 통과 대칭 **태킹(Tack)** / 풍하 통과 대칭 **자이빙(Gybe)**.
  - 5° 단위 각도 지정 **러핑(Luffing)** 및 **베어링어웨이(Bearing away)**.
- **세일 트림 & 선원 하이킹 보조**:
  - 메인세일·집세일 개별 트림 및 **최적 세일 트림(Auto Trim)** 자동 비교.
  - 선체 기울기에 따른 **최적 하이킹(Auto Hiking)** 자동 균형 조절.

### 4. 실시간 운항 텔레메트리 & 2단계 벡터 분석
- **2단계 벡터 분해 & 킬 수역학 평형**: 총 공기역학 힘의 양력/항력 → **전진 추력(`F_drive`)** 및 **횡력(`F_side`)** 분해와 킬/대거보드 측저항 평형 시각화.
- **선미 단면 롤링 & 복원력 뷰**: 선체 힐링각, 킬 복원 모멘트, 선원 하이킹 모멘트를 실시간 단면도로 표시.
- **실시간 시계열 그래프**: 전진 추력(`F_drive`) vs 횡력(`F_side`) 변화 추이를 연속 그래프로 비교.

### 5. Unity 3D 확장용 C# 물리·HUD 모듈 포함 (`J80_AeroHUD/*.cs`)
- `J80AerodynamicsSolver.cs`: 층류(Laminar), 최적 슬롯(OptimalSlot), 러핑(Luffing), 실속(Stall) 유동 상태 해석기.
- `AeroHUDVisualizer.cs`: 풍압중심(CE) 기점 5대 힘 벡터 화살표 및 세일 유선 파티클 3D HUD 렌더러.
- `BiomechanicalForceTracker.cs` / `ForceHUDOverlay.cs`: 5:1 메인시트 감속 하중, 틸러 조타각·토크, 관절 부하 시각화 오버레이.

---

## 📁 프로젝트 디렉토리 구조

```text
yacht-education-sim/
├── J80_AeroHUD/                  # 메인 시뮬레이터 소스 코드
│   ├── index.html                # 통합 2D/3D 시뮬레이터 웹 애플리케이션 (핵심 소스)
│   ├── three.min.js              # 오프라인 번들용 Three.js 라이브러리
│   ├── water_bg.*                # 수면 배경 리소스
│   ├── J80AerodynamicsSolver.cs  # Unity C# 공기역학 유동 해석 모듈
│   ├── AeroHUDVisualizer.cs      # Unity C# 3D 벡터/유선 HUD 렌더러
│   ├── BiomechanicalForceTracker.cs # Unity C# 인체공학 하중 추적 모듈
│   └── ForceHUDOverlay.cs        # Unity C# AR 생체역학 HUD 오버레이
├── build_exe/                    # 자동 빌드 & 패키징 파이프라인
│   ├── build_release.js          # 버전 관리·정적 검증·HTML/EXE/ZIP 자동 빌드 스크립트
│   ├── Program.cs                # C# 인메모리 HTTP 서버 + 앱 모드 실행 래퍼 (.exe)
│   ├── version.json              # 자동 버전업(+0.1) 상태 파일
│   └── app_icon.ico              # Windows 실행파일 및 파비콘 아이콘
├── legacy/                       # 초기 v1.0 배포본 및 초기 프로토타입 아카이브
└── releases/                     # 빌드 산출물 출력 폴더 (.gitignore 제외)
```

---

## 🛠️ 로컬 실행 및 자동 릴리스 빌드 가이드

### 1. 개발 중 브라우저에서 바로 열기
별도의 패키지 설치(`npm install`) 없이, `J80_AeroHUD/index.html`을 웹 브라우저(Chrome / Edge)로 열거나 로컬 정적 서버로 바로 확인할 수 있습니다.

```powershell
# 예: Python 또는 VS Code Live Server 등으로 정적 서빙
cd D:\My_Apps\yacht-education-sim\J80_AeroHUD
python -m http.server 8080
```
접속 주소: [http://localhost:8080](http://localhost:8080)

---

### 2. 원클릭 릴리스 자동 빌드 (`build_release.js`)

**사전 요구사항**: Windows 환경, Node.js, .NET Framework 4.0 (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` — Windows 기본 내장)

```powershell
cd D:\My_Apps\yacht-education-sim
node build_exe/build_release.js
```

#### 빌드 옵션
| 명령어 | 동작 설명 |
| :--- | :--- |
| `node build_exe/build_release.js` | `version.json` 기준 **+0.1 자동 버전업** 후 전체 빌드 (예: `v12.2` → `v12.3`) |
| `node build_exe/build_release.js --no-bump` | 현재 버전을 유지한 채 재빌드 |
| `node build_exe/build_release.js --set 13.0` | 특정 버전 번호(`v13.0`)를 직접 지정하여 빌드 |

#### 자동 빌드 파이프라인 6단계
1. **버전 동기화**: `J80_AeroHUD/index.html`(`<title>`, `#appVersionBadge`)과 `build_exe/Program.cs`(어셈블리 메타데이터)의 버전을 자동 동기화합니다.
2. **정적 무결성 검증**: HTML DOM ID 참조 누락 여부, 주요 Canvas 렌더링 함수 7종의 `ctx.save()`/`ctx.restore()` 균형, JavaScript 구문 오류(`vm.Script`)를 빌드 전에 자동 검증합니다.
3. **자립형 웹/모바일 HTML 생성**: `three.min.js`와 Base64 인코딩된 `app_icon.ico`를 단일 HTML에 완전 내장하고 로컬 EXE용 하트비트 코드를 제거합니다.
4. **Windows 단일 `.exe` 컴파일**: `csc.exe`로 HTML·Three.js·아이콘을 리소스로 임베드한 단독 실행파일을 생성합니다. 실행 시 빈 로컬 포트에서 인메모리 HTTP 서버를 띄우고 전용 앱 모드(App Mode) 창으로 실행되며, 창을 닫으면 하트비트 감지를 통해 프로세스가 깔끔하게 자동 종료됩니다.
5. **유니코드 안전 `.zip` 패키징**: .NET `ZipFile`을 통해 한글 파일명 깨짐 없이 배포용 압축 파일을 생성합니다.
6. **버전 기록 갱신**: 모든 단계가 성공한 경우에만 `build_exe/version.json`을 갱신합니다.

#### 생성되는 배포 산출물 (`releases/` 폴더)
- `서울수상레포츠센터_요트교육_vX.Y.exe` — 설치 없이 더블클릭으로 실행하는 Windows 단일 실행파일
- `서울수상레포츠센터_요트교육_vX.Y_단일실행파일.zip` — PC 배포용 EXE 압축 패키지
- `서울수상레포츠센터_요트교육_vX.Y_PC웹서버용.html` / `.zip` — 인터넷 없이도 동작하는 웹서버·오프라인용 단일 HTML
- `서울수상레포츠센터_요트교육_vX.Y_모바일용.html` — 스마트폰·태블릿 교육용 단일 HTML

---

## 🖥️ 교육 현장 활용 가이드

1. **강의실 PC / 전자칠판 (Windows)**:
   - `서울수상레포츠센터_요트교육_vX.Y.exe` 단일 파일만 복사해 실행하면 별도 브라우저 주소창 없이 전용 앱 모드로 즉시 구동됩니다.
   - `V` 키로 2D 각도계와 3D 요트 뷰를 빠르게 전환하고, `F11` 키로 전체화면 모드를 활용해 수강생 가독성을 높일 수 있습니다.
2. **웹 호스팅 / 교육생 스마트폰 공유**:
   - `*_PC웹서버용.html` 또는 `*_모바일용.html` 파일 하나만 웹서버(또는 GitHub Pages, 사내망 등)에 올리면 외부 라이브러리 의존성 없이 즉시 동작합니다.

---

## 📄 저작권 및 제작 정보

- **기관**: 서울수상레포츠센터
- **제작자**: 박주호 (Park Jooho)
- **Copyright**: © 2024-2026 박주호 (Park Jooho). All Rights Reserved.
