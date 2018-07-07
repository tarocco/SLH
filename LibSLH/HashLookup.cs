using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LibSLH
{
    public class HashLookup<TKey, TElement> : ILookup<TKey, TElement>
    {
        private int _Count = 0;
        private Dictionary<TKey, HashSet<TElement>> _Dictionary = new Dictionary<TKey, HashSet<TElement>>();

        public IEnumerable<TElement> this[TKey key]
        {
            get
            {
                return _Dictionary[key];
            }
        }

        public int Count => _Count;

        public bool Contains(TKey key)
        {
            return _Dictionary.ContainsKey(key);
        }

        public IEnumerator<IGrouping<TKey, TElement>> GetEnumerator()
        {
            return _Dictionary.SelectMany(e => e.Value.GroupBy(v => e.Key, v => v)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Add(TKey key, TElement element)
        {
            if (!_Dictionary.TryGetValue(key, out HashSet<TElement> set))
            {
                set = new HashSet<TElement>();
                _Dictionary[key] = set;
            }
            if (set.Add(element))
            {
                _Count++;
                return true;
            }
            return false;
        }

        public bool Remove(TKey key, TElement element)
        {
            if (!_Dictionary.TryGetValue(key, out HashSet<TElement> set))
                return false;
            if (set.Remove(element))
            {
                _Count--;
                if (set.Count == 0)
                    _Dictionary.Remove(key);
                return true;
            }
            return false;
        }
    }
}