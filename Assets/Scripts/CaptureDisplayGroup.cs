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
        [Tooltip("本组的画面精灵（会被对方那组画上画面）")]
        public SpriteRenderer output;

        [Tooltip("传送门画面的父节点（视觉中心）；留空时自动取输出精灵的父节点")]
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
        [Tooltip("上遮罩节点（自动查找 View Mask Top）")]
        public Transform maskTop;
        [Tooltip("下遮罩节点（自动查找 View Mask Bottom）")]
        public Transform maskBottom;

        [Header("开门动画")]
        [Tooltip("开门动画时长（秒）：上下遮罩偏移从 0 增长到传送门对配置值")]
        [Min(0f)] public float openDuration = 0.6f;

        [Tooltip("开门曲线：0 = 关闭，1 = 完全打开")]
        public AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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
                Vector3 line = ViewAnchor.position;
                line.x -= FacingSign * hintBackOffset;
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

        /// <summary>门的视觉中心：优先用 Portal View Center，否则退回组根节点。</summary>
        Transform ViewAnchor => viewCenter != null
            ? viewCenter
            : (output != null && output.transform.parent != null ? output.transform.parent : transform);

        Sprite _halfSprite;
        PortalPair _pair;
        Transform _viewAnchor;
        Transform _partnerViewAnchor;
        SpriteRenderer _hint;
        ParticleSystem _particles;
        Material _beamMaterial;

        static readonly Color HintBlue = new Color(0.35f, 0.65f, 1f, 1f);
        static readonly Color HintRed = new Color(1f, 0.35f, 0.35f, 1f);
        const string PortalLightBandLayer = "Portal light band";
        static Texture2D _hintTexture;
        static Sprite _hintSprite;
        static Material _particleMaterial;

        Vector3 _viewBaseLocalPosition;
        bool _hasViewBase;

        float _openElapsed;
        float _openProgress = 1f;
        bool _isOpening;

        void Awake()
        {
            _lastFacing = facing;
            _hasLastFacing = true;

            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            ResolveMaskRig();
            CachePlayer();
            CacheViewAnchors();
            EnsureHint();
        }

        void OnEnable()
        {
            // 门激活期间，把门洞矩形覆盖到的关卡地形碰撞剔除。
            PortalPair pair = Pair;
            if (pair != null) pair.SetPortalTerrainState(this, true);

            // 首次激活、以及禁用后重新启用 GameObject/组件时都会触发，重播开门动画。
            PlayOpenAnimation();

            // playOnAwake=false 的粒子系统不会随物体重新启用而自动恢复播放。
            if (_particles != null) _particles.Play(true);
        }

        void OnDisable()
        {
            // 门停用后恢复地形上被剔除的碰撞。
            PortalPair pair = Pair;
            if (pair != null) pair.SetPortalTerrainState(this, false);
        }

        void Start()
        {
            // 获取本门显示精灵所在的游戏对象，并记住它在视觉中心下的初始局部位置。
            // ApplyHalfCrop 只在此基础上叠加 X 偏移，不能把 Portal View Center 的 Z 偏移覆盖掉。
            if (output != null)
            {
                viewObject = output.gameObject;

                if (viewCenter == null)
                    viewCenter = output.transform.parent;

                _viewBaseLocalPosition = output.transform.localPosition;
                _hasViewBase = true;

                // 传送门相机排除 Portal View 所在层，避免把自己的输出拍进画面形成递归。
                if (capture != null && capture.captureCamera != null && output.gameObject.layer != 0)
                    capture.captureCamera.cullingMask &= ~(1 << output.gameObject.layer);
            }

            ChangeFacing();
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
            // 立刻把光带方向/粒子方向同步到新朝向，而不是等下一帧 LateUpdate。
            UpdateHint();
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

        /// <summary>缓存本门与另一扇门的视觉中心，避免每帧重复解析层级。</summary>
        void CacheViewAnchors()
        {
            _viewAnchor = ViewAnchor;
            _partnerViewAnchor = partner != null ? partner.ViewAnchor : null;
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
                _beamMaterial = new Material(beamShader)
                {
                    hideFlags = HideFlags.DontSave
                };
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
            ApplyHalfCrop();
            UpdateMasks();
            UpdateHint();
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

        /// <summary>
        /// 根据玩家与两扇门的距离更新画面颜色：
        /// 到本门的距离决定 RGB 冷暖，到另一扇门的距离决定 alpha（越近越透明）。
        /// </summary>
        void UpdateViewColor()
        {
            if (player == null || output == null) return;

            Vector3 playerPosition = player.transform.position;

            // 玩家到本门视觉中心局部 Y 轴的垂直距离。
            Transform ownAnchor = _viewAnchor != null ? _viewAnchor : transform;
            float playerDistanceToYAxis = Mathf.Abs(ownAnchor.InverseTransformPoint(playerPosition).x);

            float color_offs = 0.5f;
            if (playerDistanceToYAxis <= 5f)
            {
                color_offs = playerDistanceToYAxis / 10f;
            }

            // 另一扇门：玩家越靠近它，本门画面越透明；距离达到阈值时恢复 1。
            float alpha = 1f;
            if (_partnerViewAnchor != null && otherViewFadeDistance > 0f)
            {
                float distanceToPartner = Mathf.Abs(_partnerViewAnchor.InverseTransformPoint(playerPosition).x);
                alpha = Mathf.Clamp01(distanceToPartner / otherViewFadeDistance);
            }

            output.color = side == CaptureGroupSide.B
                ? new Color(1f, 1f - color_offs, 1f - color_offs, alpha)
                : new Color(1f - color_offs, 1f - color_offs, 1f, alpha);

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
        }

        /// <summary>双向连线：每组的相机都画到另一组的输出上。</summary>
        public void WireUp()
        {
            if (capture == null || output == null) return;
            if (partner == null || partner.capture == null || partner.output == null) return;

            capture.output = partner.output;
            partner.capture.output = output;
        }

        /// <summary>找到本组下面创建的遮罩节点。</summary>
        public void ResolveMaskRig()
        {
            if (maskTop != null && maskBottom != null) return;

            Transform maskRoot = transform.Find("Portal Mask");
            if (maskRoot == null) maskRoot = transform;
            if (maskTop == null) maskTop = maskRoot.Find("View Mask Top");
            if (maskBottom == null) maskBottom = maskRoot.Find("View Mask Bottom");
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
        /// 只保留捕获画面的一半：用贴图的一半重建精灵，
        /// 并把它摆到完整画面中对应的位置。
        /// </summary>
        public void ApplyHalfCrop()
        {
            if (output == null) return;

            Sprite full = output.sprite;
            if (full == null || full == _halfSprite) return;

            float fullWidth = full.bounds.size.x;
            float pixelsPerUnit = full.pixelsPerUnit;

            // A 保留右半，B 保留左半。两半都在中线处留出一点缝隙：
            // 共用的像素列会在交叉显示时被放大成一条明显的接缝。
            bool rightHalf = side == CaptureGroupSide.A;
            float halfPixels = full.rect.width * 0.5f;
            float gapPixels = Mathf.Clamp(centerGapPixels, 0f, halfPixels * 0.5f);

            var halfRect = new Rect(
                rightHalf ? full.rect.x + halfPixels + gapPixels : full.rect.x,
                full.rect.y,
                halfPixels - gapPixels,
                full.rect.height);

            if (_halfSprite != null) Destroy(_halfSprite);
            _halfSprite = Sprite.Create(full.texture, halfRect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
            _halfSprite.name = "Snapshot Half";

            output.sprite = _halfSprite;

            // 把保留的区域摆到它在完整画面中的位置。
            float gapWorld = gapPixels / pixelsPerUnit;
            float offset = (fullWidth * 0.25f + gapWorld * 0.5f) * (rightHalf ? 1f : -1f);

            if (!_hasViewBase)
            {
                _viewBaseLocalPosition = output.transform.localPosition;
                _hasViewBase = true;
            }

            // 在 Portal View 的初始局部位置（可能带有 Portal View Center 的 Z 偏移）上叠加半屏 X 偏移。
            output.transform.localPosition = _viewBaseLocalPosition + new Vector3(offset, 0f, 0f);
        }
    }
}
