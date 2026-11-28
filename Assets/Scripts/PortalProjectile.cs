using System;
using System.Collections.Generic;
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
        [InspectorName("短粒子起始发射距离")]
        [Tooltip("子弹从起点飞出该距离后才开始播撒短寿命粒子")]
        [Min(0f)] public float trailStartDistance = 3f;
        [InspectorName("短粒子颜色提亮程度")]
        [Range(0f, 1f)] public float trailColorBrighten = 0.35f;
        [InspectorName("短粒子起始显示距离")]
        [Min(0f)] public float trailVisibleStartDistance = 1f;
        [InspectorName("短粒子达到目标颜色距离")]
        [Min(0.01f)] public float trailFullColorDistance = 3f;
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

        [Header("路径双正弦细曲线")]
        [InspectorName("波动幅度")]
        [Min(0f)] public float waveAmplitude = 0.08f;
        [InspectorName("波长")]
        [Min(0.01f)] public float waveWavelength = 0.6f;
        [InspectorName("正弦初始相位（角度）")]
        public float sinePhaseOffset = 0f;
        [InspectorName("第二正弦初始相位（角度）")]
        public float secondSinePhaseOffset = 90f;
        [InspectorName("正弦相位滚动速度")]
        public float sineScrollSpeed = 6f;
        [InspectorName("第二正弦相位滚动速度")]
        public float secondSineScrollSpeed = -6f;
        [InspectorName("正弦曲线点存活时间")]
        [Min(0.01f)] public float sinePointLifetime = 0.1f;
        [InspectorName("第二正弦曲线点存活时间")]
        [Min(0.01f)] public float secondSinePointLifetime = 0.2f;
        [InspectorName("曲线起始显示距离")]
        [Min(0f)] public float waveVisibleStartDistance = 1f;
        [InspectorName("达到目标颜色距离")]
        [Min(0.01f)] public float waveFullColorDistance = 3f;
        [InspectorName("曲线颜色提亮程度")]
        [Range(0f, 1f)] public float waveColorBrighten = 0.35f;
        [InspectorName("正弦曲线宽度")]
        [Min(0.001f)] public float sineWidth = 0.02f;
        [InspectorName("第二正弦曲线宽度")]
        [Min(0.001f)] public float secondSineWidth = 0.05f;
        [InspectorName("曲线点间距")]
        [Min(0.01f)] public float wavePointSpacing = 0.12f;
        [InspectorName("最大曲线点数")]
        [Min(16)] public int waveMaxPoints = 512;

        Action<PortalProjectile> _completed;
        Vector2 _startPoint;
        Vector2 _endPoint;
        Vector2 _impactPoint;
        bool _emitImpactOnArrival;
        bool _launched;
        bool _arrived;
        bool _completionSent;
        bool _trailEmissionStarted;
        LineRenderer _sineLine;
        LineRenderer _secondSineLine;
        Color _waveColor;
        Color _trailColor;
        float _sineScrollPhase;
        float _secondSineScrollPhase;
        float _waveAge;
        float _nextSampleDistance;
        readonly Queue<WaveSample> _waveSamples = new Queue<WaveSample>();
        ParticleSystem.Particle[] _trailParticleBuffer = new ParticleSystem.Particle[256];

        struct WaveSample
        {
            public Vector2 Center;
            public Vector2 Normal;
            public float Distance;
            public float BirthTime;
        }

        public Vector2 StartPoint => _startPoint;
        public Vector2 EndPoint => _endPoint;
        public Vector2 ImpactPoint => _impactPoint;
        public bool EmitImpactOnArrival => _emitImpactOnArrival;
        public bool IsFlying => _launched && !_arrived;
        public bool HasArrived => _launched && _arrived;

        void Awake()
        {
            ConfigureParticles();
            EnsureWaveLines();
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
            _trailEmissionStarted = trailStartDistance <= 0f;
            Color bright = Color.Lerp(color, Color.white, Mathf.Clamp01(waveColorBrighten));
            _waveColor = new Color(bright.r, bright.g, bright.b, color.a);
            Color trailBright = Color.Lerp(color, Color.white, Mathf.Clamp01(trailColorBrighten));
            _trailColor = new Color(trailBright.r, trailBright.g, trailBright.b, color.a);
            _sineScrollPhase = 0f;
            _secondSineScrollPhase = 0f;
            _waveAge = 0f;
            EnsureWaveLines();
            ClearWaveLines();

            transform.position = new Vector3(origin.x, origin.y, 0f);
            if (trailParticles != null && direction.sqrMagnitude > 0.00000001f)
            {
                trailParticles.transform.rotation = Quaternion.FromToRotation(
                    Vector3.forward, direction);
            }
            SetStartColor(trailParticles, _trailColor);
            SetStartColor(residualParticles, color);
            SetStartColor(impactParticles, color);
            if (trailParticles != null)
            {
                var emission = trailParticles.emission;
                emission.enabled = _trailEmissionStarted;
                trailParticles.Play(true);
            }
            if (residualParticles != null) residualParticles.Play(true);

            if ((_impactPoint - origin).sqrMagnitude <= 0.00000001f)
                Arrive();
        }

        void Update()
        {
            if (!_launched) return;

            float dt = Time.deltaTime;
            if (!_arrived)
            {
                Vector3 target = new Vector3(_impactPoint.x, _impactPoint.y, 0f);
                transform.position = Vector3.MoveTowards(
                    transform.position, target, Mathf.Max(0.01f, speed) * dt);
                UpdateTrailEmission();
                if ((target - transform.position).sqrMagnitude <= 0.00000001f)
                    Arrive();
                UpdateWaveLines(dt);
                UpdateTrailColorGradient();
                return;
            }

            UpdateWaveLines(dt);
            UpdateTrailColorGradient();
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
            _trailEmissionStarted = false;
            StopAndClear(trailParticles);
            StopAndClear(residualParticles);
            StopAndClear(impactParticles);
            ClearWaveLines();
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

        void UpdateTrailEmission()
        {
            if (_trailEmissionStarted || trailParticles == null) return;
            float startDistance = Mathf.Max(0f, trailStartDistance);
            Vector2 traveled = (Vector2)transform.position - _startPoint;
            if (traveled.sqrMagnitude < startDistance * startDistance) return;
            var emission = trailParticles.emission;
            emission.enabled = true;
            _trailEmissionStarted = true;
        }

        // 短粒子按独立开放的提亮颜色和距离透明渐变参数设置出生颜色；
        // colorOverLifetime 仍负责粒子自身寿命末段的淡出。
        void UpdateTrailColorGradient()
        {
            if (trailParticles == null) return;
            float visibleStart = Mathf.Max(0f, trailVisibleStartDistance);
            float fullDistance = Mathf.Max(visibleStart + 0.01f, trailFullColorDistance);
            if (trailStartDistance >= fullDistance) return;

            Vector2 axis = _impactPoint - _startPoint;
            float fullLength = axis.magnitude;
            if (fullLength <= 0.00000001f) return;
            Vector2 direction = axis / fullLength;

            int particleCount = trailParticles.particleCount;
            if (particleCount == 0) return;
            if (_trailParticleBuffer.Length < particleCount)
                _trailParticleBuffer = new ParticleSystem.Particle[Mathf.Max(64, particleCount)];
            int count = trailParticles.GetParticles(_trailParticleBuffer);
            float range = fullDistance - visibleStart;
            for (int i = 0; i < count; i++)
            {
                float distance = Vector2.Dot(
                    (Vector2)_trailParticleBuffer[i].position - _startPoint, direction);
                float alpha = Mathf.Clamp01((distance - visibleStart) / range);
                _trailParticleBuffer[i].startColor = new Color(
                    _trailColor.r, _trailColor.g, _trailColor.b, _trailColor.a * alpha);
            }
            trailParticles.SetParticles(_trailParticleBuffer, count);
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

        void EnsureWaveLines()
        {
            if (_sineLine == null) _sineLine = CreateWaveLine("Sine Wave", sineWidth);
            if (_secondSineLine == null) _secondSineLine = CreateWaveLine("Second Sine Wave", secondSineWidth);
        }

        LineRenderer CreateWaveLine(string objectName, float width)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.sharedMaterial = CaptureDisplayGroup.ParticleMaterial;
            line.sortingLayerName = CaptureDisplayGroup.PortalLightBandLayer;
            line.sortingOrder = 14;
            float lineWidth = Mathf.Max(0.001f, width);
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            return line;
        }

        void ClearWaveLines()
        {
            if (_sineLine != null) _sineLine.positionCount = 0;
            if (_secondSineLine != null) _secondSineLine.positionCount = 0;
            _waveSamples.Clear();
            _nextSampleDistance = 0f;
        }

        // 头部按固定间距产生采样点；每条线独立按“点产生后多久消失”移除自己的点。
        // 采样队列保留到两条线中较长的寿命，距离透明渐变仍按各线当前可见段计算。
        void UpdateWaveLines(float dt)
        {
            if (_sineLine == null || _secondSineLine == null) return;
            _sineScrollPhase += sineScrollSpeed * dt;
            _secondSineScrollPhase += secondSineScrollSpeed * dt;
            _waveAge += dt;

            Vector2 axis = _impactPoint - _startPoint;
            float fullLength = axis.magnitude;
            if (fullLength <= 0.00000001f)
            {
                ClearWaveLines();
                return;
            }

            Vector2 direction = axis / fullLength;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 head = _arrived ? _impactPoint : (Vector2)transform.position;
            float length = Mathf.Clamp(Vector2.Dot(head - _startPoint, direction), 0f, fullLength);

            float spacing = Mathf.Max(0.01f, wavePointSpacing);
            int maxPoints = Mathf.Max(16, waveMaxPoints);
            int emitted = 0;
            while (_nextSampleDistance <= length && emitted < maxPoints)
            {
                _waveSamples.Enqueue(new WaveSample
                {
                    Center = _startPoint + direction * _nextSampleDistance,
                    Normal = normal,
                    Distance = _nextSampleDistance,
                    BirthTime = _waveAge
                });
                _nextSampleDistance += spacing;
                emitted++;
            }

            float firstLifetime = Mathf.Max(0.01f, sinePointLifetime);
            float secondLifetime = Mathf.Max(0.01f, secondSinePointLifetime);
            float longestLifetime = Mathf.Max(firstLifetime, secondLifetime);
            while (_waveSamples.Count > 0 &&
                   _waveAge - _waveSamples.Peek().BirthTime >= longestLifetime)
                _waveSamples.Dequeue();
            while (_waveSamples.Count > maxPoints) _waveSamples.Dequeue();

            float visibleStart = Mathf.Max(0f, waveVisibleStartDistance);
            float fullDistance = Mathf.Max(visibleStart + 0.01f, waveFullColorDistance);
            float amplitude = Mathf.Max(0f, waveAmplitude);
            float waveNumber = 2f * Mathf.PI / Mathf.Max(0.01f, waveWavelength);
            UpdateWaveLine(
                _sineLine, firstLifetime, sinePhaseOffset * Mathf.Deg2Rad, _sineScrollPhase,
                visibleStart, fullDistance, amplitude, waveNumber);
            UpdateWaveLine(
                _secondSineLine, secondLifetime, secondSinePhaseOffset * Mathf.Deg2Rad,
                _secondSineScrollPhase, visibleStart, fullDistance, amplitude, waveNumber);
        }

        void UpdateWaveLine(
            LineRenderer line,
            float pointLifetime,
            float phaseOffset,
            float scrollPhase,
            float visibleStart,
            float fullDistance,
            float amplitude,
            float waveNumber)
        {
            int count = 0;
            float minDistance = float.MaxValue;
            float maxDistance = 0f;
            foreach (WaveSample sample in _waveSamples)
            {
                if (_waveAge - sample.BirthTime >= pointLifetime) continue;
                if (sample.Distance < visibleStart) continue;
                count++;
                if (sample.Distance < minDistance) minDistance = sample.Distance;
                if (sample.Distance > maxDistance) maxDistance = sample.Distance;
            }

            if (count < 2)
            {
                line.positionCount = 0;
                return;
            }

            line.positionCount = count;
            ApplyWaveGradient(line, minDistance, maxDistance, visibleStart, fullDistance);
            int index = 0;
            foreach (WaveSample sample in _waveSamples)
            {
                if (_waveAge - sample.BirthTime >= pointLifetime) continue;
                if (sample.Distance < visibleStart) continue;
                Vector3 point = sample.Center + sample.Normal * (amplitude * Mathf.Sin(
                    sample.Distance * waveNumber + phaseOffset + scrollPhase));
                point.z = 0f;
                line.SetPosition(index, point);
                index++;
            }
        }

        // 距离起点 visibleStart 处透明，fullDistance 处达到目标颜色；采样点等距，
        // 因此 gradient 的归一化时间与路径距离成线性关系，可用少量关键帧精确表达。
        void ApplyWaveGradient(
            LineRenderer line,
            float minDistance,
            float maxDistance,
            float visibleStart,
            float fullDistance)
        {
            float span = Mathf.Max(0.0001f, maxDistance - minDistance);
            float range = fullDistance - visibleStart;
            float startAlpha = Mathf.Clamp01((minDistance - visibleStart) / range);
            float endAlpha = Mathf.Clamp01((maxDistance - visibleStart) / range);
            GradientAlphaKey[] alphaKeys;
            if (fullDistance > minDistance && fullDistance < maxDistance)
            {
                float middle = (fullDistance - minDistance) / span;
                alphaKeys = new[]
                {
                    new GradientAlphaKey(startAlpha, 0f),
                    new GradientAlphaKey(1f, middle),
                    new GradientAlphaKey(endAlpha, 1f)
                };
            }
            else
            {
                alphaKeys = new[]
                {
                    new GradientAlphaKey(startAlpha, 0f),
                    new GradientAlphaKey(endAlpha, 1f)
                };
            }

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(_waveColor, 0f),
                    new GradientColorKey(_waveColor, 1f)
                },
                alphaKeys);
            line.colorGradient = gradient;
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
