using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jigbot.States
{
    public class HistoryState : ConcurrentDictionary<ulong, ulong>
    {
        public bool Remove(ulong key, out ulong channel)
        {
            return CollectionExtensions.Remove(this, key, out channel);
        }
    }
}
