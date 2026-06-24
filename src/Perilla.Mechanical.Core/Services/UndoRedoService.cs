using System;
using System.Collections.Generic;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Core.Services
{
    /// <summary>
    /// 撤销/重做服务：管理气泡操作的命令栈。
    /// 支持的操作：添加气泡、删除气泡、自动识别（批量替换）、清空页面气泡。
    /// </summary>
    public class UndoRedoService
    {
        private readonly Stack<ICommand> _undoStack = new Stack<ICommand>();
        private readonly Stack<ICommand> _redoStack = new Stack<ICommand>();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public event Action StateChanged;

        public void Execute(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
            StateChanged?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var cmd = _undoStack.Pop();
            cmd.Undo();
            _redoStack.Push(cmd);
            StateChanged?.Invoke();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);
            StateChanged?.Invoke();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke();
        }
    }

    public interface ICommand
    {
        void Execute();
        void Undo();
    }

    /// <summary>
    /// 添加单个气泡命令
    /// </summary>
    public class AddBubbleCommand : ICommand
    {
        private readonly List<Bubble> _targetList;
        private readonly Bubble _bubble;

        public AddBubbleCommand(List<Bubble> targetList, Bubble bubble)
        {
            _targetList = targetList;
            _bubble = bubble;
        }

        public void Execute()
        {
            _targetList.Add(_bubble);
        }

        public void Undo()
        {
            _targetList.Remove(_bubble);
        }
    }

    /// <summary>
    /// 删除单个气泡命令
    /// </summary>
    public class RemoveBubbleCommand : ICommand
    {
        private readonly List<Bubble> _targetList;
        private readonly Bubble _bubble;
        private int _index;

        public RemoveBubbleCommand(List<Bubble> targetList, Bubble bubble)
        {
            _targetList = targetList;
            _bubble = bubble;
        }

        public void Execute()
        {
            _index = _targetList.IndexOf(_bubble);
            if (_index >= 0) _targetList.RemoveAt(_index);
        }

        public void Undo()
        {
            if (_index >= 0 && _index <= _targetList.Count)
                _targetList.Insert(_index, _bubble);
            else
                _targetList.Add(_bubble);
        }
    }

    /// <summary>
    /// 自动识别替换命令：保存替换前的所有自动气泡，用于撤销
    /// </summary>
    public class AutoRecognizeCommand : ICommand
    {
        private readonly List<List<Bubble>> _pageBubbles;
        private readonly List<List<Bubble>> _previousAutoBubbles;
        private readonly List<List<Bubble>> _newBubbles;

        public AutoRecognizeCommand(List<List<Bubble>> pageBubbles,
                                     List<List<Bubble>> previousAutoBubbles,
                                     List<List<Bubble>> newBubbles)
        {
            _pageBubbles = pageBubbles;
            _previousAutoBubbles = previousAutoBubbles;
            _newBubbles = newBubbles;
        }

        public void Execute()
        {
            for (int i = 0; i < _pageBubbles.Count; i++)
            {
                // 移除旧的自动气泡，保留手动气泡
                _pageBubbles[i].RemoveAll(b => !b.IsManual);
                // 添加新的自动气泡
                if (i < _newBubbles.Count)
                {
                    foreach (var b in _newBubbles[i])
                    {
                        if (!b.IsManual) _pageBubbles[i].Add(b);
                    }
                }
            }
        }

        public void Undo()
        {
            for (int i = 0; i < _pageBubbles.Count; i++)
            {
                // 移除当前自动气泡，保留手动气泡
                _pageBubbles[i].RemoveAll(b => !b.IsManual);
                // 恢复之前的自动气泡
                if (i < _previousAutoBubbles.Count)
                {
                    foreach (var b in _previousAutoBubbles[i])
                    {
                        if (!b.IsManual) _pageBubbles[i].Add(b);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 清空页面所有气泡命令（用于撤销时恢复）
    /// </summary>
    public class ClearBubblesCommand : ICommand
    {
        private readonly List<List<Bubble>> _pageBubbles;
        private readonly List<List<Bubble>> _backup;

        public ClearBubblesCommand(List<List<Bubble>> pageBubbles)
        {
            _pageBubbles = pageBubbles;
            _backup = new List<List<Bubble>>();
            foreach (var list in pageBubbles)
            {
                var copy = new List<Bubble>();
                foreach (var b in list) copy.Add(b);
                _backup.Add(copy);
            }
        }

        public void Execute()
        {
            foreach (var list in _pageBubbles) list.Clear();
        }

        public void Undo()
        {
            for (int i = 0; i < _pageBubbles.Count; i++)
            {
                _pageBubbles[i].Clear();
                if (i < _backup.Count)
                {
                    foreach (var b in _backup[i]) _pageBubbles[i].Add(b);
                }
            }
        }
    }

    /// <summary>
    /// 移动气泡位置命令
    /// </summary>
    public class MoveBubbleCommand : ICommand
    {
        private readonly Bubble _bubble;
        private readonly PointD _oldCenter;
        private readonly PointD _newCenter;

        public MoveBubbleCommand(Bubble bubble, PointD oldCenter, PointD newCenter)
        {
            _bubble = bubble;
            _oldCenter = oldCenter;
            _newCenter = newCenter;
        }

        public void Execute()
        {
            _bubble.Center = _newCenter;
        }

        public void Undo()
        {
            _bubble.Center = _oldCenter;
        }
    }
}
