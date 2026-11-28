using Platformer;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PortalProjectile))]
public sealed class PortalProjectileEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Header("粒子系统引用");
        Field("trailParticles", "短寿命粒子系统");
        Field("residualParticles", "长寿命粒子系统");
        Field("impactParticles", "撞墙粒子系统");

        Header("飞行");
        Field("speed", "子弹默认运动速度");

        Header("大量短寿命定向锥形粒子");
        Field("trailLifetime", "短粒子存活时间");
        Field("trailSize", "短粒子尺寸");
        Field("trailRateOverDistance", "短粒子每单位距离发射数");
        Field("trailSpeed", "短粒子爆炸力度");
        Field("trailConeAngle", "短粒子扩散夹角");
        Field("trailStartDistance", "短粒子起始发射距离");
        Field("trailColorBrighten", "短粒子颜色提亮程度");
        Field("trailVisibleStartDistance", "短粒子起始显示距离");
        Field("trailFullColorDistance", "短粒子达到目标颜色距离");

        Header("长寿命随机方向残留粒子");
        Field("residualLifetime", "长粒子存活时间");
        Field("residualSize", "长粒子尺寸");
        Field("residualRateOverDistance", "长粒子每单位距离发射数");
        Field("residualSpeed", "长粒子漂移速度");
        Field("residualGravityMin", "长粒子最小重力");
        Field("residualGravityMax", "长粒子最大重力");

        Header("撞墙重力大粒子");
        Field("impactLifetime", "撞墙粒子存活时间");
        Field("impactSpeed", "撞墙粒子爆炸力度");
        Field("impactSize", "撞墙粒子尺寸");
        Field("impactGravity", "撞墙粒子重力");
        Field("impactCount", "撞墙粒子数量");
        Field("impactConeAngle", "撞墙粒子扩散夹角");
        Field("impactPreDistance", "撞墙提前触发距离");
        Field("impactBounceFirst", "首次碰撞反弹系数");
        Field("impactBounceRest", "后续碰撞反弹系数");
        Field("impactBounceDecayTime", "反弹衰减时间占比");
        Field("impactCollisionMask", "撞墙粒子地图与传送门碰撞层");

        Header("路径双正弦细曲线");
        Field("waveAmplitude", "波动幅度");
        Field("waveWavelength", "波长");
        Field("sinePhaseOffset", "正弦初始相位（角度）");
        Field("secondSinePhaseOffset", "第二正弦初始相位（角度）");
        Field("sineScrollSpeed", "正弦相位滚动速度");
        Field("secondSineScrollSpeed", "第二正弦相位滚动速度");
        Field("sinePointLifetime", "正弦曲线点存活时间");
        Field("secondSinePointLifetime", "第二正弦曲线点存活时间");
        Field("waveVisibleStartDistance", "曲线起始显示距离");
        Field("waveFullColorDistance", "达到目标颜色距离");
        Field("waveColorBrighten", "曲线颜色提亮程度");
        Field("sineWidth", "正弦曲线宽度");
        Field("secondSineWidth", "第二正弦曲线宽度");
        Field("wavePointSpacing", "曲线点间距");
        Field("waveMaxPoints", "最大曲线点数");

        serializedObject.ApplyModifiedProperties();
    }

    static void Header(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void Field(string propertyName, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;
        EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }
}
