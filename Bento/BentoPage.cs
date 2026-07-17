using System;
using UnityEngine;

namespace Bento
{
    interface IBentoPage
    {
        string Name { get; }
        string Group { get; }
        string Id { get; }
        void Draw();
    }

    sealed class BentoPage : IBentoPage
    {
        readonly Action _draw;

        public BentoPage(string group, string id, string name, Action draw)
        {
            Group = group;
            Id = id;
            Name = name;
            _draw = draw ?? throw new ArgumentNullException(nameof(draw));
        }

        public string Name { get; }
        public string Group { get; }
        public string Id { get; }
        public void Draw() => _draw();
    }

    interface IBentoStatefulToolPage : IBentoPage
    {
        bool CanRestore { get; }
        void Restore();
    }
}
