using System;
using UnityEngine;

namespace J80.Aerodynamics
{
    /// <summary>
    /// 세일 주변의 공기역학적 유동 상태 열거형
    /// </summary>
    public enum FlowState
    {
        Laminar,      // 정상 층류(부착 유동) 상태
        OptimalSlot,  // 메인세일-집세일 간 슬롯 효과(Venturi/Upwash) 최적 상태 (Cl 최대 15% 향상)
        Luffing,      // 받음각 부족 또는 역풍으로 세일이 펄럭이는 상태
        Stalled       // 임계 받음각(15~18도) 초과로 인한 경계층 박리 및 실속(난류 와류) 상태
    }

    /// <summary>
    /// J80AerodynamicsSolver가 산출하여 외부 시각화 모듈(Observer)로 브로드캐스트하는 읽기 전용 텔레메트리 구조체
    /// </summary>
    [Serializable]
    public struct AeroForceTelemetry
    {
        public Vector3 CenterOfEffortWorldPos;   // 풍압중심(CE) 3D 월드 좌표
        public Vector3 LiftVector;               // 겉보기 바람에 수직인 양력 벡터 (Newtons)
        public Vector3 DragVector;               // 겉보기 바람에 평행한 항력 벡터 (Newtons)
        public Vector3 TotalAeroForceVector;     // 총 공기역학 합력 벡터 (Newtons)
        public Vector3 DrivingForceVector;       // 선체 전진 방향(추진력) 분력 벡터 (Newtons)
        public Vector3 HeelingSideForceVector;   // 선체 횡방향(힐링/횡류력) 분력 벡터 (Newtons)
        public FlowState FlowState;              // 현재 유동 상태

        // 추가 진단용 스칼라 텔레메트리 (HUD 및 셰이더 정밀 제어용)
        public Vector3 ApparentWindVector;       // 겉보기 바람 속도 벡터 (m/s)
        public float ApparentWindAngleDeg;       // 겉보기 바람 각도 (AWA, Beta, deg)
        public float EffectiveAoADeg;            // 유효 받음각 (AoA, Alpha, deg)
        public float LiftCoefficient;            // 통합 유효 양력 계수 (Cl)
        public float DragCoefficient;            // 통합 유효 항력 계수 (Cd)
        public float SlotEffectMultiplier;       // 슬롯 효과 배수 (1.00 ~ 1.15)
        public float StallProgress01;            // 박리(Stall) 진행도 (0: 완전 부착, 1: 완전 박리)
    }

    /// <summary>
    /// 기존 J80_Vessel 코드를 수정하지 않고 데이터를 읽어오기 위한 비침투적(Non-intrusive) 읽기 전용 인터페이스
    /// </summary>
    public interface IJ80VesselTelemetrySource
    {
        Vector3 GetTrueWindVector();
        Vector3 GetBoatVelocityVector();
        float GetMainSailAngleDeg();
        float GetJibSailAngleDeg();
        Transform GetVesselTransform();
    }

    /// <summary>
    /// [Task 1] Keel boat 원디자인 킬보트 정밀 공기역학 연산 모듈
    /// 기존 선체/세일 컨트롤러를 단 한 줄도 수정하지 않고 읽기 전용으로 데이터를 수집하여
    /// 풍압중심(CE), 5대 역학 벡터(N), 슬롯 효과 및 박리(Stall) 상태를 산출하고 이벤트를 발행합니다.
    /// </summary>
    public class J80AerodynamicsSolver : MonoBehaviour
    {
        #region Keel boat Physical Constants (실제 제원 상수)

        /// <summary>Keel boat 메인세일 표준 면적 (m^2)</summary>
        public const float MAINSAIL_AREA_M2 = 19.6f;

        /// <summary>Keel boat 100% 집세일(Jib) 표준 면적 (m^2)</summary>
        public const float JIB_AREA_M2 = 14.3f;

        /// <summary>총 기본 세일 면적 (33.9 m^2)</summary>
        public const float TOTAL_SAIL_AREA_M2 = MAINSAIL_AREA_M2 + JIB_AREA_M2;

        /// <summary>표준 해수면 공기 밀도 (kg/m^3, 15°C, 1 atm)</summary>
        public const float AIR_DENSITY_KG_M3 = 1.225f;

        /// <summary>슬롯 효과 최대 양력 향상률 (+15%)</summary>
        public const float MAX_SLOT_EFFECT_BOOST = 0.15f;

        #endregion

        #region Inspector Settings (비침투적 참조 및 공기역학 파라미터)

        [Header("Non-Intrusive Read-Only Vessel Binding")]
        [Tooltip("선체 기준 Transform (J80_Vessel 루트). 코드를 수정하지 않고 Transform만 참조합니다.")]
        [SerializeField] private Transform vesselTransform;

        [Tooltip("선택 사항: 선체 Rigidbody (연결 시 속도를 자동으로 읽어옵니다).")]
        [SerializeField] private Rigidbody vesselRigidbody;

        [Tooltip("선택 사항: 메인 붐(Boom) Transform (연결 시 로컬 회전각에서 MainSailAngle을 자동 추출합니다).")]
        [SerializeField] private Transform mainBoomTransform;

        [Tooltip("선택 사항: 집세일(Jib) Transform (연결 시 로컬 회전각에서 JibSailAngle을 자동 추출합니다).")]
        [SerializeField] private Transform jibSailTransform;

        [Header("Keel boat Sail Plan Center of Effort (Local Space Offsets)")]
        [Tooltip("선체 원점 대비 메인세일 기하학적 풍압중심(CE) 로컬 좌표 (m)")]
        [SerializeField] private Vector3 mainSailLocalCE = new Vector3(0.0f, 4.85f, -0.65f);

        [Tooltip("선체 원점 대비 집세일 기하학적 풍압중심(CE) 로컬 좌표 (m)")]
        [SerializeField] private Vector3 jibSailLocalCE = new Vector3(0.0f, 3.60f, 1.95f);

        [Header("Aerodynamic Polar & Stall Curve Parameters")]
        [Tooltip("러핑(Luffing) 임계 받음각 (deg). 이 각도 미만에서는 세일이 펄럭이며 양력을 잃습니다.")]
        [SerializeField] private float luffingAoAThresholdDeg = 2.0f;

        [Tooltip("박리(Stall)가 시작되는 임계 받음각 (deg, 기본 15.5도)")]
        [SerializeField] private float stallOnsetAoADeg = 15.5f;

        [Tooltip("완전 실속(Full Stall)에 도달하는 받음각 (deg, 기본 18.0도)")]
        [SerializeField] private float fullStallAoADeg = 18.0f;

        [Tooltip("단일 세일 최대 기본 양력 계수 (슬롯 효과 적용 전)")]
        [SerializeField] private float maxBaseCl = 1.42f;

        [Tooltip("기생 항력 계수 (Cd0, 최소 프로파일 항력)")]
        [SerializeField] private float parasiticCd0 = 0.045f;

        [Tooltip("유효 종횡비 (Effective Aspect Ratio, 유도 항력 산출용)")]
        [SerializeField] private float effectiveAspectRatio = 4.6f;

        [Tooltip("오스왈드 효율 계수 (Oswald Efficiency Factor, e)")]
        [SerializeField] private float oswaldEfficiency = 0.82f;

        [Header("Slot Effect (Main-Jib Interaction) Parameters")]
        [Tooltip("슬롯 효과가 극대화되는 메인세일-집세일 간의 최적 트림 각도 편차 (deg)")]
        [SerializeField] private float optimalSlotAngleDiffDeg = 4.0f;

        [Tooltip("슬롯 효과 가우시안 커브 허용 폭(표준편차, deg)")]
        [SerializeField] private float slotAngleToleranceSigmaDeg = 3.5f;

        [Header("Fallback / External Telemetry Inputs (인터페이스 미연결 시 사용)")]
        [SerializeField] private Vector3 currentTrueWindVector = new Vector3(-6.0f, 0.0f, -8.0f);
        [SerializeField] private Vector3 currentBoatVelocityVector = new Vector3(0.0f, 0.0f, 3.5f);
        [SerializeField] private float currentMainSailAngleDeg = 12.0f;
        [SerializeField] private float currentJibSailAngleDeg = 16.0f;

        #endregion

        #region Observer Event & Public Properties

        /// <summary>
        /// 매 물리/렌더링 프레임마다 연산된 공기역학 텔레메트리를 구독자(AeroHUDVisualizer 등)에게 전달하는 이벤트
        /// </summary>
        public event Action<AeroForceTelemetry> OnTelemetryUpdated;

        /// <summary>가장 최근에 연산된 공기역학 텔레메트리 스냅샷</summary>
        public AeroForceTelemetry LatestTelemetry { get; private set; }

        private IJ80VesselTelemetrySource _externalSource;
        private Vector3 _previousVesselPos;
        private bool _hasPreviousPos;

        #endregion

        #region Initialization & Non-Intrusive Binding

        private void Awake()
        {
            if (vesselTransform == null)
            {
                vesselTransform = transform;
            }

            // 동일 오브젝트나 상위에 IJ80VesselTelemetrySource 구현체(어댑터)가 존재하면 자동 바인딩
            _externalSource = GetComponentInParent<IJ80VesselTelemetrySource>();
            _previousVesselPos = vesselTransform.position;
            _hasPreviousPos = true;
        }

        /// <summary>
        /// 외부 어댑터(Observer/Provider)를 런타임에 주입할 수 있는 메서드
        /// </summary>
        public void BindTelemetrySource(IJ80VesselTelemetrySource source)
        {
            _externalSource = source;
        }

        /// <summary>
        /// 외부 풍향/선속/세일각 데이터를 직접 주입하여 즉시 연산을 수행할 수도 있는 메서드
        /// </summary>
        public void SetInputs(
            Vector3 trueWindVector,
            Vector3 boatVelocityVector,
            float mainSailAngleDeg,
            float jibSailAngleDeg)
        {
            currentTrueWindVector = trueWindVector;
            currentBoatVelocityVector = boatVelocityVector;
            currentMainSailAngleDeg = mainSailAngleDeg;
            currentJibSailAngleDeg = jibSailAngleDeg;
        }

        private void Update()
        {
            ReadNonIntrusiveInputs();

            LatestTelemetry = SolveAerodynamics(
                currentTrueWindVector,
                currentBoatVelocityVector,
                currentMainSailAngleDeg,
                currentJibSailAngleDeg);

            OnTelemetryUpdated?.Invoke(LatestTelemetry);
        }

        /// <summary>
        /// 기존 J80_Vessel 스크립트를 건드리지 않고 인터페이스 또는 Transform/Rigidbody에서 상태를 읽어옵니다.
        /// </summary>
        private void ReadNonIntrusiveInputs()
        {
            if (_externalSource != null)
            {
                currentTrueWindVector = _externalSource.GetTrueWindVector();
                currentBoatVelocityVector = _externalSource.GetBoatVelocityVector();
                currentMainSailAngleDeg = _externalSource.GetMainSailAngleDeg();
                currentJibSailAngleDeg = _externalSource.GetJibSailAngleDeg();
                if (_externalSource.GetVesselTransform() != null)
                {
                    vesselTransform = _externalSource.GetVesselTransform();
                }
                return;
            }

            // Rigidbody나 Transform 변위로부터 비침투적으로 선속 추출
            if (vesselRigidbody != null)
            {
                currentBoatVelocityVector = vesselRigidbody.velocity;
            }
            else if (vesselTransform != null && _hasPreviousPos && Time.deltaTime > 1e-5f)
            {
                // Inspector에서 직접 테스트 중이 아닐 때만 Transform 변위 사용 가능
                Vector3 displacementVel = (vesselTransform.position - _previousVesselPos) / Time.deltaTime;
                if (displacementVel.sqrMagnitude > 0.0001f)
                {
                    currentBoatVelocityVector = displacementVel;
                }
                _previousVesselPos = vesselTransform.position;
            }

            // Boom / Jib Transform이 할당되어 있다면 로컬 Y축 회전각에서 트림 각도(절댓값 또는 부호 포함) 읽기
            if (mainBoomTransform != null)
            {
                currentMainSailAngleDeg = Mathf.Abs(Mathf.DeltaAngle(0f, mainBoomTransform.localEulerAngles.y));
            }

            if (jibSailTransform != null)
            {
                currentJibSailAngleDeg = Mathf.Abs(Mathf.DeltaAngle(0f, jibSailTransform.localEulerAngles.y));
            }
        }

        #endregion

        #region Core Aerodynamics Solver (정밀 공기역학 연산)

        /// <summary>
        /// 입력된 진풍(True Wind), 선속(Boat Velocity), 메인/집 세일 각도를 바탕으로
        /// Keel boat의 공기역학 힘 벡터들과 유동 상태(FlowState)를 정밀 연산합니다.
        /// </summary>
        /// <param name="trueWindVector">진풍 속도 벡터 (월드에 부는 바람의 진행 방향 벡터, m/s)</param>
        /// <param name="boatVelocityVector">선체 이동 속도 벡터 (m/s)</param>
        /// <param name="mainSailAngleDeg">메인세일(붐) 트림 각도 (deg, 중심선 대비 열림 각도)</param>
        /// <param name="jibSailAngleDeg">집세일 트림 각도 (deg, 중심선 대비 열림 각도)</param>
        public AeroForceTelemetry SolveAerodynamics(
            Vector3 trueWindVector,
            Vector3 boatVelocityVector,
            float mainSailAngleDeg,
            float jibSailAngleDeg)
        {
            Transform hull = vesselTransform != null ? vesselTransform : transform;
            Vector3 hullForward = hull.forward;
            Vector3 hullRight = hull.right;
            Vector3 hullUp = hull.up;

            // 1. 겉보기 바람(Apparent Wind Vector) 및 AWA(Beta) 산출
            // 공식: ApparentWindVector = TrueWindVector - BoatVelocityVector
            Vector3 apparentWindVector = trueWindVector - boatVelocityVector;

            // 수평면(또는 마스트 수직 평면) 투영을 통한 유효 공기 흐름 속도 및 각도 계산
            Vector3 awPlanar = Vector3.ProjectOnPlane(apparentWindVector, hullUp);
            float awSpeed = awPlanar.magnitude;

            // 만약 겉보기 바람이 거의 0이면 모든 힘이 0인 Luffing 상태 반환
            if (awSpeed < 0.05f)
            {
                return new AeroForceTelemetry
                {
                    CenterOfEffortWorldPos = hull.TransformPoint((mainSailLocalCE * MAINSAIL_AREA_M2 + jibSailLocalCE * JIB_AREA_M2) / TOTAL_SAIL_AREA_M2),
                    FlowState = FlowState.Luffing,
                    ApparentWindVector = apparentWindVector,
                    SlotEffectMultiplier = 1.0f
                };
            }

            Vector3 awFlowDir = awPlanar / awSpeed; // 바람이 흘러가는 방향 (Flow Direction)
            Vector3 awFromDir = -awFlowDir;         // 바람이 불어오는 방향 (Inflow Direction)

            // AWA (Beta): 선체 전방(hullForward)과 바람이 불어오는 방향(awFromDir) 사이의 각도 (-180 ~ +180 deg)
            // 양수(+): 스타보드 택(우현에서 불어오는 바람), 음수(-): 포트 택(좌현에서 불어오는 바람)
            float signedBetaDeg = Vector3.SignedAngle(hullForward, awFromDir, hullUp);
            float absBetaDeg = Mathf.Abs(signedBetaDeg);

            // 2. 각 세일의 받음각(Angle of Attack, Alpha) 산출
            float absMainAngle = Mathf.Abs(mainSailAngleDeg);
            float absJibAngle = Mathf.Abs(jibSailAngleDeg);

            // 유효 받음각 Alpha = |Beta(AWA)| - |SailTrimAngle|
            float mainAoADeg = absBetaDeg - absMainAngle;
            float jibAoADeg = absBetaDeg - absJibAngle;

            // 3. 받음각(AoA)에 따른 개별 세일의 Cl, Cd 커브 연산 (15~18도 Stall 구간 포함)
            EvaluateSailPolarCurve(mainAoADeg, out float clMain, out float cdMain, out float stallMain01);
            EvaluateSailPolarCurve(jibAoADeg, out float clJib, out float cdJib, out float stallJib01);

            // 4. 메인세일과 집세일 사이의 슬롯 효과(Slot Effect) 계수 연산
            // 두 세일이 모두 유효 양력 구간(러핑 아님, 완전 실속 아님)에 있고 간격(각도 편차)이 최적일 때 Cl 최대 15% 향상
            float slotMultiplier = CalculateSlotEffectMultiplier(mainAoADeg, jibAoADeg, absMainAngle, absJibAngle, stallMain01, stallJib01);

            // 슬롯 효과를 양력 계수에 반영
            clMain *= slotMultiplier;
            clJib *= slotMultiplier;

            // 5. 동압(Dynamic Pressure, q = 0.5 * rho * V^2) 및 세일별 양력/항력 크기(Newtons) 연산
            float dynamicPressure = 0.5f * AIR_DENSITY_KG_M3 * awSpeed * awSpeed;

            float mainLiftN = dynamicPressure * MAINSAIL_AREA_M2 * clMain;
            float mainDragN = dynamicPressure * MAINSAIL_AREA_M2 * cdMain;

            float jibLiftN = dynamicPressure * JIB_AREA_M2 * clJib;
            float jibDragN = dynamicPressure * JIB_AREA_M2 * cdJib;

            float totalLiftN = mainLiftN + jibLiftN;
            float totalDragN = mainDragN + jibDragN;

            // 6. 3D 벡터 방향 결정
            // DragVector: 겉보기 바람의 진행 방향(awFlowDir)과 평행
            Vector3 dragDir = awFlowDir;

            // LiftVector: 겉보기 바람에 수직이며 풍하측(Leeward) 전방으로 작용
            // 스타보드 택(signedBetaDeg >= 0, 우현에서 바람 유입) -> 풍하측은 좌현(Port)이므로 Cross(awFlowDir, hullUp) 방향
            // 포트 택(signedBetaDeg < 0, 좌현에서 바람 유입) -> 풍하측은 우현(Starboard)이므로 Cross(hullUp, awFlowDir) 방향
            float tackSign = signedBetaDeg >= 0f ? 1f : -1f;
            Vector3 liftDir = Vector3.Cross(awFlowDir, hullUp * tackSign).normalized;

            Vector3 liftVector = liftDir * totalLiftN;
            Vector3 dragVector = dragDir * totalDragN;
            Vector3 totalAeroForceVector = liftVector + dragVector;

            // 7. 선체 기준 전진 추진력(Driving Force) 및 횡방향 힐링력(Heeling/Side Force) 분해
            // 선체 전방 축(hullForward)에 대한 정사영 벡터
            Vector3 drivingForceVector = Vector3.Project(totalAeroForceVector, hullForward);

            // 선체 횡방향 축(hullRight)에 대한 정사영 벡터
            Vector3 heelingSideForceVector = Vector3.Project(totalAeroForceVector, hullRight);

            // 8. 동적 풍압중심(Center of Effort, CE) 3D 월드 좌표 산출
            // 메인세일과 집세일이 발생시키는 총 공기력의 비중에 따라 기하학적 CE를 가중 평균
            float mainTotalForceMag = Mathf.Sqrt(mainLiftN * mainLiftN + mainDragN * mainDragN);
            float jibTotalForceMag = Mathf.Sqrt(jibLiftN * jibLiftN + jibDragN * jibDragN);
            float forceSum = mainTotalForceMag + jibTotalForceMag;

            Vector3 weightedLocalCE;
            if (forceSum > 1e-3f)
            {
                weightedLocalCE = (mainSailLocalCE * mainTotalForceMag + jibSailLocalCE * jibTotalForceMag) / forceSum;
            }
            else
            {
                weightedLocalCE = (mainSailLocalCE * MAINSAIL_AREA_M2 + jibSailLocalCE * JIB_AREA_M2) / TOTAL_SAIL_AREA_M2;
            }

            Vector3 centerOfEffortWorldPos = hull.TransformPoint(weightedLocalCE);

            // 9. 면적 가중 평균 받음각 및 유동 상태(FlowState) 판정
            float weightedAoADeg = (mainAoADeg * MAINSAIL_AREA_M2 + jibAoADeg * JIB_AREA_M2) / TOTAL_SAIL_AREA_M2;
            float weightedStall01 = (stallMain01 * MAINSAIL_AREA_M2 + stallJib01 * JIB_AREA_M2) / TOTAL_SAIL_AREA_M2;
            float combinedCl = (clMain * MAINSAIL_AREA_M2 + clJib * JIB_AREA_M2) / TOTAL_SAIL_AREA_M2;
            float combinedCd = (cdMain * MAINSAIL_AREA_M2 + cdJib * JIB_AREA_M2) / TOTAL_SAIL_AREA_M2;

            FlowState flowState = DetermineFlowState(weightedAoADeg, weightedStall01, slotMultiplier);

            return new AeroForceTelemetry
            {
                CenterOfEffortWorldPos = centerOfEffortWorldPos,
                LiftVector = liftVector,
                DragVector = dragVector,
                TotalAeroForceVector = totalAeroForceVector,
                DrivingForceVector = drivingForceVector,
                HeelingSideForceVector = heelingSideForceVector,
                FlowState = flowState,
                ApparentWindVector = apparentWindVector,
                ApparentWindAngleDeg = signedBetaDeg,
                EffectiveAoADeg = weightedAoADeg,
                LiftCoefficient = combinedCl,
                DragCoefficient = combinedCd,
                SlotEffectMultiplier = slotMultiplier,
                StallProgress01 = weightedStall01
            };
        }

        /// <summary>
        /// 받음각(AoA, Alpha)에 따른 양력 계수(Cl) 및 항력 계수(Cd)를 박리(Stall, 15~18도) 구간이 포함된
        /// 연속 미분 가능 수학적 커브(Thin Airfoil + Viterna Post-Stall Blending)로 계산합니다.
        /// </summary>
        private void EvaluateSailPolarCurve(float aoaDeg, out float cl, out float cd, out float stallProgress01)
        {
            // 1) 러핑(Luffing) 또는 역받음각 구간 (AoA < luffingAoAThresholdDeg)
            if (aoaDeg <= 0.0f)
            {
                cl = 0.0f;
                // 세일이 역풍에 맞거나 완전히 풀려 펄럭일 때의 기본 마찰/플래핑 항력
                cd = parasiticCd0 + 0.03f * Mathf.Clamp01(-aoaDeg / 10.0f);
                stallProgress01 = 0.0f;
                return;
            }

            if (aoaDeg < luffingAoAThresholdDeg)
            {
                float luffRamp = Mathf.SmoothStep(0.0f, 1.0f, aoaDeg / luffingAoAThresholdDeg);
                float clAtThreshold = maxBaseCl * Mathf.Sin(Mathf.PI * 0.5f * (luffingAoAThresholdDeg / stallOnsetAoADeg));
                cl = clAtThreshold * luffRamp;
                cd = parasiticCd0 + (cl * cl) / (Mathf.PI * effectiveAspectRatio * oswaldEfficiency);
                stallProgress01 = 0.0f;
                return;
            }

            // 2) 부착 층류 유동 구간 (luffingAoAThresholdDeg <= AoA <= stallOnsetAoADeg, 약 2도 ~ 15.5도)
            if (aoaDeg <= stallOnsetAoADeg)
            {
                // 캠버(Camber)가 있는 소프트 세일 익형의 비선형 양력 상승 곡선
                float normalizedAlpha = aoaDeg / stallOnsetAoADeg;
                cl = maxBaseCl * Mathf.Sin(normalizedAlpha * Mathf.PI * 0.5f);

                // 유도 항력(Induced Drag) + 프로파일 기생 항력
                float inducedCd = (cl * cl) / (Mathf.PI * effectiveAspectRatio * oswaldEfficiency);
                cd = parasiticCd0 + inducedCd;
                stallProgress01 = 0.0f;
                return;
            }

            // 3) 박리 천이 및 실속 구간 (AoA > 15.5도: 15.5~18도에서 급격한 Cl 하락 및 Cd 상승)
            // stallProgress01: 15.5도에서 0.0 -> 18.0도 이상에서 1.0으로 급격히 전이
            float transitionSpan = Mathf.Max(0.5f, fullStallAoADeg - stallOnsetAoADeg);
            float rawStallT = Mathf.Clamp01((aoaDeg - stallOnsetAoADeg) / transitionSpan);
            stallProgress01 = rawStallT * rawStallT * (3.0f - 2.0f * rawStallT); // Hermite SmoothStep

            // 박리 직전 최대 양력 대비 박리 직후(18도) 급격히 붕괴된 양력 (약 45% 수준으로 급락)
            float postStallDropCl = maxBaseCl * 0.48f;

            // 18도 이후 90도까지 평판 유동(Flat-plate Crossflow Theory: Cl = A * sin(2*alpha), Cd = Cd_max * sin^2(alpha))
            float aoaRad = aoaDeg * Mathf.Deg2Rad;
            float flatPlateCl = 1.05f * Mathf.Sin(2.0f * aoaRad);

            if (aoaDeg <= fullStallAoADeg)
            {
                // 15.5도 ~ 18.0도: 경계층 박리로 인한 급격한 양력 붕괴 구간
                cl = Mathf.Lerp(maxBaseCl, postStallDropCl, stallProgress01);
            }
            else
            {
                // 18.0도 ~ 180도: 심실속(Deep Stall) 및 뒷바람(Running) 평판 유동 구간
                float deepStallT = Mathf.Clamp01((aoaDeg - fullStallAoADeg) / (90.0f - fullStallAoADeg));
                cl = Mathf.Max(0.0f, Mathf.Lerp(postStallDropCl, flatPlateCl, deepStallT));
            }

            // 실속 시 압력 항력(Pressure/Form Drag)의 급격한 폭증 반영
            float attachedInducedCd = (maxBaseCl * maxBaseCl) / (Mathf.PI * effectiveAspectRatio * oswaldEfficiency);
            float cdAtStallOnset = parasiticCd0 + attachedInducedCd;

            // 15.5도 -> 18도 구간에서 와류 박리로 인해 항력이 3배 이상 급상승한 뒤 sin^2(alpha) 커브를 따름
            float stallSeparationCdSurge = 0.42f * stallProgress01;
            float bluffBodyCd = 1.35f * Mathf.Pow(Mathf.Sin(Mathf.Min(aoaRad, Mathf.PI * 0.5f)), 2.0f);

            cd = Mathf.Lerp(cdAtStallOnset, Mathf.Max(cdAtStallOnset + stallSeparationCdSurge, bluffBodyCd), stallProgress01);
        }

        /// <summary>
        /// 메인세일과 집세일 사이의 슬롯 효과(Slot Effect) 계수를 연산합니다.
        /// 두 세일이 층류 부착 유동 상태이고 트림 각도 간격(집세일이 메인세일보다 약 3~5도 더 열림)이 최적일 때
        /// 벤투리 가속 및 업워시(Upwash) 상호작용으로 양력 계수(Cl)가 최대 15%(1.15배) 향상됩니다.
        /// </summary>
        private float CalculateSlotEffectMultiplier(
            float mainAoADeg,
            float jibAoADeg,
            float absMainAngleDeg,
            float absJibAngleDeg,
            float stallMain01,
            float stallJib01)
        {
            // 두 세일 중 하나라도 러핑 중이거나 완전 실속이면 슬롯 채널이 무너짐
            if (mainAoADeg < luffingAoAThresholdDeg || jibAoADeg < luffingAoAThresholdDeg)
            {
                return 1.0f;
            }

            // 두 세일 사이의 각도 간격 (일반적으로 집세일이 메인보다 약간 더 열려 있을 때 슬롯 노즐이 형성됨)
            float angleGapDeg = absJibAngleDeg - absMainAngleDeg;
            float diffFromOptimal = angleGapDeg - optimalSlotAngleDiffDeg;

            // 가우시안 종 모양 곡선으로 최적 간격 일치도(0~1) 산출
            float sigma = Mathf.Max(0.5f, slotAngleToleranceSigmaDeg);
            float gapOptimality = Mathf.Exp(-(diffFromOptimal * diffFromOptimal) / (2.0f * sigma * sigma));

            // 두 세일의 받음각이 최적 양력 구간(5도 ~ 15도)에 있을 때 유동 에너지가 최대화됨
            float mainAttachedFactor = 1.0f - stallMain01;
            float jibAttachedFactor = 1.0f - stallJib01;
            float aoaActivityFactor = Mathf.Clamp01((Mathf.Min(mainAoADeg, jibAoADeg) - luffingAoAThresholdDeg) / 4.0f);

            float effectiveSlotFactor = gapOptimality * mainAttachedFactor * jibAttachedFactor * aoaActivityFactor;

            // 최대 1.15배 (+15%) 반환
            return 1.0f + MAX_SLOT_EFFECT_BOOST * Mathf.Clamp01(effectiveSlotFactor);
        }

        /// <summary>
        /// 현재 받음각, 박리 진행도, 슬롯 효과 배수를 종합하여 FlowState를 판정합니다.
        /// </summary>
        private FlowState DetermineFlowState(float weightedAoADeg, float weightedStall01, float slotMultiplier)
        {
            if (weightedAoADeg < luffingAoAThresholdDeg)
            {
                return FlowState.Luffing;
            }

            if (weightedStall01 >= 0.25f || weightedAoADeg >= stallOnsetAoADeg)
            {
                return FlowState.Stalled;
            }

            // 슬롯 효과 향상분이 최대치(15%)의 70%(즉 약 +10.5% 이상 향상) 이상 발휘 중일 때 OptimalSlot 판정
            if (slotMultiplier >= 1.0f + (MAX_SLOT_EFFECT_BOOST * 0.70f))
            {
                return FlowState.OptimalSlot;
            }

            return FlowState.Laminar;
        }

        #endregion
    }
}
