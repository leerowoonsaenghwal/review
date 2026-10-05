using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CatIsland
{
    public struct PointerSample
    {
        public int id;
        public Vector2 position;
        public bool pressed;
    }

    /// <summary>손가락(또는 마우스) 입력. 테스트에서는 가짜 입력으로 바꿔 끼운다.</summary>
    public interface IPointerSource
    {
        void Collect(List<PointerSample> into);
        float ScrollDelta { get; }
    }

    public class InputSystemPointers : IPointerSource
    {
        public void Collect(List<PointerSample> into)
        {
            into.Clear();
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    into.Add(new PointerSample { id = t.touchId.ReadValue(), position = t.position.ReadValue(), pressed = true });
                }
                if (into.Count > 0) return;
            }
            var m = Mouse.current;
            if (m != null && m.leftButton.isPressed)
                into.Add(new PointerSample { id = -1, position = m.position.ReadValue(), pressed = true });
        }

        public float ScrollDelta
        {
            get
            {
                var m = Mouse.current;
                return m != null ? m.scroll.ReadValue().y : 0f;
            }
        }
    }

    /// <summary>테스트와 자동 시연용 가짜 손가락.</summary>
    public class ScriptedPointers : IPointerSource
    {
        public readonly List<PointerSample> current = new List<PointerSample>();
        public float scroll;

        public void Collect(List<PointerSample> into)
        {
            into.Clear();
            into.AddRange(current);
        }

        public float ScrollDelta => scroll;

        public void Press(Vector2 screen, int id = 0)
        {
            current.RemoveAll(p => p.id == id);
            current.Add(new PointerSample { id = id, position = screen, pressed = true });
        }

        public void Release(int id = 0) => current.RemoveAll(p => p.id == id);
    }
}
