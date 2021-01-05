using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
