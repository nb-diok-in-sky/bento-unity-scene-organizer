using System;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class BentoUndoScope : IDisposable
    {
        readonly int _group;
        bool _completed;

        public BentoUndoScope(string name)
        {
            Undo.IncrementCurrentGroup();
            _group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
        }

        public int Group => _group;

        public void Complete() => _completed = true;

        public void Dispose()
        {
            if (_completed)
                Undo.CollapseUndoOperations(_group);
            else
                Undo.RevertAllDownToGroup(_group);
        }
    }

    static class BentoCommand
    {
        public static bool Confirm(string title, string message, string confirm = "执行")
        {
            return EditorUtility.DisplayDialog(title, message, confirm, "取消");
        }

        public static bool Run(string failureTitle, Action action, Func<string> successMessage = null)
        {
            try
            {
                action();
                string message = successMessage?.Invoke();
                if (!string.IsNullOrEmpty(message)) EditorUtility.DisplayDialog("操作完成", message, "确定");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(failureTitle, exception.Message, "确定");
                return false;
            }
        }
    }
}
