using UnityEngine;

namespace J80.Aerodynamics
{
    /// <summary>
    /// [Task 2] 3D 공기역학 벡터 화살표 및 유선(Streamline) HUD 렌더러
    /// J80AerodynamicsSolver의 OnTelemetryUpdated(AeroForceTelemetry data) 이벤트만 구독(Observer Pattern)하여
    /// 풍압중심(CE) 기점 5대 힘 벡터 화살표와 세일 유선 파티클/셰이더(층류 시안색 vs 실속 시 세일 뒷면 붉은 와류 효과)를 렌더링합니다.
    /// </summary>
    public class AeroHUDVisualizer : MonoBehaviour
    {
        #region Shader Property IDs (GC-Free 셰이더 파라미터 제어용 해시)

        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
        private static readonly int StreamColorPropId = Shader.PropertyToID("_StreamColor");
        private static readonly int VortexIntensityPropId = Shader.PropertyToID("_VortexIntensity");
        private static readonly int VortexFrequencyPropId = Shader.PropertyToID("_VortexFrequency");
        private static readonly int VortexCenterWorldPropId = Shader.PropertyToID("_VortexCenterWorld");
        private static readonly int StallProgressPropId = Shader.PropertyToID("_StallProgress");
        private static readonly int SlotBoostPropId = Shader.PropertyToID("_SlotBoostFactor");

        #endregion

        #region Inspector Settings

        [Header("Observer Subscription Target")]
        [Tooltip("구독할 J80AerodynamicsSolver 인스턴스 (미할당 시 씬에서 자동 탐색)")]
        [SerializeField] private J80AerodynamicsSolver aeroSolver;

        [Header("3D Vector Arrow Scaling & Smoothing")]
        [Tooltip("뉴턴(N) 단위의 힘을 3D 화살표 길이(m)로 변환하는 스케일 팩터 (예: 0.0025 = 1000N당 2.5m)")]
        [SerializeField] private float newtonsToMetersScale = 0.0025f;

        [Tooltip("화살표 최소 표시 임계 힘 (N). 이 값 미만이면 화살표를 숨깁니다.")]
        [SerializeField] private float minVisibleForceNewtons = 5.0f;

        [Tooltip("화살표 최대 길이 제한 (m)")]
        [SerializeField] private float maxArrowLengthMeters = 8.0f;

        [Tooltip("화살표 위치 및 벡터 길이/방향 보간 반응 속도 (초 단위 SmoothTime)")]
        [SerializeField] private float vectorSmoothTime = 0.08f;

        [Tooltip("화살표 샤프트 반지름 (m)")]
        [SerializeField] private float arrowShaftRadius = 0.045f;

        [Tooltip("화살표 촉(Cone Head) 길이 비율")]
        [SerializeField] private float arrowHeadLength = 0.32f;

        [Header("5 Aerodynamic Force Vector Colors")]
        [SerializeField] private Color liftVectorColor = new Color(0.15f, 0.95f, 0.35f, 1.0f);       // 초록 (Lift)
        [SerializeField] private Color dragVectorColor = new Color(0.95f, 0.20f, 0.20f, 1.0f);       // 빨강 (Drag)
        [SerializeField] private Color totalForceColor = new Color(1.00f, 0.88f, 0.15f, 1.0f);       // 노랑 (Total)
        [SerializeField] private Color drivingForceColor = new Color(0.00f, 0.95f, 1.00f, 1.0f);     // 시안 (Driving)
        [SerializeField] private Color heelingSideForceColor = new Color(1.00f, 0.52f, 0.10f, 1.0f); // 주황 (Side/Heeling)

        [Header("Streamline Particle & Vortex Shader Controls")]
        [Tooltip("세일 주변 유선(Streamline)을 표현하는 파티클 시스템")]
        [SerializeField] private ParticleSystem streamlineParticleSystem;

        [Tooltip("세일 뒷면(풍하측) 박리 소용돌이(Vortex) 난류 전용 파티클 시스템 (선택 사항)")]
        [SerializeField] private ParticleSystem leewardVortexParticleSystem;

        [Tooltip("유선 셰이더 머티리얼이 적용된 렌더러 배열 (TrailRenderer, ParticleSystemRenderer, 혹은 커스텀 유선 메쉬)")]
        [SerializeField] private Renderer[] streamlineShaderRenderers;

        [Tooltip("Laminar 또는 OptimalSlot 상태일 때의 유선 색상 (시안색, Cyan)")]
        [SerializeField] private Color laminarStreamColor = new Color(0.0f, 0.95f, 1.0f, 0.90f);

        [Tooltip("Stalled(실속/박리) 상태일 때의 유선 및 난류 와류 색상 (붉은색, Red)")]
        [SerializeField] private Color stalledVortexColor = new Color(1.0f, 0.18f, 0.12f, 0.95f);

        [Tooltip("Luffing(펄럭임) 상태일 때의 유선 색상 (흐린 백색/회청색)")]
        [SerializeField] private Color luffingStreamColor = new Color(0.65f, 0.78f, 0.85f, 0.45f);

        [Tooltip("실속(Stalled) 시 세일 뒷면(Leeward) 소용돌이(Vortex) 최대 난류 강도")]
        [SerializeField] private float maxStallVortexIntensity = 3.2f;

        [Tooltip("실속(Stalled) 시 소용돌이 회전 주파수 (Hz)")]
        [SerializeField] private float stallVortexFrequency = 4.5f;

        [Tooltip("유선 색상 및 셰이더 난류 파라미터 전이 보간 속도")]
        [SerializeField] private float shaderTransitionSpeed = 7.5f;

        #endregion

        #region Internal State & Procedural 3D Arrow Handles

        private struct ProceduralArrow3D
        {
            public Transform Root;
            public Transform Shaft;
            public Transform Head;
            public Renderer ShaftRenderer;
            public Renderer HeadRenderer;
            public Vector3 SmoothedVector;
            public Vector3 VectorVelocity;
        }

        private ProceduralArrow3D _liftArrow;
        private ProceduralArrow3D _dragArrow;
        private ProceduralArrow3D _totalArrow;
        private ProceduralArrow3D _drivingArrow;
        private ProceduralArrow3D _sideArrow;

        private AeroForceTelemetry _targetTelemetry;
        private bool _hasReceivedTelemetry;

        private Vector3 _smoothedCEWorldPos;
        private Vector3 _cePosVelocity;

        // 셰이더 및 파티클 스무딩 상태 변수
        private Color _currentStreamColor;
        private float _currentVortexIntensity;
        private float _currentStallProgress;
        private float _currentSlotBoostFactor;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _sharedConeMesh;

        #endregion

        #region Unity Lifecycle & Strict Observer Subscription

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            _currentStreamColor = laminarStreamColor;
            _sharedConeMesh = CreateProceduralConeMesh(20);

            Initialize3DVectorArrows();
        }

        private void OnEnable()
        {
            if (aeroSolver == null)
            {
                aeroSolver = FindObjectOfType<J80AerodynamicsSolver>();
            }

            if (aeroSolver != null)
            {
                // 오직 OnTelemetryUpdated 이벤트만 구독하여 비침투적(Non-intrusive)으로 동작
                aeroSolver.OnTelemetryUpdated += OnTelemetryUpdated;
            }
        }

        private void OnDisable()
        {
            if (aeroSolver != null)
            {
                aeroSolver.OnTelemetryUpdated -= OnTelemetryUpdated;
            }
        }

        /// <summary>
        /// J80AerodynamicsSolver로부터 매 연산 완료 시 호출되는 Observer 콜백
        /// </summary>
        private void OnTelemetryUpdated(AeroForceTelemetry data)
        {
            if (!_hasReceivedTelemetry)
            {
                _smoothedCEWorldPos = data.CenterOfEffortWorldPos;
                _liftArrow.SmoothedVector = data.LiftVector;
                _dragArrow.SmoothedVector = data.DragVector;
                _totalArrow.SmoothedVector = data.TotalAeroForceVector;
                _drivingArrow.SmoothedVector = data.DrivingForceVector;
                _sideArrow.SmoothedVector = data.HeelingSideForceVector;
                _hasReceivedTelemetry = true;
            }

            _targetTelemetry = data;
        }

        private void LateUpdate()
        {
            if (!_hasReceivedTelemetry)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 1e-6f)
            {
                return;
            }

            // 1. 풍압중심(CenterOfEffortWorldPos) 및 5대 벡터 화살표 부드러운 프레임 갱신
            UpdateAllVectorArrowsSmoothly(dt);

            // 2. FlowState에 따른 유선(Streamline) 파티클 색상 및 세일 뒷면 소용돌이(Vortex) 셰이더 제어
            UpdateStreamlineAndVortexShaders(dt);
        }

        #endregion

        #region 3D Vector Arrows Construction & Smooth Rendering

        private void Initialize3DVectorArrows()
        {
            _liftArrow = CreateArrowInstance("Vector_Lift_Green", liftVectorColor);
            _dragArrow = CreateArrowInstance("Vector_Drag_Red", dragVectorColor);
            _totalArrow = CreateArrowInstance("Vector_Total_Yellow", totalForceColor);
            _drivingArrow = CreateArrowInstance("Vector_Driving_Cyan", drivingForceColor);
            _sideArrow = CreateArrowInstance("Vector_HeelingSide_Orange", heelingSideForceColor);
        }

        private ProceduralArrow3D CreateArrowInstance(string arrowName, Color arrowColor)
        {
            GameObject rootObj = new GameObject(arrowName);
            rootObj.transform.SetParent(transform, false);

            // 샤프트(원기둥) 생성
            GameObject shaftObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaftObj.name = "Shaft";
            shaftObj.transform.SetParent(rootObj.transform, false);
            if (shaftObj.TryGetComponent<Collider>(out var shaftCol))
            {
                Destroy(shaftCol);
            }

            // 화살촉(원뿔 메쉬) 생성
            GameObject headObj = new GameObject("ConeHead");
            headObj.transform.SetParent(rootObj.transform, false);
            MeshFilter headMf = headObj.AddComponent<MeshFilter>();
            headMf.sharedMesh = _sharedConeMesh;
            MeshRenderer headMr = headObj.AddComponent<MeshRenderer>();

            // HDR/Unlit 자체 발광 머티리얼 설정
            Shader arrowShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (arrowShader == null) arrowShader = Shader.Find("Unlit/Color");
            if (arrowShader == null) arrowShader = Shader.Find("Standard");

            Material mat = new Material(arrowShader)
            {
                color = arrowColor
            };
            mat.SetColor(BaseColorPropId, arrowColor);
            mat.SetColor(ColorPropId, arrowColor);

            Renderer shaftMr = shaftObj.GetComponent<Renderer>();
            shaftMr.sharedMaterial = mat;
            headMr.sharedMaterial = mat;

            rootObj.SetActive(false);

            return new ProceduralArrow3D
            {
                Root = rootObj.transform,
                Shaft = shaftObj.transform,
                Head = headObj.transform,
                ShaftRenderer = shaftMr,
                HeadRenderer = headMr
            };
        }

        private void UpdateAllVectorArrowsSmoothly(float dt)
        {
            // 기점인 CenterOfEffortWorldPos를 부드럽게 보간
            _smoothedCEWorldPos = Vector3.SmoothDamp(
                _smoothedCEWorldPos,
                _targetTelemetry.CenterOfEffortWorldPos,
                ref _cePosVelocity,
                vectorSmoothTime,
                Mathf.Infinity,
                dt);

            // 5개의 3D 벡터 화살표 갱신 (Lift: 초록, Drag: 빨강, Total: 노랑, Driving: 시안, Side/Heeling: 주황)
            UpdateSingleArrow(ref _liftArrow, _smoothedCEWorldPos, _targetTelemetry.LiftVector, dt);
            UpdateSingleArrow(ref _dragArrow, _smoothedCEWorldPos, _targetTelemetry.DragVector, dt);
            UpdateSingleArrow(ref _totalArrow, _smoothedCEWorldPos, _targetTelemetry.TotalAeroForceVector, dt);
            UpdateSingleArrow(ref _drivingArrow, _smoothedCEWorldPos, _targetTelemetry.DrivingForceVector, dt);
            UpdateSingleArrow(ref _sideArrow, _smoothedCEWorldPos, _targetTelemetry.HeelingSideForceVector, dt);
        }

        private void UpdateSingleArrow(ref ProceduralArrow3D arrow, Vector3 startWorldPos, Vector3 targetForceNewtons, float dt)
        {
            arrow.SmoothedVector = Vector3.SmoothDamp(
                arrow.SmoothedVector,
                targetForceNewtons,
                ref arrow.VectorVelocity,
                vectorSmoothTime,
                Mathf.Infinity,
                dt);

            float forceMagnitudeN = arrow.SmoothedVector.magnitude;

            if (forceMagnitudeN < minVisibleForceNewtons)
            {
                if (arrow.Root.gameObject.activeSelf)
                {
                    arrow.Root.gameObject.SetActive(false);
                }
                return;
            }

            if (!arrow.Root.gameObject.activeSelf)
            {
                arrow.Root.gameObject.SetActive(true);
            }

            // 뉴턴(N) 크기에 비례하여 스케일 팩터 적용
            float totalLength = Mathf.Clamp(forceMagnitudeN * newtonsToMetersScale, 0.15f, maxArrowLengthMeters);
            Vector3 direction = arrow.SmoothedVector / forceMagnitudeN;

            arrow.Root.position = startWorldPos;
            arrow.Root.rotation = Quaternion.LookRotation(direction, Vector3.up);

            float headLen = Mathf.Min(arrowHeadLength, totalLength * 0.4f);
            float shaftLen = Mathf.Max(0.02f, totalLength - headLen);

            // Unity 기본 Cylinder 높이는 2m이므로 Y 스케일은 shaftLen * 0.5f
            arrow.Shaft.localPosition = new Vector3(0.0f, 0.0f, shaftLen * 0.5f);
            arrow.Shaft.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            arrow.Shaft.localScale = new Vector3(arrowShaftRadius * 2.0f, shaftLen * 0.5f, arrowShaftRadius * 2.0f);

            // 화살촉(Cone)을 샤프트 끝단에 배치
            float headRadius = arrowShaftRadius * 2.4f;
            arrow.Head.localPosition = new Vector3(0.0f, 0.0f, shaftLen);
            arrow.Head.localRotation = Quaternion.identity;
            arrow.Head.localScale = new Vector3(headRadius, headRadius, headLen);
        }

        #endregion

        #region Streamline Particle & Leeward Vortex Turbulence Shader Controller

        /// <summary>
        /// FlowState에 따라 유선(Streamline) 파티클 색상 및 세일 뒷면(Leeward) 소용돌이(Vortex) 셰이더 파라미터를 갱신합니다.
        /// - Laminar / OptimalSlot: 시안색(Cyan) 층류 유선, 난류 와류 0
        /// - Stalled: 붉은색(Red) 전환 + 세일 뒷면(풍하측) Vortex 난류/소용돌이 효과 활성화
        /// </summary>
        private void UpdateStreamlineAndVortexShaders(float dt)
        {
            FlowState state = _targetTelemetry.FlowState;

            // 1. 목표 색상 및 목표 소용돌이(Vortex) 난류 강도 결정
            Color targetColor;
            float targetVortexIntensity;
            float targetStallProgress;
            float targetSlotBoost = Mathf.Clamp01((_targetTelemetry.SlotEffectMultiplier - 1.0f) / J80AerodynamicsSolver.MAX_SLOT_EFFECT_BOOST);

            switch (state)
            {
                case FlowState.Laminar:
                case FlowState.OptimalSlot:
                    // Laminar 또는 OptimalSlot일 때는 시안색(Cyan)으로 설정
                    targetColor = laminarStreamColor;
                    targetVortexIntensity = 0.0f;
                    targetStallProgress = 0.0f;
                    break;

                case FlowState.Stalled:
                    // Stalled일 때는 붉은색(Red)으로 전환하고 세일 뒷면에 Vortex 소용돌이 난류 활성화
                    targetColor = stalledVortexColor;
                    float stallSeverity = Mathf.Max(0.5f, _targetTelemetry.StallProgress01);
                    targetVortexIntensity = maxStallVortexIntensity * stallSeverity;
                    targetStallProgress = stallSeverity;
                    break;

                case FlowState.Luffing:
                default:
                    targetColor = luffingStreamColor;
                    targetVortexIntensity = 0.25f; // 러핑 시 미세한 세일 플러터(Flutter) 흔들림
                    targetStallProgress = 0.0f;
                    break;
            }

            // 2. 프레임 독립적 지수 보간(Exponential Smoothing)
            float lerpFactor = 1.0f - Mathf.Exp(-shaderTransitionSpeed * dt);
            _currentStreamColor = Color.Lerp(_currentStreamColor, targetColor, lerpFactor);
            _currentVortexIntensity = Mathf.Lerp(_currentVortexIntensity, targetVortexIntensity, lerpFactor);
            _currentStallProgress = Mathf.Lerp(_currentStallProgress, targetStallProgress, lerpFactor);
            _currentSlotBoostFactor = Mathf.Lerp(_currentSlotBoostFactor, targetSlotBoost, lerpFactor);

            // 3. 세일 뒷면(풍하측, Leeward) 소용돌이 중심 좌표 계산
            // 양력(LiftVector)이 가리키는 풍하측 및 바람 하류(DragVector) 방향으로 살짝 이동한 세일 뒷면 영역
            Vector3 leewardDir = _targetTelemetry.LiftVector.sqrMagnitude > 1e-3f
                ? _targetTelemetry.LiftVector.normalized
                : Vector3.right;
            Vector3 downstreamDir = _targetTelemetry.DragVector.sqrMagnitude > 1e-3f
                ? _targetTelemetry.DragVector.normalized
                : Vector3.back;
            Vector3 leewardVortexCenter = _smoothedCEWorldPos + (leewardDir * 0.65f) + (downstreamDir * 0.85f);

            // 4. 유선 셰이더 머티리얼 파라미터(MaterialPropertyBlock) 업데이트
            if (streamlineShaderRenderers != null)
            {
                // OptimalSlot일 때 시안색 발광(Emission) 휘도를 높여 슬롯 가속 효과를 시각적으로 강조
                float emissionMultiplier = (state == FlowState.OptimalSlot) ? (1.8f + 0.7f * _currentSlotBoostFactor) : 1.25f;
                Color hdrEmissionColor = _currentStreamColor * emissionMultiplier;

                for (int i = 0; i < streamlineShaderRenderers.Length; i++)
                {
                    Renderer r = streamlineShaderRenderers[i];
                    if (r == null) continue;

                    r.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(StreamColorPropId, _currentStreamColor);
                    _propertyBlock.SetColor(BaseColorPropId, _currentStreamColor);
                    _propertyBlock.SetColor(ColorPropId, _currentStreamColor);
                    _propertyBlock.SetColor(EmissionColorPropId, hdrEmissionColor);
                    _propertyBlock.SetFloat(VortexIntensityPropId, _currentVortexIntensity);
                    _propertyBlock.SetFloat(VortexFrequencyPropId, stallVortexFrequency);
                    _propertyBlock.SetVector(VortexCenterWorldPropId, leewardVortexCenter);
                    _propertyBlock.SetFloat(StallProgressPropId, _currentStallProgress);
                    _propertyBlock.SetFloat(SlotBoostPropId, _currentSlotBoostFactor);
                    r.SetPropertyBlock(_propertyBlock);
                }
            }

            // 5. 메인 유선(Streamline) 파티클 시스템 제어 (색상, 유속, 컬 노이즈 난류)
            if (streamlineParticleSystem != null)
            {
                var mainModule = streamlineParticleSystem.main;
                mainModule.startColor = _currentStreamColor;

                // 겉보기 바람 속도에 비례하여 유선 파티클 속도 조절 (OptimalSlot 시 벤투리 가속 반영)
                float awSpeed = _targetTelemetry.ApparentWindVector.magnitude;
                float slotSpeedBoost = (state == FlowState.OptimalSlot) ? 1.15f : 1.0f;
                mainModule.startSpeed = Mathf.Max(1.0f, awSpeed * slotSpeedBoost);

                // Stalled 상태일 때 파티클 NoiseModule을 활성화하여 세일 뒷면 소용돌이(Vortex) 궤적 형성
                var noiseModule = streamlineParticleSystem.noise;
                noiseModule.enabled = _currentVortexIntensity > 0.05f;
                noiseModule.strength = _currentVortexIntensity;
                noiseModule.frequency = stallVortexFrequency * 0.25f;
                noiseModule.scrollSpeed = stallVortexFrequency * 0.5f;
            }

            // 6. 세일 뒷면 전용 소용돌이(Leeward Vortex) 파티클 시스템 제어 (할당된 경우)
            if (leewardVortexParticleSystem != null)
            {
                leewardVortexParticleSystem.transform.position = leewardVortexCenter;
                if (downstreamDir.sqrMagnitude > 1e-4f)
                {
                    leewardVortexParticleSystem.transform.rotation = Quaternion.LookRotation(downstreamDir, Vector3.up);
                }

                var vortexMain = leewardVortexParticleSystem.main;
                vortexMain.startColor = _currentStreamColor;

                var emission = leewardVortexParticleSystem.emission;
                bool isStalled = (state == FlowState.Stalled);
                emission.enabled = isStalled;

                if (isStalled)
                {
                    // 박리 진행도에 비례해 세일 뒷면 와류 파티클 방출량 및 회전 각속도(Orbital Velocity) 증가
                    var velOverLifetime = leewardVortexParticleSystem.velocityOverLifetime;
                    velOverLifetime.enabled = true;
                    velOverLifetime.orbitalZ = stallVortexFrequency * _currentVortexIntensity;

                    if (!leewardVortexParticleSystem.isPlaying)
                    {
                        leewardVortexParticleSystem.Play();
                    }
                }
            }
        }

        #endregion

        #region Procedural Cone Mesh Helper

        /// <summary>
        /// 외부 에셋 의존성 없이 즉시 3D 화살촉(Cone)을 렌더링하기 위한 프로시저럴 메쉬 생성기
        /// (+Z축 방향으로 뻗는 밑면 반지름 1, 높이 1인 원뿔)
        /// </summary>
        private static Mesh CreateProceduralConeMesh(int subdivisions)
        {
            Mesh mesh = new Mesh { name = "ProceduralArrowCone" };

            int vertexCount = subdivisions + 2;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            int[] triangles = new int[subdivisions * 6];

            // 0번 정점: 원뿔 꼭짓점 (+Z = 1), 1번 정점: 밑면 중심 (Z = 0)
            vertices[0] = new Vector3(0f, 0f, 1f);
            normals[0] = Vector3.forward;
            vertices[1] = Vector3.zero;
            normals[1] = Vector3.back;

            float angleStep = Mathf.PI * 2.0f / subdivisions;
            for (int i = 0; i < subdivisions; i++)
            {
                float angle = i * angleStep;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);
                vertices[i + 2] = new Vector3(x, y, 0f);
                normals[i + 2] = new Vector3(x, y, 0.5f).normalized;
            }

            int triIdx = 0;
            for (int i = 0; i < subdivisions; i++)
            {
                int current = i + 2;
                int next = ((i + 1) % subdivisions) + 2;

                // 옆면 삼각형
                triangles[triIdx++] = 0;
                triangles[triIdx++] = current;
                triangles[triIdx++] = next;

                // 밑면 삼각형
                triangles[triIdx++] = 1;
                triangles[triIdx++] = next;
                triangles[triIdx++] = current;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        #endregion
    }
}
