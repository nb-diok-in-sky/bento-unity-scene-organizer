using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bento
{
    public interface IBentoPage
    {
        string Name { get; }
        string Group { get; }
        string Id { get; }
        void Draw();
    }

    public interface IBentoPageLifecycle
    {
        void OnActivated();
        void OnDeactivated();
        void OnSelectionChanged();
    }

    public sealed class BentoPage : IBentoPage, IBentoPageLifecycle
    {
        readonly Action _draw;
        readonly IBentoPageLifecycle _lifecycle;

        public BentoPage(string group, string id, string name, Action draw, object owner = null)
        {
            Group = group;
            Id = id;
            Name = name;
            _draw = draw ?? throw new ArgumentNullException(nameof(draw));
            _lifecycle = owner as IBentoPageLifecycle;
        }

        public string Name { get; }
        public string Group { get; }
        public string Id { get; }
        public void Draw() => _draw();
        public void OnActivated() => _lifecycle?.OnActivated();
        public void OnDeactivated() => _lifecycle?.OnDeactivated();
        public void OnSelectionChanged() => _lifecycle?.OnSelectionChanged();
    }

    sealed class SelectionPlanCache<TPlan>
    {
        readonly Func<IReadOnlyList<GameObject>, TPlan> _builder;
        int[] _selectionIds = Array.Empty<int>();
        IReadOnlyList<GameObject> _roots = Array.Empty<GameObject>();
        TPlan _plan;
        bool _dirty = true;

        public SelectionPlanCache(Func<IReadOnlyList<GameObject>, TPlan> builder) =>
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));

        public IReadOnlyList<GameObject> Roots { get { EnsureCurrent(); return _roots; } }
        public TPlan Plan { get { EnsureCurrent(); return _plan; } }
        public void Invalidate() => _dirty = true;

        void EnsureCurrent()
        {
            var roots = BentoUtility.GetTopLevelSelectedSceneObjects(sortByHierarchy: true);
            int[] ids = roots.Select(root => root.GetInstanceID()).ToArray();
            if (!_dirty && ids.SequenceEqual(_selectionIds)) return;
            _selectionIds = ids;
            _roots = roots;
            _plan = _builder(_roots);
            _dirty = false;
        }
    }
}
