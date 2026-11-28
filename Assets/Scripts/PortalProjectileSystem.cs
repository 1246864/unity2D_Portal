using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Platformer
{
    /// <summary>管理 Portal Pair 的子弹预制体、红蓝发射请求和共享对象池。</summary>
    [DisallowMultipleComponent]
    public sealed class PortalProjectileSystem : MonoBehaviour
    {
        [Header("子弹")]
        [InspectorName("子弹粒子预制体")]
        [Tooltip("纯粒子射击预制体；留空时只跳过视觉，不影响放门")]
        public PortalProjectile projectilePrefab;

        [InspectorName("子弹运动速度")]
        [Tooltip("子弹飞行速度（世界单位/秒）")]
        [Min(0.01f)] public float projectileSpeed = 95f;

        [Header("对象池")]
        [InspectorName("预热数量")]
        [Tooltip("Awake 时预热的子弹实例数量")]
        [Min(0)] public int prewarmCount = 8;

        [InspectorName("最大闲置保留数")]
        [Tooltip("闲置实例的最大保留数量；活动子弹不受此数量限制")]
        [Min(1)] public int maxIdleCapacity = 64;

        readonly List<PortalProjectile> _activeProjectiles = new List<PortalProjectile>();
        ObjectPool<PortalProjectile> _pool;

        public int ActiveProjectileCount => _activeProjectiles.Count;
        public int IdleProjectileCount => _pool != null ? _pool.CountInactive : 0;

        void Awake()
        {
            EnsurePool();
        }

        void OnDisable()
        {
            ReleaseAllProjectiles();
        }

        void OnDestroy()
        {
            ReleaseAllProjectiles();
            if (_pool != null)
            {
                _pool.Clear();
                _pool = null;
            }
        }

        /// <summary>发起一次视觉射击；是否产生撞墙粒子只由射线命中结果决定。</summary>
        public void Launch(
            CaptureGroupSide side,
            Vector2 origin,
            Vector2 endPoint,
            bool emitImpactOnArrival)
        {
            if (!isActiveAndEnabled || projectilePrefab == null) return;
            EnsurePool();
            if (_pool == null) return;

            PortalProjectile projectile = _pool.Get();
            _activeProjectiles.Add(projectile);
            projectile.speed = Mathf.Max(0.01f, projectileSpeed);
            Color color = side == CaptureGroupSide.A
                ? CaptureDisplayGroup.HintBlue
                : CaptureDisplayGroup.HintRed;
            projectile.Launch(origin, endPoint, color, emitImpactOnArrival);
        }

        /// <summary>立即清理并归还全部活动视觉，不恢复旧射击。</summary>
        public void ReleaseAllProjectiles()
        {
            if (_activeProjectiles.Count == 0) return;

            PortalProjectile[] active = _activeProjectiles.ToArray();
            foreach (PortalProjectile projectile in active)
                ReleaseProjectile(projectile);
            _activeProjectiles.Clear();
        }

        void EnsurePool()
        {
            if (_pool != null || projectilePrefab == null) return;

            int maxIdle = Mathf.Max(1, maxIdleCapacity);
            _pool = new ObjectPool<PortalProjectile>(
                CreateProjectile,
                projectile => projectile.gameObject.SetActive(true),
                ReleaseToPool,
                DestroyProjectile,
                collectionCheck: true,
                defaultCapacity: Mathf.Max(1, prewarmCount),
                maxSize: maxIdle);

            int count = Mathf.Clamp(prewarmCount, 0, maxIdle);
            if (count == 0) return;
            var prewarmed = new PortalProjectile[count];
            for (int i = 0; i < count; i++) prewarmed[i] = _pool.Get();
            for (int i = 0; i < count; i++) _pool.Release(prewarmed[i]);
        }

        PortalProjectile CreateProjectile()
        {
            PortalProjectile projectile = Instantiate(projectilePrefab, transform);
            projectile.Configure(ReleaseProjectile);
            projectile.gameObject.SetActive(false);
            return projectile;
        }

        void ReleaseProjectile(PortalProjectile projectile)
        {
            if (projectile == null || _pool == null) return;
            if (!_activeProjectiles.Remove(projectile)) return;
            _pool.Release(projectile);
        }

        static void ReleaseToPool(PortalProjectile projectile)
        {
            projectile.ResetForPool();
            projectile.gameObject.SetActive(false);
        }

        static void DestroyProjectile(PortalProjectile projectile)
        {
            if (projectile != null) Destroy(projectile.gameObject);
        }
    }
}
