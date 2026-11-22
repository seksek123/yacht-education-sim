using UnityEngine;
using UnityEngine.UI;

namespace J80.Biomechanics
{
    /// <summary>
    /// [Task 2] AR 비주얼 및 생체역학 HUD UI 오버레이 컴포넌트
    /// BiomechanicalForceTracker의 OnForceMetricsCalculated(gripForce, torque, stamina) 이벤트를 구독하여:
    /// 1) 5:1 메인시트 블록 위치에 "650 N" -> "감속(5:1)" -> "실 가해 힘: 130 N" 셰이더/파티클 콜아웃 라인 및 텍스트 렌더링
    /// 2) 틸러(Tiller) 부근에 호(Arc) 형태의 조타각 미터(-6.2°)와 부드럽게 감쇄(SmoothDamp)된 실시간 토크(18.5 N·m) 표시
    /// 3) 피교육생 관절(목, 어깨, 팔꿈치, 손목) 위치에 인체 부하(Green-Yellow 그라데이션 전이)를 가시화하는 2D/3D Skeletal 라인 드로잉 수행
    /// </summary>
    public class ForceHUDOverlay : MonoBehaviour
    {
        #region Shader Property IDs

        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
        private static readonly int PulseSpeedPropId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int LoadRatioPropId = Shader.PropertyToID("_LoadRatio");

        #endregion

        #region Inspector Settings

        [Header("Observer Subscription Target")]
        [Tooltip("구독할 BiomechanicalForceTracker 컴포넌트 (미할당 시 씬에서 자동 탐색)")]
        [SerializeField] private BiomechanicalForceTracker forceTracker;

        [Header("1. Mainsheet (5:1 Tackle) 3D Callout & Particle/Shader Settings")]
        [Tooltip("5:1 메인시트 블록 월드 기준점 (미할당 시 forceTracker의 MainsheetBlockTransform 자동 참조)")]
        [SerializeField] private Transform mainsheetBlockAnchor;

        [Tooltip("메인시트 블록에서 3D HUD 텍스트 패널로 뻗어나가는 콜아웃 오프셋 (로컬/월드 오프셋)")]
        [SerializeField] private Vector3 mainsheetCalloutOffset = new Vector3(0.55f, 0.32f, -0.15f);

        [Tooltip("5:1 블록 에너지 플로우 표현용 파티클 시스템 (선택 사항, 미할당 시 런타임 프로시저럴 생성)")]
        [SerializeField] private ParticleSystem mainsheetFlowParticles;

        [Tooltip("메인시트 콜아웃 라인/셰이더 색상 (시안-그린 AR 글로우)")]
        [SerializeField] private Color mainsheetHudColor = new Color(0.25f, 1.0f, 0.85f, 0.95f);

        [Header("2. Tiller Arc Gauge & Damped Torque Display")]
        [Tooltip("틸러(조타기) 피벗 또는 그립 기준점 (미할당 시 forceTracker의 TillerTransform 참조)")]
        [SerializeField] private Transform tillerArcCenterAnchor;

        [Tooltip("호(Arc) 조타각 미터의 반지름 (m)")]
        [SerializeField] private float tillerArcRadius = 0.38f;

        [Tooltip("호(Arc) 게이지 최대 표시 각도 범위 (예: ±30도)")]
        [SerializeField] private float tillerArcMaxSweepDeg = 30.0f;

        [Tooltip("조타각(-6.2°) 및 실시간 토크(18.5 N·m) 감쇄 보간 시간 (SmoothTime, 초)")]
        [SerializeField] private float tillerHudSmoothTime = 0.12f;

        [Header("3. Trainee Skeletal Joint Load Overlay (Green-Yellow Transition)")]
        [Tooltip("목/상부 흉추 관절 (Neck / C7)")]
        [SerializeField] private Transform neckJoint;

        [Tooltip("우측 어깨 관절 (Right Shoulder - 틸러 익스텐션 조작 팔)")]
        [SerializeField] private Transform rightShoulderJoint;

        [Tooltip("우측 팔꿈치 관절 (Right Elbow)")]
        [SerializeField] private Transform rightElbowJoint;

        [Tooltip("우측 손목 관절 (Right Wrist)")]
        [SerializeField] private Transform rightWristJoint;

        [Tooltip("좌측 어깨 관절 (Left Shoulder - 메인시트 당김 팔)")]
        [SerializeField] private Transform leftShoulderJoint;

        [Tooltip("좌측 팔꿈치 관절 (Left Elbow)")]
        [SerializeField] private Transform leftElbowJoint;

        [Tooltip("좌측 손목 관절 (Left Wrist)")]
        [SerializeField] private Transform leftWristJoint;

        [Tooltip("낮은 인체 부하 시 골격/관절 색상 (Green, #2ECC71)")]
        [SerializeField] private Color lowLoadGreenColor = new Color(0.18f, 0.92f, 0.38f, 0.88f);

        [Tooltip("높은 인체 부하 시 골격/관절 색상 (Yellow, #F1C40F)")]
        [SerializeField] private Color highLoadYellowColor = new Color(1.00f, 0.88f, 0.12f, 0.95f);

        [Tooltip("골격 라인 두께 (m)")]
        [SerializeField] private float skeletalBoneWidth = 0.032f;

        [Tooltip("관절 노드 구체 반지름 (m)")]
        [SerializeField] private float jointNodeRadius = 0.048f;

        [Header("Optional 2D Screen-Space UI Text Bindings")]
        [SerializeField] private Text uiMainsheetSummaryText;
        [SerializeField] private Text uiTillerMetricsText;
        [SerializeField] private Text uiErgonomicStaminaText;

        #endregion

        #region Internal Runtime State & 3D AR Renderers

        // 이벤트로부터 수신된 최신 목표값
        private float _targetGripForceN = 130.0f;
        private float _targetTorqueNm = 18.5f;
        private float _targetStaminaPercent = 82.0f;
        private float _targetMainsailLoadN = 650.0f;
        private float _targetTackleRatio = 5.0f;
        private float _targetTillerAngleDeg = -6.2f;

        // 부드럽게 감쇄(SmoothDamp) 연산된 표시값
        private float _smoothedGripForceN = 130.0f;
        private float _gripForceVel;

        private float _smoothedMainsailLoadN = 650.0f;
        private float _mainsailLoadVel;

        private float _smoothedTorqueNm = 18.5f;
        private float _torqueVel;

        private float _smoothedTillerAngleDeg = -6.2f;
        private float _tillerAngleVel;

        private float _smoothedStaminaPercent = 82.0f;
        private float _staminaVel;

        private float _smoothedLoad01;
        private float _load01Vel;

        // 1) 메인시트 5:1 블록 콜아웃 렌더러
        private LineRenderer _mainsheetCalloutLine;
        private TextMesh _mainsheet3DText;

        // 2) 틸러 호(Arc) 조타각 미터 렌더러
        private LineRenderer _tillerBackgroundArcLine;
        private LineRenderer _tillerActiveAngleArcLine;
        private TextMesh _tiller3DText;

        // 3) 관절 인체 부하 Skeletal 오버레이 렌더러
        private struct SkeletalBoneSegment
        {
            public Transform StartJoint;
            public Transform EndJoint;
            public LineRenderer BoneLine;
            public float LoadWeight; // 해당 뼈대의 부하 가중치 (어깨/팔꿈치 강조)
        }

        private SkeletalBoneSegment[] _skeletalBones;
        private Renderer[] _jointNodeRenderers;
        private float[] _jointLoadWeights;
        private TextMesh _skeletalErgonomic3DText;

        private Material _arAdditiveLineMaterial;
        private MaterialPropertyBlock _propertyBlock;

        #endregion

        #region Unity Lifecycle & Strict Observer Subscription

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            CreateARShaderMaterial();
            InitializeMainsheetCalloutVisuals();
            InitializeTillerArcVisuals();
            InitializeSkeletalOverlayVisuals();
        }

        private void OnEnable()
        {
            if (forceTracker == null)
            {
                forceTracker = FindObjectOfType<BiomechanicalForceTracker>();
            }

            if (forceTracker != null)
            {
                // [Task 2-1] BiomechanicalForceTracker의 이벤트 구독
                forceTracker.OnForceMetricsCalculated += OnForceMetricsCalculated;

                if (mainsheetBlockAnchor == null)
                    mainsheetBlockAnchor = forceTracker.MainsheetBlockTransform;
                if (tillerArcCenterAnchor == null)
                    tillerArcCenterAnchor = forceTracker.TillerTransform;
            }
        }

        private void OnDisable()
        {
            if (forceTracker != null)
            {
                forceTracker.OnForceMetricsCalculated -= OnForceMetricsCalculated;
            }
        }

        /// <summary>
        /// BiomechanicalForceTracker.OnForceMetricsCalculated(float gripForce, float torque, float stamina) 콜백
        /// </summary>
        private void OnForceMetricsCalculated(float gripForce, float torque, float stamina)
        {
            _targetGripForceN = gripForce;
            _targetTorqueNm = torque;
            _targetStaminaPercent = stamina;

            if (forceTracker != null)
            {
                _targetMainsailLoadN = forceTracker.CurrentMainsailLoad;
                _targetTackleRatio = forceTracker.CurrentTackleRatio;
                _targetTillerAngleDeg = forceTracker.CurrentTillerAngleDeg;
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 1e-6f) return;

            // 1. 수치들의 부드러운 감쇄 연산 (SmoothDamp)
            UpdateDampedTelemetry(dt);

            // 2. 5:1 메인시트 블록 콜아웃 라인, 파티클/셰이더 및 텍스트 갱신
            UpdateMainsheetBlockOverlay(dt);

            // 3. 틸러 호(Arc) 조타각 미터(-6.2°) 및 실시간 토크(18.5 N·m) 갱신
            UpdateTillerArcAndTorqueOverlay();

            // 4. 관절 위치(어깨, 팔꿈치 등) Green-Yellow 인체 부하 Skeletal 드로잉 갱신
            UpdateSkeletalJointLoadOverlay();

            // 5. 선택적 2D Canvas UI 텍스트 동기화
            UpdateOptional2DUIText();
        }

        #endregion

        #region Smooth Damping Calculation

        private void UpdateDampedTelemetry(float dt)
        {
            _smoothedMainsailLoadN = Mathf.SmoothDamp(_smoothedMainsailLoadN, _targetMainsailLoadN, ref _mainsailLoadVel, tillerHudSmoothTime, Mathf.Infinity, dt);
            _smoothedGripForceN = Mathf.SmoothDamp(_smoothedGripForceN, _targetGripForceN, ref _gripForceVel, tillerHudSmoothTime, Mathf.Infinity, dt);
            _smoothedTorqueNm = Mathf.SmoothDamp(_smoothedTorqueNm, _targetTorqueNm, ref _torqueVel, tillerHudSmoothTime, Mathf.Infinity, dt);
            _smoothedTillerAngleDeg = Mathf.SmoothDamp(_smoothedTillerAngleDeg, _targetTillerAngleDeg, ref _tillerAngleVel, tillerHudSmoothTime, Mathf.Infinity, dt);
            _smoothedStaminaPercent = Mathf.SmoothDamp(_smoothedStaminaPercent, _targetStaminaPercent, ref _staminaVel, tillerHudSmoothTime, Mathf.Infinity, dt);

            float targetLoad01 = forceTracker != null
                ? forceTracker.NormalizedBiomechanicalLoad01
                : Mathf.Clamp01((_smoothedGripForceN / 250.0f) * 0.6f + (_smoothedTorqueNm / 35.0f) * 0.4f);

            _smoothedLoad01 = Mathf.SmoothDamp(_smoothedLoad01, targetLoad01, ref _load01Vel, tillerHudSmoothTime, Mathf.Infinity, dt);
        }

        #endregion

        #region Task 2-2: 5:1 Mainsheet Block 3D Callout ("650 N" -> "감속(5:1)" -> "실 가해 힘: 130 N")

        private void InitializeMainsheetCalloutVisuals()
        {
            GameObject calloutRoot = new GameObject("HUD_Mainsheet5to1_Callout");
            calloutRoot.transform.SetParent(transform, false);

            _mainsheetCalloutLine = calloutRoot.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_mainsheetCalloutLine, 3, 0.014f, mainsheetHudColor);

            // 3D 월드 텍스트 생성
            _mainsheet3DText = CreateWorldSpaceTextMesh(
                calloutRoot.transform,
                "Mainsheet3DText",
                0.022f,
                TextAnchor.MiddleLeft,
                mainsheetHudColor);

            // 파티클 시스템이 할당되지 않은 경우 블록 주변에 에너지 텐션 글로우 파티클 자동 생성
            if (mainsheetFlowParticles == null)
            {
                GameObject ptObj = new GameObject("MainsheetTensionParticles");
                ptObj.transform.SetParent(calloutRoot.transform, false);
                mainsheetFlowParticles = ptObj.AddComponent<ParticleSystem>();

                var main = mainsheetFlowParticles.main;
                main.startSize = 0.035f;
                main.startLifetime = 0.65f;
                main.startSpeed = 0.45f;
                main.maxParticles = 40;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = mainsheetHudColor;

                var shape = mainsheetFlowParticles.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.08f;

                var pr = ptObj.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = _arAdditiveLineMaterial;
            }
        }

        private void UpdateMainsheetBlockOverlay(float dt)
        {
            Vector3 blockWorldPos = mainsheetBlockAnchor != null
                ? mainsheetBlockAnchor.position
                : transform.TransformPoint(new Vector3(0.0f, 0.45f, -0.2f));

            Vector3 elbowPos = blockWorldPos + new Vector3(mainsheetCalloutOffset.x * 0.45f, mainsheetCalloutOffset.y, mainsheetCalloutOffset.z * 0.45f);
            Vector3 labelPos = blockWorldPos + mainsheetCalloutOffset;

            // 3점 지시선 (블록 원점 -> 꺾임점 -> 텍스트 패널 언더라인)
            _mainsheetCalloutLine.SetPosition(0, blockWorldPos);
            _mainsheetCalloutLine.SetPosition(1, elbowPos);
            _mainsheetCalloutLine.SetPosition(2, labelPos);

            // 셰이더 펄스 파라미터 업데이트 (장력에 비례하여 에너지 펄스 속도 증가)
            float pulseSpeed = 2.0f + (_smoothedMainsailLoadN / 200.0f);
            _mainsheetCalloutLine.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorPropId, mainsheetHudColor);
            _propertyBlock.SetColor(ColorPropId, mainsheetHudColor);
            _propertyBlock.SetColor(EmissionColorPropId, mainsheetHudColor * 1.6f);
            _propertyBlock.SetFloat(PulseSpeedPropId, pulseSpeed);
            _mainsheetCalloutLine.SetPropertyBlock(_propertyBlock);

            // 파티클 위치 및 방출 속도를 시트 장력(650N)에 연동
            if (mainsheetFlowParticles != null)
            {
                mainsheetFlowParticles.transform.position = blockWorldPos;
                var emission = mainsheetFlowParticles.emission;
                emission.rateOverTime = Mathf.Clamp(_smoothedMainsailLoadN * 0.04f, 5.0f, 45.0f);
            }

            // [Task 2-2 핵심 텍스트 포맷]: "650 N" -> "감속(5:1)" -> "실 가해 힘: 130 N"
            int roundedLoadN = Mathf.RoundToInt(_smoothedMainsailLoadN);
            int roundedRatio = Mathf.Max(1, Mathf.RoundToInt(_targetTackleRatio));
            int roundedGripN = Mathf.RoundToInt(_smoothedGripForceN);

            _mainsheet3DText.transform.position = labelPos + new Vector3(0.04f, 0.02f, 0.0f);
            BillboardToMainCamera(_mainsheet3DText.transform);
            _mainsheet3DText.text =
                $"<b>MAINSHEET ({roundedRatio}:1 TACKLE)</b>\n" +
                $"\"{roundedLoadN} N\" -> \"감속({roundedRatio}:1)\" -> \"실 가해 힘: {roundedGripN} N\"\n" +
                $"<size=28>ROPE TENSION: {roundedLoadN} N | GRIP PULL FORCE: {roundedGripN} N</size>";
        }

        #endregion

        #region Task 2-3: Tiller Arc Steering Angle Meter (-6.2°) & Damped Torque (18.5 N·m)

        private void InitializeTillerArcVisuals()
        {
            GameObject tillerHudRoot = new GameObject("HUD_TillerArc_Gauge");
            tillerHudRoot.transform.SetParent(transform, false);

            // 배경 기준 호(Arc, -30° ~ +30°)
            GameObject bgArcObj = new GameObject("TillerArc_Background");
            bgArcObj.transform.SetParent(tillerHudRoot.transform, false);
            _tillerBackgroundArcLine = bgArcObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_tillerBackgroundArcLine, 33, 0.012f, new Color(0.2f, 0.85f, 0.95f, 0.38f));

            // 현재 조타각(-6.2°)을 가리키는 활성 호(Active Sweep Arc)
            GameObject activeArcObj = new GameObject("TillerArc_ActiveAngle");
            activeArcObj.transform.SetParent(tillerHudRoot.transform, false);
            _tillerActiveAngleArcLine = activeArcObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_tillerActiveAngleArcLine, 21, 0.024f, new Color(0.45f, 1.0f, 0.55f, 0.95f));

            // 틸러 조타각 및 토크 3D 텍스트
            _tiller3DText = CreateWorldSpaceTextMesh(
                tillerHudRoot.transform,
                "Tiller3DText",
                0.021f,
                TextAnchor.UpperCenter,
                Color.white);
        }

        private void UpdateTillerArcAndTorqueOverlay()
        {
            Vector3 arcCenter = tillerArcCenterAnchor != null
                ? tillerArcCenterAnchor.position
                : transform.TransformPoint(new Vector3(0.0f, 0.35f, -0.85f));

            Quaternion basisRot = (tillerArcCenterAnchor != null && tillerArcCenterAnchor.parent != null)
                ? tillerArcCenterAnchor.parent.rotation
                : transform.rotation;

            // 1. 배경 눈금 호(-tillerArcMaxSweepDeg ~ +tillerArcMaxSweepDeg) 그리기
            DrawCircularArc(_tillerBackgroundArcLine, arcCenter, basisRot, -tillerArcMaxSweepDeg, tillerArcMaxSweepDeg, tillerArcRadius, 33);

            // 2. 현재 감쇄 연산된 조타각(예: -6.2°) 호 그리기
            float clampedAngle = Mathf.Clamp(_smoothedTillerAngleDeg, -tillerArcMaxSweepDeg, tillerArcMaxSweepDeg);
            DrawCircularArc(_tillerActiveAngleArcLine, arcCenter, basisRot, 0.0f, clampedAngle, tillerArcRadius * 1.02f, 21);

            // 토크 부하율에 따라 활성 호 색상을 Green -> Yellow로 연동
            Color arcColor = Color.Lerp(lowLoadGreenColor, highLoadYellowColor, _smoothedLoad01);
            _tillerActiveAngleArcLine.startColor = arcColor;
            _tillerActiveAngleArcLine.endColor = arcColor;

            // 3. 실시간 조타각(-6.2°) 및 감쇄 토크(18.5 N·m) 텍스트 표시
            float capacityPct = forceTracker != null ? forceTracker.HumanTorqueCapacityPercent : Mathf.Clamp((_smoothedTorqueNm / 44.0f) * 100f, 0f, 100f);
            _tiller3DText.transform.position = arcCenter + (basisRot * new Vector3(0.0f, -0.12f, -tillerArcRadius * 0.8f));
            BillboardToMainCamera(_tiller3DText.transform);

            _tiller3DText.text =
                $"<b>TILLER INPUT</b>\n" +
                $"ANGLE: <b>{_smoothedTillerAngleDeg:F1}°</b>\n" +
                $"TORQUE: <b>{_smoothedTorqueNm:F1} N·m</b> / HUMAN CAPACITY: {Mathf.RoundToInt(capacityPct)}%";
        }

        private static void DrawCircularArc(
            LineRenderer lr,
            Vector3 centerWorld,
            Quaternion referenceRotation,
            float startAngleDeg,
            float endAngleDeg,
            float radius,
            int pointCount)
        {
            lr.positionCount = pointCount;
            for (int i = 0; i < pointCount; i++)
            {
                float t = pointCount > 1 ? (float)i / (pointCount - 1) : 0.0f;
                float angleRad = Mathf.Lerp(startAngleDeg, endAngleDeg, t) * Mathf.Deg2Rad;

                // 선미 방향(-Z)을 0도 기준으로 수평/경사 호 생성
                Vector3 localPos = new Vector3(Mathf.Sin(angleRad) * radius, 0.05f, -Mathf.Cos(angleRad) * radius);
                lr.SetPosition(i, centerWorld + (referenceRotation * localPos));
            }
        }

        #endregion

        #region Task 2-4: Skeletal Joint Load Visualization (Green-Yellow Transition)

        private void InitializeSkeletalOverlayVisuals()
        {
            GameObject skelRoot = new GameObject("HUD_TraineeSkeletal_Overlay");
            skelRoot.transform.SetParent(transform, false);

            // 관절 Transform이 인스펙터에 할당되지 않은 경우 Keel boat 조타수 착석 자세 기준 기본 아바타 관절 노드 생성
            EnsureDefaultSkeletalJointsIfUnassigned(skelRoot.transform);

            // 상체 주요 뼈대 연결 (목-우측어깨-우측팔꿈치-우측손목, 목-좌측어깨-좌측팔꿈치-좌측손목)
            _skeletalBones = new[]
            {
                CreateBoneSegment(skelRoot.transform, "Bone_Neck_RShoulder", neckJoint, rightShoulderJoint, 0.85f),
                CreateBoneSegment(skelRoot.transform, "Bone_RShoulder_RElbow", rightShoulderJoint, rightElbowJoint, 1.00f),
                CreateBoneSegment(skelRoot.transform, "Bone_RElbow_RWrist", rightElbowJoint, rightWristJoint, 0.90f),
                CreateBoneSegment(skelRoot.transform, "Bone_Neck_LShoulder", neckJoint, leftShoulderJoint, 0.80f),
                CreateBoneSegment(skelRoot.transform, "Bone_LShoulder_LElbow", leftShoulderJoint, leftElbowJoint, 0.95f),
                CreateBoneSegment(skelRoot.transform, "Bone_LElbow_LWrist", leftElbowJoint, leftWristJoint, 0.85f)
            };

            // 관절 위치(목, 양쪽 어깨, 양쪽 팔꿈치, 양쪽 손목)에 반투명 발광 구체 노드 배치
            Transform[] joints =
            {
                neckJoint,
                rightShoulderJoint, rightElbowJoint, rightWristJoint,
                leftShoulderJoint, leftElbowJoint, leftWristJoint
            };

            // 어깨와 팔꿈치에 가장 높은 인체 부하 가중치 부여 (Green -> Yellow 하이라이트)
            _jointLoadWeights = new[] { 0.65f, 1.0f, 1.0f, 0.75f, 0.90f, 0.95f, 0.75f };
            _jointNodeRenderers = new Renderer[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"JointNode_{joints[i].name}";
                sphere.transform.SetParent(joints[i], false);
                sphere.transform.localPosition = Vector3.zero;
                sphere.transform.localScale = Vector3.one * (jointNodeRadius * 2.0f);

                if (sphere.TryGetComponent<Collider>(out var col))
                {
                    Destroy(col);
                }

                Renderer r = sphere.GetComponent<Renderer>();
                r.sharedMaterial = _arAdditiveLineMaterial;
                _jointNodeRenderers[i] = r;
            }

            // 어깨 옆 REBA 인간공학 점수 및 체력(STAMINA) 3D 라벨 생성
            _skeletalErgonomic3DText = CreateWorldSpaceTextMesh(
                skelRoot.transform,
                "ErgonomicStamina3DText",
                0.019f,
                TextAnchor.MiddleRight,
                lowLoadGreenColor);
        }

        private SkeletalBoneSegment CreateBoneSegment(
            Transform parent,
            string segmentName,
            Transform startJoint,
            Transform endJoint,
            float loadWeight)
        {
            GameObject boneObj = new GameObject(segmentName);
            boneObj.transform.SetParent(parent, false);
            LineRenderer lr = boneObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(lr, 2, skeletalBoneWidth, lowLoadGreenColor);

            return new SkeletalBoneSegment
            {
                StartJoint = startJoint,
                EndJoint = endJoint,
                BoneLine = lr,
                LoadWeight = loadWeight
            };
        }

        private void UpdateSkeletalJointLoadOverlay()
        {
            // 1. 각 골격 선(Skeletal Line) 위치 및 Green-Yellow 부하 그라데이션 업데이트
            for (int i = 0; i < _skeletalBones.Length; i++)
            {
                SkeletalBoneSegment seg = _skeletalBones[i];
                if (seg.StartJoint == null || seg.EndJoint == null) continue;

                seg.BoneLine.SetPosition(0, seg.StartJoint.position);
                seg.BoneLine.SetPosition(1, seg.EndJoint.position);

                float localStress01 = Mathf.Clamp01(_smoothedLoad01 * seg.LoadWeight);
                Color boneColor = Color.Lerp(lowLoadGreenColor, highLoadYellowColor, localStress01);

                seg.BoneLine.startColor = boneColor;
                seg.BoneLine.endColor = Color.Lerp(lowLoadGreenColor, highLoadYellowColor, Mathf.Clamp01(localStress01 * 1.1f));

                seg.BoneLine.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorPropId, boneColor);
                _propertyBlock.SetColor(ColorPropId, boneColor);
                _propertyBlock.SetColor(EmissionColorPropId, boneColor * (1.2f + 0.6f * localStress01));
                _propertyBlock.SetFloat(LoadRatioPropId, localStress01);
                seg.BoneLine.SetPropertyBlock(_propertyBlock);
            }

            // 2. 관절 노드(어깨, 팔꿈치 등) Green-Yellow 색상 업데이트
            for (int i = 0; i < _jointNodeRenderers.Length; i++)
            {
                Renderer r = _jointNodeRenderers[i];
                if (r == null) continue;

                float jointStress01 = Mathf.Clamp01(_smoothedLoad01 * _jointLoadWeights[i]);
                Color jointColor = Color.Lerp(lowLoadGreenColor, highLoadYellowColor, jointStress01);

                r.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorPropId, jointColor);
                _propertyBlock.SetColor(ColorPropId, jointColor);
                _propertyBlock.SetColor(EmissionColorPropId, jointColor * (1.5f + 0.8f * jointStress01));
                r.SetPropertyBlock(_propertyBlock);
            }

            // 3. 어깨/상체 옆 REBA 점수 및 STAMINA (%) 텍스트 표시
            if (_skeletalErgonomic3DText != null && rightShoulderJoint != null)
            {
                int rebaScore = forceTracker != null ? forceTracker.RebaErgonomicScore : 4;
                string riskLabel = rebaScore <= 4 ? "Low Risk" : (rebaScore <= 7 ? "Medium Risk" : "High Risk");
                Color statusColor = Color.Lerp(lowLoadGreenColor, highLoadYellowColor, _smoothedLoad01);

                _skeletalErgonomic3DText.transform.position = rightShoulderJoint.position + new Vector3(-0.28f, 0.08f, 0.0f);
                BillboardToMainCamera(_skeletalErgonomic3DText.transform);
                _skeletalErgonomic3DText.color = statusColor;
                _skeletalErgonomic3DText.text =
                    $"<b>REBA ERGONOMIC SCORE: {rebaScore} ({riskLabel})</b>\n" +
                    $"MUSCLE STRESS | <b>STAMINA: {Mathf.RoundToInt(_smoothedStaminaPercent)}%</b>";
            }
        }

        private void EnsureDefaultSkeletalJointsIfUnassigned(Transform parent)
        {
            if (neckJoint == null)
                neckJoint = CreateFallbackJoint(parent, "Neck", new Vector3(-0.45f, 1.15f, -0.55f));
            if (rightShoulderJoint == null)
                rightShoulderJoint = CreateFallbackJoint(parent, "RightShoulder", new Vector3(-0.48f, 0.98f, -0.62f));
            if (rightElbowJoint == null)
                rightElbowJoint = CreateFallbackJoint(parent, "RightElbow", new Vector3(-0.42f, 0.72f, -0.58f));
            if (rightWristJoint == null)
                rightWristJoint = CreateFallbackJoint(parent, "RightWrist", new Vector3(-0.22f, 0.68f, -0.45f));
            if (leftShoulderJoint == null)
                leftShoulderJoint = CreateFallbackJoint(parent, "LeftShoulder", new Vector3(-0.32f, 0.98f, -0.45f));
            if (leftElbowJoint == null)
                leftElbowJoint = CreateFallbackJoint(parent, "LeftElbow", new Vector3(-0.18f, 0.78f, -0.35f));
            if (leftWristJoint == null)
                leftWristJoint = CreateFallbackJoint(parent, "LeftWrist", new Vector3(-0.05f, 0.72f, -0.28f));
        }

        private static Transform CreateFallbackJoint(Transform parent, string name, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        #endregion

        #region UI & Rendering Helpers

        private void UpdateOptional2DUIText()
        {
            int roundedLoadN = Mathf.RoundToInt(_smoothedMainsailLoadN);
            int roundedRatio = Mathf.Max(1, Mathf.RoundToInt(_targetTackleRatio));
            int roundedGripN = Mathf.RoundToInt(_smoothedGripForceN);

            if (uiMainsheetSummaryText != null)
            {
                uiMainsheetSummaryText.text = $"\"{roundedLoadN} N\" -> \"감속({roundedRatio}:1)\" -> \"실 가해 힘: {roundedGripN} N\"";
            }

            if (uiTillerMetricsText != null)
            {
                uiTillerMetricsText.text = $"ANGLE: {_smoothedTillerAngleDeg:F1}° | TORQUE: {_smoothedTorqueNm:F1} N·m";
            }

            if (uiErgonomicStaminaText != null)
            {
                int reba = forceTracker != null ? forceTracker.RebaErgonomicScore : 4;
                uiErgonomicStaminaText.text = $"REBA SCORE: {reba} | STAMINA: {Mathf.RoundToInt(_smoothedStaminaPercent)}%";
            }
        }

        private void CreateARShaderMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _arAdditiveLineMaterial = new Material(shader)
            {
                name = "AR_Biomechanical_HUD_Mat"
            };
        }

        private void ConfigureLineRenderer(LineRenderer lr, int positionCount, float width, Color color)
        {
            lr.useWorldSpace = true;
            lr.positionCount = positionCount;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 6;
            lr.numCornerVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = _arAdditiveLineMaterial;
        }

        private static TextMesh CreateWorldSpaceTextMesh(
            Transform parent,
            string name,
            float characterSize,
            TextAnchor anchor,
            Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMesh tm = go.AddComponent<TextMesh>();
            tm.characterSize = characterSize;
            tm.fontSize = 36;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Left;
            tm.richText = true;
            tm.color = color;
            return tm;
        }

        private static void BillboardToMainCamera(Transform target)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 dirToCam = target.position - cam.transform.position;
            if (dirToCam.sqrMagnitude > 1e-4f)
            {
                target.rotation = Quaternion.LookRotation(dirToCam.normalized, Vector3.up);
            }
        }

        #endregion
    }
}
