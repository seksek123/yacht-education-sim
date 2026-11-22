using System;
using UnityEngine;

namespace J80.Biomechanics
{
    /// <summary>
    /// 기존 조타기(Tiller) 및 메인시트(Mainsheet) 컨트롤러를 수정하지 않고
    /// 읽기 전용으로 물리 하중과 조타각을 추출하기 위한 비간섭(Non-Intrusive) 인터페이스
    /// </summary>
    public interface IJ80ControlLoadSource
    {
        float GetMainsailLoadNewtons();
        float GetRudderResistingForceNewtons();
        float GetTillerAngleDeg();
    }

    /// <summary>
    /// [Task 1] 피교육생 생체역학 부하 및 피로도(Stamina) 실시간 연산 모듈
    /// 기존 Keel boat 요트 메쉬와 조작 스크립트를 단 한 줄도 수정하지 않고,
    /// 대상 객체의 Transform 및 물리 하중을 읽어 기계적 이점(5:1 태클)과 조타 토크(1.1m 익스텐션),
    /// 체력(Stamina) 소모율을 연산하여 이벤트를 발행합니다.
    /// </summary>
    public class BiomechanicalForceTracker : MonoBehaviour
    {
        #region Keel boat Geometry & Biomechanical Constants (기하학 및 생체역학 기본 상수)

        /// <summary>Keel boat 틸러 익스텐션(Tiller Extension) 기본 길이 (1.1 m)</summary>
        public const float DEFAULT_TILLER_EXTENSION_LENGTH = 1.1f;

        /// <summary>Keel boat 메인시트 태클(Mainsheet Tackle) 기본 기계적 감속비 (5:1)</summary>
        public const float DEFAULT_MAINSHEET_TACKLE_RATIO = 5.0f;

        /// <summary>피교육생 표준 체중 기본값 (78 kg, 이미지 레퍼런스 기준)</summary>
        public const float DEFAULT_TRAINEE_MASS_KG = 78.0f;

        #endregion

        #region Inspector Settings

        [Header("Non-Intrusive 3D Target Transforms (읽기 전용 참조)")]
        [Tooltip("조타기(Tiller) Transform. 기존 스크립트 수정 없이 로컬 회전각과 각속도만 읽어옵니다.")]
        [SerializeField] private Transform tillerTransform;

        [Tooltip("5:1 메인시트 태클 블록(Mainsheet Block) Transform. 3D AR 오버레이 기점으로 사용됩니다.")]
        [SerializeField] private Transform mainsheetBlockTransform;

        [Tooltip("선택 사항: Task 1의 J80AerodynamicsSolver 연결 시 세일 하중을 자동으로 읽어옵니다.")]
        [SerializeField] private J80.Aerodynamics.J80AerodynamicsSolver aeroSolver;

        [Header("Task 1: Mechanical Advantage & Input Variables")]
        [Tooltip("돛이 받는 총 하중 (MainsailLoad, 단위: N, 기본 예시 650 N)")]
        [SerializeField] private float MainsailLoad = 650.0f;

        [Tooltip("물 저항으로 인해 러더에 가해지는 힘 (RudderResistingForce, 단위: N, 기본 예시 16.82 N -> 18.5 N·m)")]
        [SerializeField] private float RudderResistingForce = 16.818f;

        [Tooltip("틸러 익스텐션 길이 (TillerExtensionLength, 기본값: 1.1 m)")]
        [SerializeField] private float TillerExtensionLength = DEFAULT_TILLER_EXTENSION_LENGTH;

        [Tooltip("메인시트 블록 감속비 (TackleRatio, 기본값: 5.0 [5:1])")]
        [SerializeField] private float TackleRatio = DEFAULT_MAINSHEET_TACKLE_RATIO;

        [Header("Trainee Biomechanics & Stamina Parameters")]
        [Tooltip("피교육생 체중 (kg)")]
        [SerializeField] private float traineeMassKg = DEFAULT_TRAINEE_MASS_KG;

        [Tooltip("피교육생 최대 가용 인간 당김 힘 (N). 체력 소모율 수식 (GripPullForce / 최대가용인간력) * dt 의 분모")]
        [SerializeField] private float maxAvailableHumanForceN = 450.0f;

        [Tooltip("피교육생 최대 가용 조타 토크 용량 (N·m). HUD의 HUMAN CAPACITY % 산출 기준")]
        [SerializeField] private float maxHumanTorqueCapacityNm = 44.0f;

        [Tooltip("초기 체력 (Stamina, 0.0 ~ 100.0 %, 이미지 레퍼런스 82%)")]
        [Range(0f, 100f)]
        [SerializeField] private float currentStaminaPercent = 82.0f;

        [Tooltip("초당 기본 체력 소모 스케일 계수 (백분율 환산용, 기본 1.0)")]
        [SerializeField] private float staminaDrainScale = 1.0f;

        [Tooltip("틸러 조타각 수동/초기 입력값 (Tiller Transform 미연결 시 사용, 기본 -6.2°)")]
        [SerializeField] private float fallbackTillerAngleDeg = -6.2f;

        #endregion

        #region Observer Event & Public Read-Only Telemetry

        /// <summary>
        /// [Task 1 필수 이벤트] 매 프레임 연산된 파지 당김 힘(gripForce, N), 조타 토크(torque, N·m), 남은 체력(stamina, %)을 발행합니다.
        /// 시그니처: OnForceMetricsCalculated(float gripForce, float torque, float stamina)
        /// </summary>
        public event Action<float, float, float> OnForceMetricsCalculated;

        /// <summary>현재 피교육생이 직접 당기는 힘 (GripPullForce = MainsailLoad / TackleRatio, N)</summary>
        public float GripPullForce { get; private set; }

        /// <summary>현재 조타에 필요한 토크 (TillerTorque = RudderResistingForce * TillerExtensionLength, N·m)</summary>
        public float TillerTorque { get; private set; }

        /// <summary>현재 남은 체력 백분율 (0 ~ 100 %)</summary>
        public float CurrentStamina => currentStaminaPercent;

        /// <summary>현재 프레임의 체력 소모율 ((GripPullForce / 최대가용인간력) * Time.deltaTime)</summary>
        public float CurrentStaminaConsumptionDelta { get; private set; }

        /// <summary>돛이 받는 총 하중 (MainsailLoad, N)</summary>
        public float CurrentMainsailLoad => MainsailLoad;

        /// <summary>메인시트 감속비 (TackleRatio, 기본 5.0)</summary>
        public float CurrentTackleRatio => TackleRatio;

        /// <summary>틸러 익스텐션 길이 (TillerExtensionLength, 기본 1.1m)</summary>
        public float CurrentTillerExtensionLength => TillerExtensionLength;

        /// <summary>현재 읽어들인 틸러 조타각 (deg, 예: -6.2°)</summary>
        public float CurrentTillerAngleDeg { get; private set; }

        /// <summary>인간 조타 토크 용량 대비 부하율 (0 ~ 100 %, 예: 42%)</summary>
        public float HumanTorqueCapacityPercent =>
            maxHumanTorqueCapacityNm > 1e-3f ? Mathf.Clamp((TillerTorque / maxHumanTorqueCapacityNm) * 100.0f, 0f, 100f) : 0f;

        /// <summary>상체 관절(어깨/팔꿈치) 정규화 부하 계수 (0.0: Green 안정 ~ 1.0: Yellow 고부하)</summary>
        public float NormalizedBiomechanicalLoad01 { get; private set; }

        /// <summary>REBA 인간공학 위험도 점수 (1 ~ 15 척도, 이미지 레퍼런스: 4 [Low Risk])</summary>
        public int RebaErgonomicScore { get; private set; } = 4;

        public Transform TillerTransform => tillerTransform;
        public Transform MainsheetBlockTransform => mainsheetBlockTransform;
        public float TraineeMassKg => traineeMassKg;

        private IJ80ControlLoadSource _externalLoadSource;

        #endregion

        #region Unity Lifecycle & Non-Intrusive Data Acquisition

        private void Awake()
        {
            _externalLoadSource = GetComponentInParent<IJ80ControlLoadSource>();
            if (aeroSolver == null)
            {
                aeroSolver = GetComponentInParent<J80.Aerodynamics.J80AerodynamicsSolver>();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // 1. 기존 컴포넌트를 수정하지 않고 Transform 및 물리력을 비간섭적으로 읽어옴
            ReadNonIntrusivePhysicalState();

            // 2. 기계적 이점(Mechanical Advantage) 및 조타 토크 연산
            CalculateBiomechanicalMetrics(dt);

            // 3. 구독자(ForceHUDOverlay 등)에게 이벤트 발행
            OnForceMetricsCalculated?.Invoke(GripPullForce, TillerTorque, currentStaminaPercent);
        }

        /// <summary>
        /// 기존 조타기(Tiller)와 시트(Sheet) 조작 컴포넌트를 수정하지 않고
        /// Transform 회전각 및 공기역학 솔버/외부 인터페이스에서 하중을 읽어옵니다.
        /// </summary>
        private void ReadNonIntrusivePhysicalState()
        {
            if (_externalLoadSource != null)
            {
                MainsailLoad = _externalLoadSource.GetMainsailLoadNewtons();
                RudderResistingForce = _externalLoadSource.GetRudderResistingForceNewtons();
                CurrentTillerAngleDeg = _externalLoadSource.GetTillerAngleDeg();
                return;
            }

            // J80AerodynamicsSolver가 연결되어 있고 유효한 공기력이 발생 중이면 메인세일 하중 연동
            if (aeroSolver != null && aeroSolver.LatestTelemetry.TotalAeroForceVector.sqrMagnitude > 1.0f)
            {
                // 전체 세일 면적 중 메인세일 비중(19.6 / 33.9)만큼의 시트 장력 추출
                float mainShare = J80.Aerodynamics.J80AerodynamicsSolver.MAINSAIL_AREA_M2 /
                                  J80.Aerodynamics.J80AerodynamicsSolver.TOTAL_SAIL_AREA_M2;
                MainsailLoad = aeroSolver.LatestTelemetry.TotalAeroForceVector.magnitude * mainShare;
            }

            // Tiller Transform이 연결되어 있으면 로컬 Y축 회전각에서 조타각(deg)을 비침투적으로 추출
            if (tillerTransform != null)
            {
                CurrentTillerAngleDeg = Mathf.DeltaAngle(0.0f, tillerTransform.localEulerAngles.y);

                // 조타각 크기에 따른 유체 저항력(RudderResistingForce) 동적 추정 (외부 소스가 없을 때)
                if (Mathf.Abs(CurrentTillerAngleDeg) > 0.05f && aeroSolver != null)
                {
                    float boatSpeedSq = Mathf.Max(4.0f, aeroSolver.LatestTelemetry.ApparentWindVector.sqrMagnitude * 0.25f);
                    RudderResistingForce = Mathf.Abs(CurrentTillerAngleDeg) * 0.45f * boatSpeedSq * 0.15f;
                }
            }
            else
            {
                CurrentTillerAngleDeg = fallbackTillerAngleDeg;
            }
        }

        /// <summary>
        /// [Task 1 핵심 수식 연산]
        /// - GripPullForce = MainsailLoad / TackleRatio
        /// - TillerTorque = RudderResistingForce * TillerExtensionLength
        /// - Stamina 소모율 = (GripPullForce / 최대가용인간력) * Time.deltaTime
        /// </summary>
        private void CalculateBiomechanicalMetrics(float dt)
        {
            float safeTackleRatio = Mathf.Max(0.1f, TackleRatio);
            float safeMaxHumanForce = Mathf.Max(1.0f, maxAvailableHumanForceN);

            // 1) 피교육생이 직접 당기는 힘 (GripPullForce) = MainsailLoad / TackleRatio
            GripPullForce = MainsailLoad / safeTackleRatio;

            // 2) 조타에 필요한 토크 (TillerTorque) = RudderResistingForce * TillerExtensionLength
            TillerTorque = Mathf.Abs(RudderResistingForce) * TillerExtensionLength;

            // 3) 현재 누적된 당김 및 조타 하중으로 인한 체력(Stamina) 소모율
            // 공식: (GripPullForce / 최대가용인간력) * Time.deltaTime
            CurrentStaminaConsumptionDelta = (GripPullForce / safeMaxHumanForce) * dt;

            // 체력(Stamina %) 차감 반영
            currentStaminaPercent = Mathf.Clamp(
                currentStaminaPercent - (CurrentStaminaConsumptionDelta * staminaDrainScale),
                0.0f,
                100.0f);

            // 4) 상체 관절 부하율 (0: Green ~ 1: Yellow) 및 REBA 점수 산출
            float pullLoadRatio = Mathf.Clamp01(GripPullForce / (safeMaxHumanForce * 0.65f));
            float torqueLoadRatio = Mathf.Clamp01(TillerTorque / Mathf.Max(1.0f, maxHumanTorqueCapacityNm));
            NormalizedBiomechanicalLoad01 = Mathf.Clamp01(pullLoadRatio * 0.6f + torqueLoadRatio * 0.4f);

            // REBA 인간공학 점수 (1~15, 기본 안정 자세 2점 + 부하 가산)
            RebaErgonomicScore = Mathf.Clamp(Mathf.RoundToInt(2.0f + NormalizedBiomechanicalLoad01 * 5.0f), 1, 15);
        }

        /// <summary>
        /// 외부 시뮬레이터나 테스트 코드에서 비간섭적으로 하중 입력값을 설정할 수 있는 헬퍼
        /// </summary>
        public void SetSimulationLoads(float mainsailLoadN, float rudderResistingForceN, float tillerAngleDeg)
        {
            MainsailLoad = mainsailLoadN;
            RudderResistingForce = rudderResistingForceN;
            fallbackTillerAngleDeg = tillerAngleDeg;
        }

        #endregion
    }
}
