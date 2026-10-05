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
        // (화면 버튼·창 위에서 시작한 손가락은 뗄 때까지 섬으로 보내지 않는다)
        readonly HashSet<int> onUI = new HashSet<int>(), down = new HashSet<int>();

        public void Collect(List<PointerSample> into)
        {
            into.Clear();
            var now = new HashSet<int>();
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    int id = t.touchId.ReadValue(); now.Add(id);
                    if (!down.Contains(id) && UI.GameUI.PointerOverUI(id)) onUI.Add(id);
                    if (!onUI.Contains(id)) into.Add(new PointerSample { id = id, position = t.position.ReadValue(), pressed = true });
                }
            }
            var m = Mouse.current;
            if (now.Count == 0 && m != null && m.leftButton.isPressed)
            {
                now.Add(-1);
                if (!down.Contains(-1) && UI.GameUI.PointerOverUI()) onUI.Add(-1);
                if (!onUI.Contains(-1)) into.Add(new PointerSample { id = -1, position = m.position.ReadValue(), pressed = true });
            }
            onUI.IntersectWith(now); down.Clear(); down.UnionWith(now);
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
