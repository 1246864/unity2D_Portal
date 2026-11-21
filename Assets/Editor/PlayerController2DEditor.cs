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
        Field("moveSpeed", "最高跑速",
            "常规水平目标速度（单位/秒）。墙跳或传送产生的同向超速会渐进衰减，不会瞬间压回此值。");
        Field("groundAcceleration", "地面加速度",
            "接地时按住方向，水平速度接近目标跑速的速率（单位/秒²）；越大起步越快。");
        Field("groundDeceleration", "地面减速度",
            "接地时松开方向或反向输入时使用的水平减速率（单位/秒²）。");
        Field("airAcceleration", "空中加速度",
            "空中按住方向时使用的水平加速率（单位/秒²）；通常低于地面加速度以保留惯性。");
        Field("airDeceleration", "空中减速度",
            "空中松开方向或反向输入时使用的水平减速率（单位/秒²）。");
        Field("overSpeedDeceleration", "同向超速衰减",
            "速度超过最高跑速且仍同向输入时的衰减速率（单位/秒²）；空中自动使用此值的 65%。");

        Header("输入方向");
        Field("invertHorizontalInput", "反转水平输入",
            "开启后 A/左方向会向右移动，D/右方向会向左移动；墙跳方向锁和墙面判断仍使用实际输入方向。");

        Header("跳跃");
        EditorGUILayout.HelpBox(
            "满跳高度会自动反算起跳初速度。基础重力由 Physics2D.gravity 与 Rigidbody2D.gravityScale 共同决定；" +
            "上升、顶点、下落倍率分别控制对应阶段的弧线，不需要手动换算初速度。",
            MessageType.Info);
        Field("jumpHeight", "满跳高度（格）",
            "持续按住跳跃时，从起跳位置到顶点的目标世界高度；1 世界单位按 1 格计算。");
        Field("playerHeight", "角色显示高度（格）",
            "用于 Scene 视图的跳跃高度辅助线；实际接触探测使用 CapsuleCollider2D 的实时 Bounds。");
        Field("coyoteTime", "离地起跳宽限（秒）",
            "离开平台边缘后仍允许普通跳跃的时间；设为 0 可关闭土狼时间。");
        Field("jumpBufferTime", "跳跃预输入（秒）",
            "提前按跳后保留输入的时间；落地或接触墙面时自动消费，地面/土狼跳优先于墙跳。");
        Field("variableJumpTime", "长按维持上升（秒）",
            "起跳后持续按住跳跃可维持推进的时间；提前松开会结束维持，但不会直接截断当前速度。传送门会保留剩余时间。");
        Field("riseGravityMultiplier", "上升重力倍率",
            "非维持阶段且向上运动时使用的重力倍率。调低可减缓上升速度变化并降低起跳初速度，满跳高度仍自动保持。");
        Field("jumpHorizontalBoost", "跳跃水平助推",
            "起跳时沿当前输入方向额外增加的水平速度（单位/秒）；墙跳也用它计算离墙初速度。");
        Field("apexVelocityThreshold", "顶点速度阈值",
            "垂直速度绝对值低于此值（单位/秒）时进入顶点阶段；只有仍按住跳跃才应用顶点低重力。");
        Field("apexGravityMultiplier", "顶点重力倍率",
            "顶点阶段直接乘在基础重力上的倍率，优先于上升/下落倍率；越小，越过顶点时越柔和。");
        Field("fallGravityMultiplier", "下落重力倍率",
            "向下运动时使用的重力倍率。调低可减缓下落加速，不改变上升过程和满跳高度。");
        Field("maxFallSpeed", "最大下落速度",
            "世界 Y 轴的终端下落速度（单位/秒）；贴墙下落恢复上限也会跟随此值。");

        Header("墙面（无主动抓墙/攀爬/体力）");
        Field("wallCheckDistance", "墙面探测距离",
            "从胶囊左右两侧进行 BoxCast 的额外距离（世界单位）；只接受法线足够竖直的墙面。");
        Field("wallJumpHeight", "墙跳满跳高度（格）",
            "墙跳持续按住时的目标高度，独立于普通满跳高度。较低墙跳配合离墙方向锁，使玩家返回同一面墙时高度自然下降，但不限制墙跳次数。");
        Field("wallSlideStartSpeed", "贴墙初始下落上限",
            "空中下落并持续推向墙面时，刚开始贴墙的下落速度上限（单位/秒）；松开方向立即退出。");
        Field("wallSlideTime", "贴墙恢复时间（秒）",
            "持续贴墙时，下落上限从初始值恢复到最大下落速度所需时间，避免无限缓慢悬挂。");
        Field("wallJumpForceTime", "墙跳方向锁（秒）",
            "墙跳后短暂保持离墙方向，避免持续推向原墙立即抵消离墙速度；与较低墙跳高度共同保证返回同墙时低于起跳点，不限制再次墙跳。");

        Header("接触探测");
        Field("groundMask", "地面与墙面层",
            "同时参与地面和墙面 BoxCast 的 LayerMask；应只包含允许站立、贴墙下落和墙跳的地形层。");
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
            new GUIContent(label, tooltip),
            true);
    }
}
