using System;
using UnityEngine;

namespace Platformer
{
    /// <summary>单个池化射击的飞行、世界空间轨迹和撞墙粒子表现；不绘制弹体。</summary>
    [DisallowMultipleComponent]
    public sealed class PortalProjectile : MonoBehaviour
    {
        [Header("引用")]
        [InspectorName("短寿命粒子系统")]
        public ParticleSystem trailParticles;
        [InspectorName("长寿命粒子系统")]
        public ParticleSystem residualParticles;
        [InspectorName("撞墙粒子系统")]
        public ParticleSystem impactParticles;

        [Header("飞行")]
        [InspectorName("子弹默认运动速度")]
        [Tooltip("由 PortalProjectileSystem 在每次发射时下发")]
        [Min(0.01f)] public float speed = 95f;

        [Header("大量短寿命定向锥形粒子")]
        [InspectorName("短粒子存活时间")]
        [Min(0.01f)] public float trailLifetime = 0.14f;
        [InspectorName("短粒子尺寸")]
        [Min(0.001f)] public float trailSize = 0.035f;
        [InspectorName("短粒子每单位距离发射数")]
        [Min(0f)] public float trailRateOverDistance = 160f;
        [InspectorName("短粒子爆炸力度")]
        [Min(0f)] public float trailSpeed = 2.6f;
        [InspectorName("短粒子扩散夹角")]
        [Range(0f, 89f)] public float trailConeAngle = 14f;

        [Header("长寿命随机方向残留粒子")]
        [InspectorName("长粒子存活时间")]
        [Min(0.1f)] public float residualLifetime = 1.2f;
        [InspectorName("长粒子尺寸")]
        [Min(0.001f)] public float residualSize = 0.05f;
        [InspectorName("长粒子每单位距离发射数")]
        [Min(0f)] public float residualRateOverDistance = 4f;
        [InspectorName("长粒子漂移速度")]
        [Min(0f)] public float residualSpeed = 0.35f;
        [InspectorName("长粒子最小重力")]
        [Min(0f)] public float residualGravityMin = 0.01f;
        [InspectorName("长粒子最大重力")]
        [Min(0f)] public float residualGravityMax = 0.06f;

        [Header("撞墙重力大粒子")]
        [InspectorName("撞墙粒子存活时间")]
        [Min(0.01f)] public float impactLifetime = 0.75f;
        [InspectorName("撞墙粒子爆炸力度")]
        [Min(0f)] public float impactSpeed = 3.5f;
        [InspectorName("撞墙粒子尺寸")]
        [Min(0.001f)] public float impactSize = 0.13f;
        [InspectorName("撞墙粒子重力")]
        [Min(0f)] public float impactGravity = 1.2f;
        [InspectorName("撞墙粒子数量")]
        [Min(1)] public int impactCount = 36;
        [InspectorName("撞墙粒子扩散夹角")]
        [Range(0f, 89f)] public float impactConeAngle = 30f;
        [InspectorName("撞墙提前触发距离")]
        [Min(0f)] public float impactPreDistance = 0.1f;
        [InspectorName("首次碰撞反弹系数")]
        [Range(0f, 1f)] public float impactBounceFirst = 1f;
        [InspectorName("后续碰撞反弹系数")]
        [Range(0f, 1f)] public float impactBounceRest = 0.05f;
        [InspectorName("反弹衰减时间占比")]
        [Tooltip("占粒子存活时间的比例：0~该值之间从首次反弹系数衰减到后续反弹系数")]
        [Range(0.001f, 1f)] public float impactBounceDecayTime = 0.15f;
        [InspectorName("撞墙粒子地图与传送门碰撞层")]
        public LayerMask impactCollisionMask = (1 << 6) | (1 << 8);

        Action<PortalProjectile> _completed;
        Vector2 _startPoint;
        Vector2 _endPoint;
        Vector2 _impactPoint;
        bool _emitImpactOnArrival;
        bool _launched;
        bool _arrived;
        bool _completionSent;

        public Vector2 StartPoint => _startPoint;
        public Vector2 EndPoint => _endPoint;
        public Vector2 ImpactPoint => _impactPoint;
        public bool EmitImpactOnArrival => _emitImpactOnArrival;
        public bool IsFlying => _launched && !_arrived;
        public bool HasArrived => _launched && _arrived;

        void Awake()
        {
            ConfigureParticles();
        }

        internal void Configure(Action<PortalProjectile> completed)
        {
            _completed = completed;
        }

        public void Launch(
            Vector2 origin,
            Vector2 endPoint,
            Color color,
            bool emitImpactOnArrival)
        {
            StopAndClear(trailParticles);
            StopAndClear(residualParticles);
            StopAndClear(impactParticles);

            _startPoint = origin;
            _endPoint = endPoint;
            Vector2 vector = endPoint - origin;
            float distance = vector.magnitude;
            Vector2 direction = distance > 0.00000001f ? vector / distance : Vector2.zero;
            float preDistance = Mathf.Min(impactPreDistance, distance);
            _impactPoint = endPoint - direction * preDistance;
            _emitImpactOnArrival = emitImpactOnArrival;
            _launched = true;
            _arrived = false;
            _completionSent = false;

            transform.position = new Vector3(origin.x, origin.y, 0f);
            if (trailParticles != null && direction.sqrMagnitude > 0.00000001f)
            {
                trailParticles.transform.rotation = Quaternion.FromToRotation(
                    Vector3.forward, direction);
            }
            SetStartColor(trailParticles, color);
            SetStartColor(residualParticles, color);
            SetStartColor(impactParticles, color);
            if (trailParticles != null) trailParticles.Play(true);
            if (residualParticles != null) residualParticles.Play(true);

            if ((_impactPoint - origin).sqrMagnitude <= 0.00000001f)
                Arrive();
        }

        void Update()
        {
            if (!_launched) return;

            if (!_arrived)
            {
                Vector3 target = new Vector3(_impactPoint.x, _impactPoint.y, 0f);
                transform.position = Vector3.MoveTowards(
                    transform.position, target, Mathf.Max(0.01f, speed) * Time.deltaTime);
                if ((target - transform.position).sqrMagnitude <= 0.00000001f)
                    Arrive();
                return;
            }

            if (!IsAlive(trailParticles) &&
                !IsAlive(residualParticles) &&
                !IsAlive(impactParticles))
                Complete();
        }

        public void ResetForPool()
        {
            _launched = false;
            _arrived = false;
            _completionSent = false;
            StopAndClear(trailParticles);
            StopAndClear(residualParticles);
            StopAndClear(impactParticles);
        }

        void Arrive()
        {
            if (_arrived) return;
            _arrived = true;
            transform.position = new Vector3(_impactPoint.x, _impactPoint.y, 0f);
            if (trailParticles != null)
                trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (residualParticles != null)
                residualParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (_emitImpactOnArrival && impactParticles != null)
            {
                impactParticles.transform.position = transform.position;
                Vector3 direction = (_endPoint - _startPoint).normalized;
                if (direction.sqrMagnitude > 0.00000001f)
                {
                    impactParticles.transform.rotation = Quaternion.FromToRotation(
                        Vector3.forward, direction);
                }
                impactParticles.Play(true);
            }
        }

        void Complete()
        {
            if (_completionSent) return;
            _completionSent = true;
            _launched = false;
            _completed?.Invoke(this);
        }

        void ConfigureParticles()
        {
            ConfigureTrail();
            ConfigureResidual();
            ConfigureImpact();
        }

        void ConfigureTrail()
        {
            if (trailParticles == null) return;
            var main = trailParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = Mathf.Max(0.01f, trailLifetime);
            main.startSpeed = Mathf.Max(0f, trailSpeed);
            main.startSize = Mathf.Max(0.001f, trailSize);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2048;

            var emission = trailParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = Mathf.Max(0f, trailRateOverDistance);
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var shape = trailParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(trailConeAngle, 0f, 89f);
            shape.radius = 0.02f;
            shape.radiusThickness = 1f;

            ConfigureFade(trailParticles);
            ConfigureRenderer(trailParticles.GetComponent<ParticleSystemRenderer>(), 11);
        }

        void ConfigureResidual()
        {
            if (residualParticles == null) return;
            var main = residualParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = Mathf.Max(0.1f, residualLifetime);
            main.startSpeed = Mathf.Max(0f, residualSpeed);
            main.startSize = Mathf.Max(0.001f, residualSize);
            float gravityMin = Mathf.Max(0f, residualGravityMin);
            float gravityMax = Mathf.Max(gravityMin, residualGravityMax);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(gravityMin, gravityMax);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 768;

            var emission = residualParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = Mathf.Max(0f, residualRateOverDistance);
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var shape = residualParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.02f;

            ConfigureFade(residualParticles);
            ConfigureRenderer(residualParticles.GetComponent<ParticleSystemRenderer>(), 10);
        }

        void ConfigureImpact()
        {
            if (impactParticles == null) return;
            var main = impactParticles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.01f, impactLifetime);
            main.startLifetime = Mathf.Max(0.01f, impactLifetime);
            main.startSpeed = Mathf.Max(0f, impactSpeed);
            main.startSize = Mathf.Max(0.001f, impactSize);
            main.gravityModifier = Mathf.Max(0f, impactGravity);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 128;

            var emission = impactParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)Mathf.Clamp(impactCount, 1, 128))
            });

            var shape = impactParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(impactConeAngle, 0f, 89f);
            shape.radius = 0.08f;

            var collision = impactParticles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision2D;
            collision.collidesWith = impactCollisionMask;
            collision.bounce = BuildBounceCurve();
            collision.dampen = 0f;
            collision.lifetimeLoss = 0f;
            collision.radiusScale = 1f;

            ConfigureFade(impactParticles);
            ConfigureRenderer(impactParticles.GetComponent<ParticleSystemRenderer>(), 12);
        }

        // 首次碰撞（粒子刚出生）取满弹性，存活时间经过 decayTime 比例后降到接近零。
        // bounce 的 Curve 模式按粒子年龄归一化求值，再乘 multiplier。
        ParticleSystem.MinMaxCurve BuildBounceCurve()
        {
            float first = Mathf.Clamp01(impactBounceFirst);
            if (first <= 0f) return new ParticleSystem.MinMaxCurve(0f);
            float rest = Mathf.Clamp01(Mathf.Min(impactBounceRest, first));
            float decay = Mathf.Clamp(impactBounceDecayTime, 0.001f, 1f);
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(decay, rest / first),
                new Keyframe(1f, rest / first));
            return new ParticleSystem.MinMaxCurve(first, curve);
        }

        static void ConfigureFade(ParticleSystem particles)
        {
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;
        }

        static void ConfigureRenderer(ParticleSystemRenderer renderer, int sortingOrder)
        {
            if (renderer == null) return;
            renderer.sharedMaterial = CaptureDisplayGroup.ParticleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerName = CaptureDisplayGroup.PortalLightBandLayer;
            renderer.sortingOrder = sortingOrder;
            renderer.maskInteraction = SpriteMaskInteraction.None;
        }

        static void SetStartColor(ParticleSystem particles, Color color)
        {
            if (particles == null) return;
            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }

        static void StopAndClear(ParticleSystem particles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static bool IsAlive(ParticleSystem particles)
        {
            return particles != null && particles.IsAlive(true);
        }
    }
}
