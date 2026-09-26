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

    /// <summary>A registered page whose tool instance is created on first use.</summary>
    sealed class RegisteredToolPage : IBentoPage, IBentoPageLifecycle
    {
        IBentoTool _tool;

        public RegisteredToolPage(Type toolType, BentoPageAttribute info)
        {
            ToolType = toolType ?? throw new ArgumentNullException(nameof(toolType));
            if (info == null) throw new ArgumentNullException(nameof(info));
            Id = info.Id;
            Group = info.Group;
            Name = info.Name;
            Order = info.Order;
        }

        public Type ToolType { get; }
        public string Name { get; }
        public string Group { get; }
        public string Id { get; }
        public int Order { get; }

        IBentoTool Tool => _tool ?? (_tool = (IBentoTool)Activator.CreateInstance(ToolType, nonPublic: true));

        public void Draw() => Tool.Draw();
        public void OnActivated() => (Tool as IBentoPageLifecycle)?.OnActivated();
        public void OnDeactivated() => (_tool as IBentoPageLifecycle)?.OnDeactivated();
        public void OnSelectionChanged() => (_tool as IBentoPageLifecycle)?.OnSelectionChanged();
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
