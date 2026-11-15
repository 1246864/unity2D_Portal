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
        Field("moveSpeed", "移动速度", "最高跑速（单位/秒）");
        Field("groundAcceleration", "地面加速度", "地面上按住方向时的加速度");
        Field("groundDeceleration", "地面减速度", "地面上松开方向时的减速度");
        Field("airAcceleration", "空中加速度", "空中按住方向时的加速度");
        Field("airDeceleration", "空中减速度", "空中松开方向时的减速度");

        Header("跳跃");
        Field("jumpHeight", "跳跃高度", "最大跳跃高度（世界单位，等于格）");
        Field("playerHeight", "角色高度", "角色高度（格），用于地面探测");
        Field("coyoteTime", "离地起跳宽限", "离开地面后仍可起跳的宽容时间");
        Field("jumpBufferTime", "跳跃缓冲时间", "落地前提前按跳的缓冲时间");
        Field("fallGravityMultiplier", "下落重力倍率", "下落时额外的重力倍率");
        Field("maxFallSpeed", "最大下落速度", "最大下落速度（单位/秒）");
        Field("jumpCutMultiplier", "松键截断倍率", "提前松开跳跃键时保留的垂直速度比例");

        Header("地面检测");
        Field("groundMask", "地面层", "算作可站立地面的层");
        Field("groundCheckDistance", "地面检测距离", "地面探测向下延伸的距离");

        Header("重生");
        Field("fallLimitY", "重生高度", "低于该 Y 坐标时重生");

        Header("传送门碰撞点");
        Field("centerPointRadius", "中心碰撞点半径",
            "玩家中心碰撞点的半径（世界单位），用于和传送门的薄碰撞箱做连续碰撞");

        Header("显示");
        Field("visualRoot", "显示根节点",
            "玩家精灵的父节点；留空时运行时自动创建，物理根节点只保留碰撞");

        Header("传送门旋转（玩家管理）");
        Field("portalRealignSpeed", "基础回正速度", "回正角速度（度/秒）。越大回正越快");
        Field("portalRealignCurve", "回正曲线",
            "回正曲线：横轴 0~1 为回正进度，纵轴 0~1 为角度插值权重");

        Header("传送门回正动态加速");
        Field("portalRealignFallSpeedForMaxBoost", "下坠加速阈值",
            "下坠速度达到该值时，下坠加速达到最大值（世界单位/秒）");
        Field("portalRealignMaxFallSpeedMultiplier", "最大下坠加速倍率",
            "下坠速度达到最大值时，回正速度倍率");
        Field("portalRealignGroundedSpeedMultiplier", "触地回正倍率",
            "玩家触地时的回正速度倍率");

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
