using UnityEngine;

namespace Platformer
{
    /// <summary>本组属于捕获对中的哪一半。</summary>
    public enum CaptureGroupSide
    {
        [InspectorName("A 门")] A,
        [InspectorName("B 门")] B
    }

    /// <summary>门的朝向：朝左或朝右。</summary>
    public enum PortalFacing
    {
        [InspectorName("朝左")] Left,
        [InspectorName("朝右")] Right
    }

    /// <summary>Portal View 当前显示的半屏纹理。</summary>
    public enum PortalViewHalf
    {
        [InspectorName("左半")] Left,
        [InspectorName("右半")] Right
    }

    /// <summary>
    /// 一个捕获显示组：把快照相机、Portal View Center（视觉中心）、输出精灵和捕获控制器打包在同一个组节点下。
    ///
    /// 两组并排放在同一父节点下。每组会自己找到另一侧的那一组并交叉连线：
    /// 本组的相机画到对方的输出上，对方的相机画到本组的输出上。
    ///
    /// 两块画面遮罩把输出裁成门洞的形状，并且始终朝向玩家的眼睛。
    /// </summary>
    public class CaptureDisplayGroup : MonoBehaviour
    {
        [Tooltip("本组是 A 还是 B；会自动和另一侧配对")]
        public CaptureGroupSide side = CaptureGroupSide.A;

        [Tooltip("自动解析出的另一扇门，无需手填")]
        public CaptureDisplayGroup partner;

        [Header("内部引用")]
        [Tooltip("本组的捕获控制器")]
        public RegionSnapshotCamera capture;
        [Tooltip("本组的画面精灵（会被对方那组画上画面）；启动时自动查找 Portal View")]
        public SpriteRenderer output;

        [Tooltip("传送门画面的父节点（视觉中心）；启动时自动查找 Portal View Center")]
        public Transform viewCenter;

        [Tooltip("本门显示精灵所在的游戏对象（开始时自动获取）")]
        public GameObject viewObject;

        [Tooltip("左右两半在中线各让出的像素数。留一点缝可以避免半屏拼接把共用像素列放大成一条亮线")]
        [Range(0f, 16f)] public float centerGapPixels = 1f;

        [Header("另一扇门透明度")]
        [Tooltip("玩家离一扇门小于该距离时，另一扇门线性变透明；距离 0 时完全透明。单位与颜色距离公式一致")]
        [Min(0f)] public float otherViewFadeDistance = 3f;

        [Header("门位置提示对象")]
        [Tooltip("场景中的静态光带 SpriteRenderer；不再运行时创建")]
        public SpriteRenderer hintRenderer;

        [Tooltip("场景中的静态粒子系统；不再运行时创建")]
        public ParticleSystem hintParticles;

        [Tooltip("光带沿门朝向方向的长度（世界单位）")]
        [Min(0.01f)] public float hintBeamLength = 0.45f;

        [Tooltip("细线高度比传送门对的门洞高度多出的世界单位（两端露在门洞外）")]
        public float hintExtraHeight = 0.3f;

        [Tooltip("细线核心宽度（占光带长度的比例）")]
        [Range(0.02f, 0.5f)] public float hintCoreWidth = 0.18f;

        [Tooltip("流光速度")]
        public float hintFlowSpeed = 2.5f;

        [Tooltip("两端高亮宽度（纹理纵向比例）")]
        [Range(0.02f, 0.5f)] public float hintEndWidth = 0.12f;

        [Tooltip("整体透明度")]
        [Range(0f, 1f)] public float hintAlpha = 0.5f;

        // 仅由代码配置，避免已有场景组件的序列化值覆盖此频率。
        [System.NonSerialized] public float hintBreathFrequency = 0.5f;

        [Tooltip("噪音失真强度")]
        [Range(0f, 0.3f)] public float hintNoiseStrength = 0.05f;

        [Tooltip("噪音缩放")]
        [Min(0.01f)] public float hintNoiseScale = 7f;

        [Tooltip("噪音流动速度（纹理两轴持续滚动）")]
        public float hintNoiseSpeed = 0.9f;

        [Tooltip("细线向门朝向反方向（门的背面）偏移的世界单位")]
        public float hintBackOffset = 0.1f;

        [Header("门位置提示粒子")]
        [Tooltip("粒子沿门朝向方向的基础前进速度（实际速度为其 1/4）")]
        public float hintParticleSpeed = 1.8f;

        [Tooltip("粒子发射速率（实际发射率为其 1/4）")]
        [Min(0f)] public float hintParticleRate = 45f;

        [Tooltip("粒子基础存活时间（实际生命周期为其 1.5 倍）")]
        [Min(0.01f)] public float hintParticleLifetime = 1f;

        [Tooltip("粒子发射区域比门洞高度小多少（世界单位）")]
        [Min(0f)] public float hintParticleHeightInset = 0.1f;

        [Header("玩家")]
        [Tooltip("本对传送门绑定的玩家游戏对象，由传送门对下发，无需手填")]
        public GameObject player;

        [Header("传送门")]
        [Tooltip("门的朝向：定义出口在竖直方向的哪一侧；传送时据此决定出面对方向")]
        [SerializeField] PortalFacing facing = PortalFacing.Right;

        public PortalFacing Facing
        {
            get { return facing; }
            set
            {
                if (facing == value) return;
                facing = value;
                _lastFacing = value;
                _hasLastFacing = true;
                ChangeFacing();
            }
        }

        PortalFacing _lastFacing;
        bool _hasLastFacing;

        [Header("遮罩")]
        [Tooltip("上遮罩节点；启动时自动查找 View Mask Top")]
        public Transform maskTop;
        [Tooltip("下遮罩节点；启动时自动查找 View Mask Bottom")]
        public Transform maskBottom;

        [Header("开门动画")]
        [Tooltip("开门动画时长（秒）：上下遮罩偏移从 0 增长到传送门对配置值")]
        [Min(0f)] public float openDuration = 0.6f;

        [Tooltip("开门曲线：0 = 关闭，1 = 完全打开")]
        public AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("连接画面渐变")]
        [Tooltip("单门纹理与双门捕获画面之间的渐变时长；与遮罩开门动画相互独立")]
        [Min(0f)] public float connectionRevealDuration = 0.5f;

        [Tooltip("连接渐变曲线：0 = 单门纹理，1 = 双门捕获画面")]
        public AnimationCurve connectionRevealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("单门程序纹理的噪音缩放")]
        [Min(0.01f)] public float closedNoiseScale = 7f;

        [Tooltip("单门程序纹理的噪音流动速度")]
        public float closedNoiseSpeed = 0.9f;

        [Tooltip("单门程序纹理的纵向流光速度")]
        public float closedFlowSpeed = 2.5f;

        /// <summary>门洞高度，由 PortalPair 统一配置的上下遮罩偏移算出。</summary>
        public float PortalHeight => Pair != null ? Pair.PortalHeight : 3f;

        /// <summary>开门动画已播放次数（调试/测试用，含 OnEnable 的首次播放）。</summary>
        public int OpenAnimationCount { get; private set; }

        /// <summary>门位置提示的渲染器（调试/测试用）。</summary>
        public SpriteRenderer HintRenderer => _hint;

        /// <summary>门位置提示的粒子系统（调试/测试用）。</summary>
        public ParticleSystem HintParticles => _particles;

        /// <summary>细线（光带靠门的一侧）的世界位置；随视觉中心/朝向/偏移实时计算。</summary>
        public Vector3 HintLinePosition
        {
            get
            {
                Vector3 line = ViewAnchor.position + (Vector3)BackDirection * hintBackOffset;
                line.z = transform.position.z;
                return line;
            }
        }

        /// <summary>所属的 PortalPair（必要时向上查找）。</summary>
        public PortalPair Pair
        {
            get
            {
                // 父级被换到另一个 PortalPair 时重新解析，避免缓存指向旧门对。
                if (_pair == null || !transform.IsChildOf(_pair.transform))
                    _pair = GetComponentInParent<PortalPair>();
                return _pair;
            }
        }

        /// <summary>本门朝左时返回 true。</summary>
        public bool FacesLeft => facing == PortalFacing.Left;

        /// <summary>朝左为 -1，朝右为 +1。</summary>
        public float FacingSign => FacesLeft ? -1f : 1f;

        /// <summary>
        /// 开口/朝向向量：把“本地 Y 轴正方向”按 facing 旋转 90°，再变换到世界空间。
        /// 朝右 = (1,0)，朝左 = (-1,0)；门整体旋转时方向也会实时跟着转。
        /// 这是属性而不是字段，每次读取都会重新计算，不做一次性缓存。
        /// </summary>
        public Vector2 OpeningDirection
        {
            get
            {
                Vector3 local = Quaternion.Euler(0f, 0f, FacingSign * -90f) * Vector3.up;
                Vector3 world = transform.TransformDirection(local);
                Vector2 result = world;
                return result.sqrMagnitude > 0.000001f ? result.normalized : Vector2.right;
            }
        }

        /// <summary>门背后方向：开口方向的反向量，实时计算。</summary>
        public Vector2 BackDirection => -OpeningDirection;

        /// <summary>玩家的 Transform（未下发玩家时为 null）。</summary>
        public Transform PlayerTransform => player != null ? player.transform : null;

        /// <summary>
        /// 本门 Portal View 显示对方相机纹理的哪一半。
        /// 默认 A 门右半、B 门左半；可在运行时直接修改。
        /// </summary>
        public PortalViewHalf OutputHalf
        {
            get
            {
                if (!_outputHalfInitialized)
                    return side == CaptureGroupSide.A ? PortalViewHalf.Right : PortalViewHalf.Left;
                return _outputHalf;
            }
            set
            {
                _outputHalf = value;
                _outputHalfInitialized = true;

                // 本门直接改半屏：按新半屏自动对齐 Portal View 的 X。
                ApplyHalfCrop(true, false);
            }
        }

        /// <summary>
        /// 直接设置本门输出使用的半屏纹理。
        /// </summary>
        public void SetOutputHalf(PortalViewHalf half)
        {
            OutputHalf = half;
        }

        /// <summary>
        /// 只替换输出半屏纹理，不移动 Portal View 的 X。
        /// 供 partner 控制 API 使用；本门直接改半屏请用 OutputHalf/SetOutputHalf。
        /// </summary>
        public void SetOutputHalfPreservingX(PortalViewHalf half)
        {
            _outputHalf = half;
            _outputHalfInitialized = true;

            // 还没有裁过半屏时先正常对齐；已经裁过才保留当前 X。
            ApplyHalfCrop(true, _hasAppliedCrop);
        }

        /// <summary>另一扇门（partner）的输出 SpriteRenderer；partner 未解析或没有输出时为 null。</summary>
        public SpriteRenderer PartnerOutput => partner != null ? partner.output : null;

        /// <summary>另一扇门（partner）的输出 GameObject；没有输出时为 null。</summary>
        public GameObject PartnerOutputObject => PartnerOutput != null ? PartnerOutput.gameObject : null;

        /// <summary>
        /// 另一扇门（partner）当前输出使用的半屏纹理。
        /// 在 A 上设置它，就是控制 B 的 Portal View 使用 A 相机的哪半张纹理。
        /// </summary>
        public PortalViewHalf PartnerOutputHalf
        {
            get
            {
                if (partner != null) return partner.OutputHalf;
                if (_pendingPartnerHalf.HasValue) return _pendingPartnerHalf.Value;
                return side == CaptureGroupSide.A ? PortalViewHalf.Left : PortalViewHalf.Right;
            }
            set
            {
                SetPartnerOutputHalf(value);
            }
        }

        /// <summary>
        /// 直接控制另一扇门（partner）的输出使用哪一半纹理。
        /// 例如在 A 的 ChangeFacing 中调用 SetPartnerOutputHalf(...) 修改 B 的输出。
        /// 如果调用时 partner 尚未解析，会先缓存，等 ResolvePartner 后自动应用。
        /// </summary>
        public void SetPartnerOutputHalf(PortalViewHalf half)
        {
            _pendingPartnerHalf = half;
            if (partner != null)
            {
                partner.SetOutputHalfPreservingX(half);
                _pendingPartnerHalf = null;
            }
        }

        /// <summary>
        /// 明确的语义别名：只修改 partner 的输出，不修改自己。
        /// 在 A 上调用 SetPartnerCameraHalf(Right)，表示让 B 使用 A 相机的右半张纹理。
        /// </summary>
        public void SetPartnerCameraHalf(PortalViewHalf half)
        {
            SetPartnerOutputHalf(half);
        }

        /// <summary>门的视觉中心：优先用 Portal View Center，否则退回组根节点。</summary>
        public Transform ViewAnchor => viewCenter != null
            ? viewCenter
            : (output != null && output.transform.parent != null ? output.transform.parent : transform);

        Sprite _fullSprite;
        Sprite _halfSprite;
        PortalViewHalf _outputHalf;
        bool _outputHalfInitialized;
        bool _hasAppliedCrop;
        PortalViewHalf? _pendingPartnerHalf;
        PortalPair _pair;
        Transform _viewAnchor;
        float _viewAlpha = 1f;
        SpriteRenderer _hint;
        ParticleSystem _particles;
        Material _beamMaterial;
        Material _viewMaterial;

        static readonly Color HintBlue = new Color(0.35f, 0.65f, 1f, 1f);
        static readonly Color HintRed = new Color(1f, 0.35f, 0.35f, 1f);
        static readonly int ConnectionRevealId = Shader.PropertyToID("_ConnectionReveal");
        static readonly int OpenBrightnessId = Shader.PropertyToID("_OpenBrightness");
        static readonly int PortalColorId = Shader.PropertyToID("_PortalColor");
        static readonly int ClosedNoiseScaleId = Shader.PropertyToID("_ClosedNoiseScale");
        static readonly int ClosedNoiseSpeedId = Shader.PropertyToID("_ClosedNoiseSpeed");
        static readonly int ClosedFlowSpeedId = Shader.PropertyToID("_ClosedFlowSpeed");
        const string PortalLightBandLayer = "Portal light band";
        static Texture2D _hintTexture;
        static Sprite _hintSprite;
        static Material _particleMaterial;

        Vector3 _viewBaseLocalPosition;
        bool _hasViewBase;

        float _openElapsed;
        float _openProgress = 1f;
        bool _isOpening;

        float _connectionReveal;
        float _connectionRevealElapsed;
        float _connectionRevealStart;
        float _connectionRevealTarget;
        bool _isConnectionTransitioning;

        void Awake()
        {
            _lastFacing = facing;
            _hasLastFacing = true;

            // 先解析本组自己的 View 结构，WireUp 需要 output 已就位。
            ResolveViewRig();
            EnsureViewMaterial();

            // Pair 可能已提前处理未激活门并完成首次裁切；此时必须保留原始基准，
            // 不能把已经叠加过半屏偏移的局部位置重新记为基准。
            if (output != null && !_hasViewBase)
            {
                _viewBaseLocalPosition = output.transform.localPosition;
                _hasViewBase = true;
            }

            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            CachePlayer();
            CacheViewAnchors();
            EnsureHint();
        }

        void OnEnable()
        {
            PortalPair pair = Pair;
            if (pair != null)
            {
                pair.SetPortalTerrainState(this, true);
                pair.NotifyPortalStateChanged();
            }

            // 首次激活、以及禁用后重新启用 GameObject/组件时都会触发，重播开门动画。
            // 已激活的 partner 不会因此重播；双门连接渐变由 PortalPair 单独触发。
            PlayOpenAnimation();

            // playOnAwake=false 的粒子系统不会随物体重新启用而自动恢复播放。
            if (_particles != null) _particles.Play(true);
        }

        void OnDisable()
        {
            SetConnectionClosedImmediate();

            PortalPair pair = Pair;
            if (pair != null)
            {
                pair.SetPortalTerrainState(this, false);
                pair.NotifyPortalStateChanged();
            }
        }

        void Start()
        {
            // 启动时再解析一次，兼容运行时动态创建/重命名的子对象。
            ResolveViewRig();
            EnsureViewMaterial();

            // 获取本门显示精灵所在的游戏对象，并记住它在视觉中心下的初始局部位置。
            // ApplyHalfCrop 只在此基础上叠加 X 偏移，不能把 Portal View Center 的 Z 偏移覆盖掉。
            if (output != null)
            {
                viewObject = output.gameObject;

                if (viewCenter == null)
                    viewCenter = output.transform.parent;

                if (!_hasViewBase)
                {
                    _viewBaseLocalPosition = output.transform.localPosition;
                    _hasViewBase = true;
                }

                // 传送门相机排除 Portal View 所在层，避免把自己的输出拍进画面形成递归。
                if (capture != null && capture.captureCamera != null && output.gameObject.layer != 0)
                    capture.captureCamera.cullingMask &= ~(1 << output.gameObject.layer);
            }

            ChangeFacing();

            // 启动时强制按最终尺寸对齐一次，避免首帧拿到半屏后不再重建。
            ApplyHalfCrop(true, false);

            CachePlayer();
            CacheViewAnchors();
            EnsureHint();
        }

        /// <summary>
        /// Inspector 直接修改序列化字段 facing，不会执行 Facing 属性的 setter。
        /// 缓存上一次朝向，确保只有值实际变化时才调用 ChangeFacing；首次加载只记录初始值。
        /// </summary>
        void OnValidate()
        {
            if (!_hasLastFacing)
            {
                _lastFacing = facing;
                _hasLastFacing = true;
                return;
            }

            if (_lastFacing == facing) return;

            _lastFacing = facing;
            ChangeFacing();
        }

        void ChangeFacing()
        {
            if (partner == null) ResolvePartner();

            CaptureDisplayGroup groupA = side == CaptureGroupSide.A ? this : partner;
            CaptureDisplayGroup groupB = side == CaptureGroupSide.B ? this : partner;
            ApplyFacingStates(groupA, groupB);
        }

        /// <summary>按 A/B 的四种朝向组合，直接设置两扇门的全部视觉状态。</summary>
        public static void ApplyFacingStates(CaptureDisplayGroup groupA, CaptureDisplayGroup groupB)
        {
            if (groupA == null || groupB == null) return;

            if (groupA.side != CaptureGroupSide.A || groupB.side != CaptureGroupSide.B)
            {
                var temp = groupA;
                groupA = groupB;
                groupB = temp;
            }


            groupA.UpdateHint();
            groupB.UpdateHint();

            if (groupA.maskTop == null || groupA.maskBottom == null ||
                groupA.viewCenter == null || groupA.output == null ||
                groupB.maskTop == null || groupB.maskBottom == null ||
                groupB.viewCenter == null || groupB.output == null)
                return;

            // A 左，B 左。
            if (groupA.FacesLeft && groupB.FacesLeft)
            {
                groupA.maskTop.localScale = groupA.maskBottom.localScale = new Vector3(1f, -1f, 1f);
                groupA.viewCenter.localScale = new Vector3(-1f, 1f, 1f);
                groupA.output.flipX = false;
                groupA.output.flipY = false;
                groupA.SetOutputHalf(PortalViewHalf.Left);

                groupB.maskTop.localScale = groupB.maskBottom.localScale = new Vector3(1f, -1f, 1f);
                groupB.viewCenter.localScale = new Vector3(-1f, -1f, 1f);
                groupB.output.flipX = false;
                groupB.output.flipY = true;
                groupB.SetOutputHalf(PortalViewHalf.Left);
                return;
            }

            // A 左，B 右。
            if (groupA.FacesLeft && !groupB.FacesLeft)
            {
                groupA.maskTop.localScale = groupA.maskBottom.localScale = new Vector3(1f, -1f, 1f);
                groupA.viewCenter.localScale = new Vector3(1f, 1f, 1f);
                groupA.output.flipX = false;
                groupA.output.flipY = false;
                groupA.SetOutputHalf(PortalViewHalf.Right);

                groupB.maskTop.localScale = groupB.maskBottom.localScale = new Vector3(1f, 1f, 1f);
                groupB.viewCenter.localScale = new Vector3(1f, -1f, 1f);
                groupB.output.flipX = false;
                groupB.output.flipY = true;
                groupB.SetOutputHalf(PortalViewHalf.Left);
                return;
            }

            // A 右，B 左。
            if (!groupA.FacesLeft && groupB.FacesLeft)
            {
                groupA.maskTop.localScale = groupA.maskBottom.localScale = new Vector3(-1f, 1f, 1f);
                groupA.viewCenter.localScale = new Vector3(1f, -1f, 1f);
                groupA.output.flipX = false;
                groupA.output.flipY = true;
                groupA.SetOutputHalf(PortalViewHalf.Left);

                groupB.maskTop.localScale = groupB.maskBottom.localScale = new Vector3(1f, -1f, 1f);
                groupB.viewCenter.localScale = new Vector3(1f, -1f, 1f);
                groupB.output.flipX = false;
                groupB.output.flipY = true;
                groupB.SetOutputHalf(PortalViewHalf.Right);
                return;
            }

            // A 右，B 右。
            if (!groupA.FacesLeft && !groupB.FacesLeft)
            {
                groupA.maskTop.localScale = groupA.maskBottom.localScale = new Vector3(-1f, 1f, 1f);
                groupA.viewCenter.localScale = new Vector3(-1f, -1f, 1f);
                groupA.output.flipX = false;
                groupA.output.flipY = true;
                groupA.SetOutputHalf(PortalViewHalf.Right);

                groupB.maskTop.localScale = groupB.maskBottom.localScale = new Vector3(1f, 1f, 1f);
                groupB.viewCenter.localScale = new Vector3(-1f, -1f, 1f);
                groupB.output.flipX = false;
                groupB.output.flipY = true;
                groupB.SetOutputHalf(PortalViewHalf.Right);
            }
        }

        /// <summary>
        /// 立即停止当前开门动画，并把门保持在完全打开状态。
        /// 传送时先对所有门调用，再重新选择需要重播动画的门。
        /// </summary>
        public void StopOpenAnimation()
        {
            _isOpening = false;
            _openElapsed = 0f;
            _openProgress = 1f;
            ApplyMaskOffsets(1f);
        }

        /// <summary>
        /// 播放一次开门动画：上下遮罩偏移在 openDuration 内从 0 增长到 PortalPair 配置的偏移。
        /// 可通过 openCurve 调整节奏；时长为 0 时直接完全打开。
        /// </summary>
        public void PlayOpenAnimation()
        {
            OpenAnimationCount++;
            _openElapsed = 0f;
            _isOpening = openDuration > 0f;
            _openProgress = _isOpening ? 0f : 1f;

            // 立刻把遮罩拉回关闭状态（偏移 0），
            // 这样重新启用时不会先看到上一帧的打开位置，动画一定从 0 开始。
            ApplyMaskOffsets(_openProgress);
        }

        void AdvanceOpenAnimation()
        {
            if (!_isOpening) return;

            _openElapsed += Time.deltaTime;

            float t = openDuration > 0f ? Mathf.Clamp01(_openElapsed / openDuration) : 1f;
            _openProgress = openCurve != null ? Mathf.Clamp01(openCurve.Evaluate(t)) : t;

            if (t >= 1f)
            {
                _isOpening = false;
                _openProgress = 1f;
            }
        }

        /// <summary>从当前画面状态渐变到双门捕获画面，不影响遮罩开门动画。</summary>
        public void PlayConnectionReveal()
        {
            PlayConnectionTransition(1f);
        }

        /// <summary>从当前画面状态渐变回单门程序纹理。</summary>
        public void PlayConnectionClose()
        {
            PlayConnectionTransition(0f);
        }

        /// <summary>立即恢复为单门程序纹理，供门停用或门对重置时调用。</summary>
        public void SetConnectionClosedImmediate()
        {
            _connectionReveal = 0f;
            _connectionRevealElapsed = 0f;
            _connectionRevealStart = 0f;
            _connectionRevealTarget = 0f;
            _isConnectionTransitioning = false;
            ApplyViewMaterialProperties();
        }

        /// <summary>传送发生时立即完成连接渐显，直接显示正常捕获画面。</summary>
        public void CompleteConnectionReveal()
        {
            _connectionReveal = 1f;
            _connectionRevealElapsed = 0f;
            _connectionRevealStart = 1f;
            _connectionRevealTarget = 1f;
            _isConnectionTransitioning = false;
            ApplyViewMaterialProperties();
        }

        void PlayConnectionTransition(float target)
        {
            if (_isConnectionTransitioning && Mathf.Approximately(_connectionRevealTarget, target))
                return;
            if (!_isConnectionTransitioning && Mathf.Approximately(_connectionReveal, target))
                return;

            _connectionRevealStart = _connectionReveal;
            _connectionRevealTarget = target;
            _connectionRevealElapsed = 0f;
            _isConnectionTransitioning = connectionRevealDuration > 0f &&
                                         !Mathf.Approximately(_connectionRevealStart, target);

            if (_isConnectionTransitioning) return;

            _connectionReveal = target;
            ApplyViewMaterialProperties();
        }

        void AdvanceConnectionTransition()
        {
            if (!_isConnectionTransitioning) return;

            _connectionRevealElapsed += Time.deltaTime;
            float t = connectionRevealDuration > 0f
                ? Mathf.Clamp01(_connectionRevealElapsed / connectionRevealDuration)
                : 1f;
            float eased = connectionRevealCurve != null
                ? Mathf.Clamp01(connectionRevealCurve.Evaluate(t))
                : t;
            _connectionReveal = Mathf.Lerp(_connectionRevealStart, _connectionRevealTarget, eased);

            if (t < 1f) return;

            _isConnectionTransitioning = false;
            _connectionReveal = _connectionRevealTarget;
        }

        void EnsureViewMaterial()
        {
            if (output == null) return;

            if (_viewMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Platformer/PortalView");
                if (shader == null)
                {
                    Debug.LogWarning("[CaptureDisplayGroup] 找不到 PortalView Shader。", this);
                    return;
                }

                _viewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
            }

            // 每扇门持有独立材质，连接进度和 A/B 颜色不会互相覆盖。
            output.sharedMaterial = _viewMaterial;
            ApplyViewMaterialProperties();
        }

        void ApplyViewMaterialProperties()
        {
            if (_viewMaterial == null) return;

            _viewMaterial.SetFloat(ConnectionRevealId, Mathf.Clamp01(_connectionReveal));
            _viewMaterial.SetFloat(OpenBrightnessId, Mathf.Clamp01(_openProgress));
            _viewMaterial.SetColor(PortalColorId, side == CaptureGroupSide.A ? HintBlue : HintRed);
            _viewMaterial.SetFloat(ClosedNoiseScaleId, Mathf.Max(0.01f, closedNoiseScale));
            _viewMaterial.SetFloat(ClosedNoiseSpeedId, closedNoiseSpeed);
            _viewMaterial.SetFloat(ClosedFlowSpeedId, closedFlowSpeed);
        }

        /// <summary>接收 PortalPair 下发的玩家实例。</summary>
        public void SetPlayer(GameObject playerObject)
        {
            player = playerObject;
        }

        /// <summary>玩家尚未下发时，从所属的 PortalPair 上取一次。</summary>
        void CachePlayer()
        {
            if (player != null) return;

            if (_pair == null) _pair = GetComponentInParent<PortalPair>();
            if (_pair != null && _pair.player != null)
                player = _pair.player.gameObject;
        }

        /// <summary>缓存本门视觉中心，避免每帧重复解析层级。</summary>
        void CacheViewAnchors()
        {
            _viewAnchor = ViewAnchor;
        }

        static Sprite HintSprite
        {
            get
            {
                if (_hintSprite != null) return _hintSprite;

                _hintTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.DontSave
                };
                _hintTexture.SetPixel(0, 0, Color.white);
                _hintTexture.Apply();

                _hintSprite = Sprite.Create(
                    _hintTexture, new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f), 1f);
                _hintSprite.hideFlags = HideFlags.DontSave;
                return _hintSprite;
            }
        }

        /// <summary>
        /// 在门的背面创建一条细长的位置提示：高度跟随 PortalPair 门洞高度，
        /// 颜色 A 蓝 / B 红，排序在 Portal View 之下（主相机里被输出盖住），
        /// 但位于 Default 层，传送门相机仍能捕获。
        /// </summary>
        void EnsureHint()
        {
            // 光带是场景里的静态对象；这里只取引用并运行时换材质，不再 new GameObject。
            _hint = hintRenderer;
            if (_hint == null)
            {
                Transform existing = transform.Find("Portal Hint");
                if (existing != null) _hint = existing.GetComponent<SpriteRenderer>();
            }

            if (_hint == null)
            {
                Debug.LogWarning("[CaptureDisplayGroup] 没有指定静态光带 hintRenderer。", this);
                return;
            }

            if (_hint.sprite == null) _hint.sprite = HintSprite;
            _hint.color = Color.white;
            _hint.maskInteraction = SpriteMaskInteraction.None; // 不被门遮罩裁掉

            // 所有光带相关渲染资源统一使用专用排序层。
            _hint.sortingLayerName = PortalLightBandLayer;

            Shader beamShader = Shader.Find("Hidden/Platformer/PortalHintBeam");
            if (beamShader != null)
            {
                // Awake/Start 和重复初始化共用同一份运行时材质，避免丢失旧引用。
                if (_beamMaterial == null)
                {
                    _beamMaterial = new Material(beamShader)
                    {
                        hideFlags = HideFlags.DontSave
                    };
                }
                _beamMaterial.SetColor("_Color", side == CaptureGroupSide.A ? HintBlue : HintRed);
                _beamMaterial.SetFloat("_Direction", FacingSign);
                _beamMaterial.SetFloat("_CoreWidth", hintCoreWidth);
                _beamMaterial.SetFloat("_FlowSpeed", hintFlowSpeed);
                _beamMaterial.SetFloat("_EndWidth", hintEndWidth);
                _beamMaterial.SetFloat("_Alpha", hintAlpha);
                _beamMaterial.SetFloat("_BreathFrequency", hintBreathFrequency);
                _beamMaterial.SetFloat("_NoiseStrength", hintNoiseStrength);
                _beamMaterial.SetFloat("_NoiseScale", hintNoiseScale);
                _beamMaterial.SetFloat("_NoiseSpeed", hintNoiseSpeed);
                _hint.material = _beamMaterial;
            }

            EnsureHintParticles();
            UpdateHint();
        }

        /// <summary>门位置提示的粒子：直接使用场景中的静态 ParticleSystem，仅在运行时配置效果。</summary>
        void EnsureHintParticles()
        {
            _particles = hintParticles;
            if (_particles == null)
            {
                Debug.LogWarning("[CaptureDisplayGroup] 没有指定静态粒子系统 hintParticles。", this);
                return;
            }

            var main = _particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = hintParticleLifetime * 1.5f;
            main.startSpeed = 0f;
            main.startSize = 0.05f;
            main.maxParticles = 256;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = new ParticleSystem.MinMaxGradient(
                side == CaptureGroupSide.A ? HintBlue : HintRed);

            var emission = _particles.emission;
            emission.rateOverTime = hintParticleRate * 0.25f;

            var shape = _particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                0.02f,
                Mathf.Max(0.01f, (Pair != null ? Pair.PortalHeight : 3f) - hintParticleHeightInset),
                0.1f);

            var velocity = _particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = FacingSign * hintParticleSpeed * 0.25f;
            velocity.y = 0f;
            velocity.z = 0f;

            var colorOverLifetime = _particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            var renderer = _particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.material = ParticleMaterial;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sortingLayerName = PortalLightBandLayer;
                renderer.maskInteraction = SpriteMaskInteraction.None;
            }

            _particles.Play(true);
        }

        static Material ParticleMaterial
        {
            get
            {
                if (_particleMaterial != null) return _particleMaterial;

                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) return null;

                _particleMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
                _particleMaterial.mainTexture = Texture2D.whiteTexture;
                return _particleMaterial;
            }
        }

        /// <summary>只更新光带材质参数；静态光带和粒子的相对位置/层级完全不改。</summary>
        void UpdateHint()
        {
            if (_hint == null) return;

            // 视觉中心引用可能被动态替换，这里重新取一次，避免使用旧的缓存。
            _viewAnchor = ViewAnchor;

            if (_beamMaterial != null)
            {
                _beamMaterial.SetFloat("_Direction", FacingSign);
                _beamMaterial.SetFloat("_CoreWidth", hintCoreWidth);
                _beamMaterial.SetFloat("_FlowSpeed", hintFlowSpeed);
                _beamMaterial.SetFloat("_EndWidth", hintEndWidth);
                _beamMaterial.SetFloat("_Alpha", hintAlpha);
                _beamMaterial.SetFloat("_BreathFrequency", hintBreathFrequency);
                _beamMaterial.SetFloat("_NoiseStrength", hintNoiseStrength);
                _beamMaterial.SetFloat("_NoiseScale", hintNoiseScale);
                _beamMaterial.SetFloat("_NoiseSpeed", hintNoiseSpeed);
            }

            if (_particles != null)
            {
                var velocity = _particles.velocityOverLifetime;
                velocity.x = FacingSign * hintParticleSpeed * 0.25f;
            }
        }

        void OnDestroy()
        {
            if (_halfSprite != null) Destroy(_halfSprite);
            if (_beamMaterial != null) Destroy(_beamMaterial);
            if (_viewMaterial != null) Destroy(_viewMaterial);
        }

        /// <summary>在 Scene 视图里实时画出开口方向（蓝/红）和门背后方向（灰）。</summary>
        void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;
            Vector2 opening = OpeningDirection;

            Gizmos.color = side == CaptureGroupSide.A
                ? new Color(0.35f, 0.65f, 1f, 1f)
                : new Color(1f, 0.35f, 0.35f, 1f);
            Gizmos.DrawRay(origin, opening * 1.5f);
            Gizmos.DrawWireSphere(origin + (Vector3)opening * 1.5f, 0.06f);

            Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            Gizmos.DrawRay(origin, -opening * 1.5f);
        }

        void LateUpdate()
        {
            AdvanceOpenAnimation();
            AdvanceConnectionTransition();
            ApplyHalfCrop();
            UpdateMasks();
            UpdateHint();
            ApplyViewMaterialProperties();
        }

        void Update()
        {
            CachePlayer();
            CacheViewAnchors();
            UpdateViewColor();
            UpdateVisibility();
        }

        /// <summary>
        /// 用 Y 轴正方向按 Facing 旋转 90° 得到开口方向（朝右 -90°，朝左 +90°）。
        /// 玩家相对门的向量与开口方向的夹角 ≥ 90° 时，说明玩家绕到了门后面，隐藏本门画面。
        /// </summary>
        void UpdateVisibility()
        {
            if (output == null) return;

            CachePlayer();
            if (player == null)
            {
                output.enabled = true;
                return;
            }

            Vector3 openingDirection = OpeningDirection;
            Vector3 anchor = _viewAnchor != null ? _viewAnchor.position : transform.position;
            Vector3 toPlayer = player.transform.position - anchor;

            // 点积 <= 0 等价于夹角 >= 90°，此时玩家在门的背面。
            output.enabled = Vector3.Dot(openingDirection, toPlayer) > 0f;
        }

        /// <summary>接收 PortalPair 的统一仲裁结果并立即应用，不在本组内决定透明目标。</summary>
        internal void SetViewAlpha(float alpha)
        {
            _viewAlpha = Mathf.Clamp01(alpha);
            UpdateViewColor();
        }

        /// <summary>
        /// 本门距离只决定 RGB 冷暖；alpha 由 PortalPair 下发，开门进度只影响 RGB 明暗。
        /// </summary>
        void UpdateViewColor()
        {
            if (output == null) return;

            float color_offs = 0.5f;
            if (player != null)
            {
                Vector3 playerPosition = player.transform.position;

                // 玩家到本门视觉中心局部 Y 轴的垂直距离。
                Transform ownAnchor = _viewAnchor != null ? _viewAnchor : transform;
                float playerDistanceToYAxis = Mathf.Abs(ownAnchor.InverseTransformPoint(playerPosition).x);

                if (playerDistanceToYAxis <= 5f)
                    color_offs = playerDistanceToYAxis / 10f;
            }

            output.color = side == CaptureGroupSide.B
                ? new Color(1f, 1f - color_offs, 1f - color_offs, _viewAlpha)
                : new Color(1f - color_offs, 1f - color_offs, 1f, _viewAlpha);

            // 开门动画的明暗只在这里乘一次，避免 ApplyMaskOffsets 反复乘导致颜色越来越黑。
            float open = Mathf.Clamp01(_openProgress);
            Color color = output.color;
            color.r *= open;
            color.g *= open;
            color.b *= open;
            output.color = color;
        }

        /// <summary>门的碰撞转发给 PortalPair，由它统一处理传送。</summary>
        void OnCollisionEnter2D(Collision2D collision)
        {
            ForwardGateCollision(collision);
        }

        /// <summary>冷却结束时如果仍贴着门，也要能触发。</summary>
        void OnCollisionStay2D(Collision2D collision)
        {
            ForwardGateCollision(collision);
        }

        void ForwardGateCollision(Collision2D collision)
        {
            PortalPair pair = Pair;
            if (pair != null) pair.OnGateHit(this, collision);
        }

        /// <summary>在兄弟节点中找到另一侧的组，并把两者连接起来。</summary>
        public void ResolvePartner()
        {
            if (partner != null && partner.side != side) return;

            CaptureDisplayGroup[] candidates = transform.parent != null
                ? transform.parent.GetComponentsInChildren<CaptureDisplayGroup>(true)
                : FindObjectsOfType<CaptureDisplayGroup>();

            partner = null;
            foreach (var candidate in candidates)
            {
                if (candidate == this) continue;
                if (candidate.side == side) continue;

                partner = candidate;
                break;
            }

            // ChangeFacing/OnValidate 可能早于 partner 解析；把之前缓存的控制指令补发。
            if (partner != null && _pendingPartnerHalf.HasValue)
            {
                partner.SetOutputHalfPreservingX(_pendingPartnerHalf.Value);
                _pendingPartnerHalf = null;
            }
        }

        /// <summary>双向连线：每组的相机都画到另一组的输出上。</summary>
        public void WireUp()
        {
            if (capture == null || output == null) return;

            // 没有可用 partner 时仍保留自己的输出引用，确保 RegionSnapshotCamera
            // 能初始化 Sprite 网格；单门关闭纹理也依赖这个网格进行渲染。
            if (partner == null || partner.capture == null || partner.output == null)
            {
                capture.output = output;
                return;
            }

            capture.output = partner.output;
            partner.capture.output = output;
        }

        /// <summary>
        /// 启动时自动解析本组自己的 View 结构：
        /// Portal View Center → Portal View → Portal Mask/View Mask Top/Bottom。
        /// 也可以在 Inspector 右键菜单里手动重新查找。
        /// </summary>
        [ContextMenu("重新查找 Portal View 结构")]
        public void ResolveViewRig()
        {
            Transform foundCenter = FindDescendant(transform, "Portal View Center");
            if (foundCenter != null) viewCenter = foundCenter;

            Transform viewRoot = viewCenter != null ? viewCenter : transform;
            Transform foundView = FindDescendant(viewRoot, "Portal View");
            if (foundView == null && viewRoot != transform)
                foundView = FindDescendant(transform, "Portal View");

            if (foundView != null)
            {
                viewObject = foundView.gameObject;
                SpriteRenderer foundRenderer = foundView.GetComponent<SpriteRenderer>();
                if (foundRenderer != null) output = foundRenderer;
            }
            else if (output != null)
            {
                viewObject = output.gameObject;
            }

            if (viewCenter == null && output != null)
                viewCenter = output.transform.parent;

            Transform maskRoot = FindDescendant(transform, "Portal Mask");
            if (maskRoot == null) maskRoot = transform;

            Transform foundTop = FindDescendant(maskRoot, "View Mask Top");
            Transform foundBottom = FindDescendant(maskRoot, "View Mask Bottom");
            if (foundTop == null && maskRoot != transform)
                foundTop = FindDescendant(transform, "View Mask Top");
            if (foundBottom == null && maskRoot != transform)
                foundBottom = FindDescendant(transform, "View Mask Bottom");

            if (foundTop != null) maskTop = foundTop;
            if (foundBottom != null) maskBottom = foundBottom;

            if (viewCenter == null)
                Debug.LogWarning($"[CaptureDisplayGroup] {name} 未找到 Portal View Center。", this);
            if (output == null)
                Debug.LogWarning($"[CaptureDisplayGroup] {name} 未找到 Portal View（SpriteRenderer）。", this);
            if (maskTop == null || maskBottom == null)
                Debug.LogWarning($"[CaptureDisplayGroup] {name} 缺少 View Mask Top/Bottom。", this);
        }

        static Transform FindDescendant(Transform root, string childName)
        {
            if (root == null) return null;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in all)
            {
                if (item != root && item.name == childName)
                    return item;
            }
            return null;
        }

        /// <summary>兼容旧调用：解析遮罩节点（现在会同时解析整个 View 结构）。</summary>
        public void ResolveMaskRig()
        {
            ResolveViewRig();
        }

        /// <summary>
        /// 把两块遮罩放回各自的偏移位置，并转向玩家的眼睛，
        /// 让门洞始终正对玩家。
        /// </summary>
        public void UpdateMasks()
        {
            if (maskTop == null || maskBottom == null) return;

            CachePlayer();
            ApplyMaskOffsets(_openProgress);

            if (player == null) return;

            // 眼睛位置 = 玩家中心 + 门对配置的眼睛高度。
            PortalPair pair = Pair;
            float eyeOffset = pair != null ? pair.playerEyeOffset : 0.4f;
            Vector3 eye = player.transform.position + Vector3.up * eyeOffset;
            FacePoint(maskTop, eye);
            FacePoint(maskBottom, eye);
        }

        /// <summary>按 0..1 的开门比例设置上下遮罩的 Y 偏移（0 = 关闭，1 = 完全打开）。</summary>
        void ApplyMaskOffsets(float progress)
        {
            if (maskTop == null || maskBottom == null) return;

            PortalPair pair = Pair;
            float open = Mathf.Clamp01(progress);
            float topOffset = (pair != null ? pair.maskTopOffset : 1.5f) * open;
            float bottomOffset = (pair != null ? pair.maskBottomOffset : -1.5f) * open;

            maskTop.localPosition = new Vector3(0f, topOffset, 0f);
            maskBottom.localPosition = new Vector3(0f, bottomOffset, 0f);
        }

        static void FacePoint(Transform mask, Vector3 point)
        {
            Vector3 direction = point - mask.position;
            // 先对准眼睛，再旋转 90 度。
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            mask.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// 只显示捕获画面的一半，并把它摆到完整画面中对应的位置。
        /// 快照自己的 Sprite 保留全尺寸网格，半屏和中心缝由 Shader UV 处理。
        /// </summary>
        public void ApplyHalfCrop()
        {
            ApplyHalfCrop(false, false);
        }

        void ApplyHalfCrop(bool force, bool preserveX)
        {
            if (output == null) return;

            // 保留完整的捕获 Sprite：切到另一半时不能以已经裁过的半屏为源。
            Sprite current = output.sprite;
            if (current != null && current != _halfSprite)
                _fullSprite = current;

            Sprite full = _fullSprite != null ? _fullSprite : current;
            if (full == null) return;

            float fullWidth = full.bounds.size.x;
            float pixelsPerUnit = full.pixelsPerUnit;

            // 资源重建的瞬间可能拿到零尺寸 Sprite；此时不要用它把 X 摆到 0。
            if (fullWidth <= 0.0001f || pixelsPerUnit <= 0.0001f)
                return;

            // 默认 A 保留右半、B 保留左半；可通过 OutputHalf 直接切换。
            // 两半都在中线处留出一点缝隙，避免交叉显示时出现亮线。
            bool rightHalf = OutputHalf == PortalViewHalf.Right;
            float halfPixels = full.rect.width * 0.5f;
            float gapPixels = Mathf.Clamp(centerGapPixels, 0f, halfPixels * 0.5f);
            RegionSnapshotCamera source = FindOutputCapture();
            bool usePersistentCapture = source != null && full == source.CaptureSprite &&
                                        source.CapturedTexture != null;

            if (usePersistentCapture)
            {
                if (_halfSprite != null)
                {
                    Destroy(_halfSprite);
                    _halfSprite = null;
                }

                output.sprite = full;
                float gapUv = gapPixels / full.rect.width;
                float edgeUv = 0.25f + gapUv * 0.5f;
                var uvTransform = new Vector4(1f, 1f, rightHalf ? edgeUv : -edgeUv, 0f);
                var uvClip = new Vector4(edgeUv, 0f, 0.75f - gapUv * 0.5f, 1f);
                source.SetOutputCapture(source.CapturedTexture, uvTransform, uvClip);
            }
            else
            {
                // 外部手动指定 Sprite 时保留旧路径，避免改变普通 RegionSnapshotCamera 的用途。
                if (!force && output.sprite == _halfSprite) return;

                if (source != null)
                    source.SetOutputCapture(full.texture, new Vector4(1f, 1f, 0f, 0f),
                        new Vector4(0f, 0f, 1f, 1f));

                var halfRect = new Rect(
                    rightHalf ? full.rect.x + halfPixels + gapPixels : full.rect.x,
                    full.rect.y,
                    halfPixels - gapPixels,
                    full.rect.height);

                if (_halfSprite != null) Destroy(_halfSprite);
                _halfSprite = Sprite.Create(full.texture, halfRect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
                _halfSprite.name = "Snapshot Half";
                output.sprite = _halfSprite;
            }

            // 把保留的区域摆到它在完整画面中的位置。
            float gapWorld = gapPixels / pixelsPerUnit;
            float offset = (fullWidth * 0.25f + gapWorld * 0.5f) * (rightHalf ? 1f : -1f);

            if (!_hasViewBase)
            {
                _viewBaseLocalPosition = output.transform.localPosition;
                _hasViewBase = true;
            }

            // 换输出半屏时保留当前 X，只改纹理；尺寸变化/正常刷新才重新摆位。
            if (!preserveX)
            {
                // 在 Portal View 的初始局部位置（可能带有 Portal View Center 的 Z 偏移）上叠加半屏 X 偏移。
                output.transform.localPosition = _viewBaseLocalPosition + new Vector3(offset, 0f, 0f);
            }

            _hasAppliedCrop = true;
        }

        RegionSnapshotCamera FindOutputCapture()
        {
            if (partner != null && partner.capture != null && partner.capture.output == output)
                return partner.capture;
            if (capture != null && capture.output == output) return capture;
            return null;
        }
    }
}
