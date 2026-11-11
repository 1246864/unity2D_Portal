using System;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Minimal GameObject pool. Used by the region mirror so the per-frame capture
    /// never allocates or destroys objects.
    /// </summary>
    public class SimpleObjectPool
    {
        readonly Func<GameObject> _factory;
        readonly Transform _parent;
        readonly Stack<GameObject> _idle = new Stack<GameObject>();

        /// <summary>Number of objects ever created by this pool.</summary>
        public int Created { get; private set; }

        /// <summary>Number of objects currently checked out.</summary>
        public int Active { get; private set; }

        public SimpleObjectPool(Func<GameObject> factory, Transform parent, int prewarm = 0)
        {
            _factory = factory;
            _parent = parent;

            for (int i = 0; i < prewarm; i++)
            {
                GameObject go = Create();
                go.SetActive(false);
                _idle.Push(go);
            }
        }

        public GameObject Get()
        {
            GameObject go = _idle.Count > 0 ? _idle.Pop() : Create();
            if (go.transform.parent != _parent) go.transform.SetParent(_parent, false);
            go.SetActive(true);
            Active++;
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;

            go.SetActive(false);
            if (go.transform.parent != _parent) go.transform.SetParent(_parent, false);
            _idle.Push(go);
            if (Active > 0) Active--;
        }

        public void ReleaseAll(IList<GameObject> activeObjects)
        {
            for (int i = 0; i < activeObjects.Count; i++)
            {
                Release(activeObjects[i]);
            }

            activeObjects.Clear();
        }

        GameObject Create()
        {
            GameObject go = _factory();
            go.transform.SetParent(_parent, false);
            Created++;
            return go;
        }
    }
}
