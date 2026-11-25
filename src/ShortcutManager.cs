using System;
using System.Collections.Generic;
using System.Linq;

namespace Hepta
{
    internal static class Chord
    {
        public static bool Valid(int key, int mod)
        {
            return mod > 0 && mod < 16 && ((key >= 65 && key <= 90) || (key >= 48 && key <= 57) || (key >= 112 && key <= 135));
        }

        public static string Format(int key, int mod)
        {
            var parts = new List<string>();
            if ((mod & 2) != 0)
                parts.Add("Ctrl");
            if ((mod & 1) != 0)
                parts.Add("Alt");
            if ((mod & 4) != 0)
                parts.Add("Shift");
            if ((mod & 8) != 0)
                parts.Add("Win");
            parts.Add(key >= 112 ? "F" + (key - 111) : ((char)key).ToString());
            return string.Join("+", parts);
        }
    }

    internal class ShortcutManager
    {
        readonly Func<int, int, int, bool> register;
        readonly Action<int> release;
        readonly Dictionary<int, int> ids = new Dictionary<int, int>();
        int counter = 100, editHandle;
        public ShortcutManager(Func<int, int, int, bool> acquire, Action<int> free)
        {
            register = acquire;
            release = free;
        }

        public bool Apply(int c, int cm, int e, int em)
        {
            if (!Chord.Valid(c, cm) || !Chord.Valid(e, em))
                return false;
            int first = (cm << 8) | c, second = (em << 8) | e;
            if (first == second)
                return false;
            var added = new List<int>();
            foreach (int chord in new[]
            {
                first,
                second
            }

            )
            {
                if (ids.ContainsKey(chord))
                    continue;
                int id = NextId();
                if (!register(id, chord >> 8, chord & 255))
                {
                    foreach (int rollback in added)
                    {
                        release(ids[rollback]);
                        ids.Remove(rollback);
                    }

                    return false;
                }

                ids.Add(chord, id);
                added.Add(chord);
            }

            foreach (int old in ids.Keys.Where(k => k != first && k != second).ToArray())
            {
                release(ids[old]);
                ids.Remove(old);
            }

            editHandle = ids[second];
            return true;
        }

        int NextId()
        {
            do
            {
                counter = counter >= 0xbfff ? 101 : counter + 1;
            }
            while (ids.ContainsValue(counter));
            return counter;
        }

        public bool IsEdit(int id)
        {
            return id == editHandle;
        }

        public void Clear()
        {
            foreach (int id in ids.Values)
                release(id);
            ids.Clear();
        }
    }
}
