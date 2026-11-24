// 文档导航（相对于项目根目录）：
// 模块说明：.trae/documents/player-controller-modules.md
// 架构说明：.trae/documents/player-controller-architecture.md
using UnityEditor;
using UnityEngine;
using Platformer;

[CustomEditor(typeof(PlayerController2D))]
public class PlayerController2DEditor : Editor
{
    SerializedProperty _property;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Header("水平移动");
        Field("moveSpeed", "玩家水平目标速度",
            "地面和空中方向输入共同使用的目标速度（单位/秒）；已有同向超速不会被压回此值。");
        Field("groundAcceleration", "玩家水平加速度",
            "地面或玩家墙面模式按住方向时，水平速度接近目标跑速的速率（单位/秒²）。");
        Field("groundDeceleration", "玩家水平减速度",
            "地面松开方向时使用的水平减速率（单位/秒²）；贴墙松键会回到物理模块，不在墙面应用此减速。");
        Field("airAcceleration", "空中速度变化率",
            "方向输入让水平速度接近目标速度的最大变化率（单位/秒²）；乘以剩余空中权限，松键不主动刹车。");
        Field("inputSpeedLimit", "AD 横向增速上限",
            "只限制 AD 继续增加 X 速度绝对值；Y 不参与，已有超速不硬截断，仍可减速和反向。也是高速落地与撞墙动量保留的触发阈值。");

        Header("空中方向控制权限");
        Field("airControlFullTime", "完整控制时间（秒）",
            "离地后方向键保持完整速度调整能力的时间。跳跃和非跳跃坠落共用；非跳跃坠落超过此时间后立即停止方向调整。");
        Field("jumpAirControlEndTime", "跳跃控制归零时间（秒）",
            "跳跃离地后，方向键速度调整能力从完整控制时间到此刻线性降至 0；不缩放已有速度，归零后保持水平动量，落地恢复。");

        Header("输入方向");
        Field("invertHorizontalInput", "反转水平输入",
            "开启后 A/左方向会向右移动，D/右方向会向左移动；墙跳方向锁和墙面判断仍使用实际输入方向。");

        Header("跳跃");
        EditorGUILayout.HelpBox(
            "满跳高度会自动反算起跳初速度。基础重力由 Physics2D.gravity 与 Rigidbody2D.gravityScale 共同决定；" +
            "脚本只调整上升和顶点弧线，下落完全交给原生重力。",
            MessageType.Info);
        Field("jumpHeight", "满跳高度（格）",
            "持续按住跳跃时，从起跳位置到顶点的目标世界高度；1 世界单位按 1 格计算。");
        Field("playerHeight", "角色显示高度（格）",
            "用于 Scene 视图的跳跃高度辅助线；实际接触探测使用 CapsuleCollider2D 的实时 Bounds。");
        Field("coyoteTime", "离地起跳宽限（秒）",
            "离开平台边缘后仍允许普通跳跃的时间；设为 0 可关闭土狼时间。");
        Field("jumpBufferTime", "跳跃预输入（秒）",
            "提前按跳后保留输入的时间；明确离墙输入且非上升时墙跳优先。上升贴墙按跳会丢弃墙跳缓冲，下降后需重新按跳。");
        Field("variableJumpTime", "长按维持上升（秒）",
            "持续按住跳跃可维持推进；玩家跳跃上升期间暂停墙面监测，顶点或传送后恢复。主动切入玩家模块会结束推进，但不截断当前 Y 速度。");
        Field("riseGravityMultiplier", "上升重力倍率",
            "非维持阶段且向上运动时使用的重力倍率。调低可减缓上升速度变化并降低起跳初速度，满跳高度仍自动保持。");
        Field("jumpHorizontalBoost", "跳跃水平助推",
            "普通起跳在当前 X 上沿输入方向代数相加；墙跳用它计算固定离墙 X，并同时覆盖原 X/Y，不继承碰墙动量。");
        Field("apexVelocityThreshold", "上升顶点速度阈值",
            "上升速度低于此值（单位/秒）时进入顶点阶段；只有仍按住跳跃才应用顶点低重力。");
        Field("apexGravityMultiplier", "顶点重力倍率",
            "上升顶点阶段直接乘在基础重力上的倍率；下落阶段不使用此修正。");
        Field("maxFallSpeed", "玩家墙滑最终上限",
            "普通低速玩家墙滑的最终下落速度上限（单位/秒）；不限制自由下落或传送出口速度。");

        Header("墙面（无主动抓墙/攀爬/体力）");
        Field("wallCheckDistance", "墙面探测距离",
            "从胶囊左右两侧进行 BoxCast 的额外距离（世界单位）；只接受法线足够竖直的墙面。");
        Field("wallJumpHeight", "墙跳满跳高度（格）",
            "墙跳持续按住时的目标高度，独立于普通满跳高度。较低墙跳配合离墙方向锁，使玩家返回同一面墙时高度自然下降，但不限制墙跳次数。");
        Field("wallSlideStartSpeed", "墙滑初始速度上限",
            "普通玩家墙滑从该下落上限开始；高速撞墙缓存期内的物理推墙制动也使用此阈值，不参与动作系统交接。");
        Field("wallSlideTime", "贴墙恢复时间（秒）",
            "持续玩家墙滑时，下落上限从初始值恢复到最终上限所需时间，避免无限缓慢悬挂。");
        Field("highSpeedWallBrakeAcceleration", "高速推墙制动加速度",
            "高速撞墙的 X 缓存期内，下落且朝墙输入时由物理模块施加的向上加速度（单位/秒²）；范围 0~任意正值，0 表示关闭，仅贴墙不推墙不会制动。");
        Field("wallJumpForceTime", "墙跳方向锁（秒）",
            "墙跳后短暂保持离墙方向，避免持续推向原墙立即抵消离墙速度；范围 0~任意正值，0 表示关闭，与较低墙跳高度共同保证返回同墙时低于起跳点。");

        Header("高速接触与墙滑淡出");
        Field("highSpeedContactRetentionTime", "高速接触保留时间（秒）",
            "高速落地延迟交接及撞墙 X 缓存共用的时长；范围 0~任意正值，0 表示立即交接且不建立撞墙缓存。落地、跳跃或传送会按规则提前清理缓存。");
        Field("wallSlideReleaseFadeTime", "墙滑松键淡出时间（秒）",
            "同一面墙从中性松键退出玩家墙滑后，Y 制动权重线性淡出的时长；范围 0~任意正值，0 表示立即恢复原生重力，X 不做额外插值。");

        Header("接触探测");
        Field("groundMask", "地面与墙面层",
            "参与地面和墙面 BoxCast 的地形 LayerMask；用于动作系统切换、贴墙下落和墙跳。");
        Field("portalContactMask", "排除的传送门层",
            "从地墙控制接触中排除的门碰撞层；入口和出口碰撞不能触发玩家接管或墙制动。");
        Field("groundCheckDistance", "地面探测距离",
            "胶囊底部向下延伸的探测距离（世界单位）；过大会提前判定接地，过小可能在斜坡上丢失接触。");

        Header("重生");
        Field("fallLimitY", "重生高度（世界 Y）",
            "玩家根节点低于此世界 Y 坐标时回到出生点；跳跃缓冲和土狼时间不会跨重生保留。");

        Header("传送门碰撞点");
        Field("centerPointRadius", "中心碰撞点半径",
            "玩家中心用于撞击薄传送门的圆形碰撞点半径（世界单位）；连续碰撞检测用它避免高速穿门。");

        Header("显示");
        Field("visualRoot", "显示根节点",
            "承载 SpriteRenderer 和传送门旋转的子节点；留空会在 Awake 自动创建。物理根节点和胶囊始终不旋转。");

        Header("传送门回正");
        Field("portalRealignSpeed", "基础回正速度（度/秒）",
            "玩家显示节点从出口绝对角度回到竖直的基础角速度；出口为 0° 时无需回正。");
        Field("portalRealignCurve", "回正插值曲线",
            "横轴为归一化回正进度，纵轴为角度插值权重；可用于调整回正先快后慢或先慢后快。");

        Header("传送门回正动态加速");
        Field("portalRealignFallSpeedForMaxBoost", "下坠加速阈值",
            "世界下落速度达到此值（单位/秒）时，下坠带来的回正加速达到配置上限。");
        Field("portalRealignMaxFallSpeedMultiplier", "最大下坠回正倍率",
            "达到下坠加速阈值时使用的最大回正速度倍率；实际倍率会随当前下落速度插值。");
        Field("portalRealignGroundedSpeedMultiplier", "触地回正倍率",
            "接地时至少使用的回正速度倍率，让玩家落地后更快恢复竖直；不会低于当前下坠倍率。");

        serializedObject.ApplyModifiedProperties();
    }

    void Header(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void Field(string propertyName, string label, string tooltip)
    {
        _property = serializedObject.FindProperty(propertyName);
        if (_property == null) return;

        EditorGUILayout.PropertyField(
            _property,
            new GUIContent(label, string.IsNullOrEmpty(_property.tooltip) ? tooltip : _property.tooltip),
            true);
    }
}
