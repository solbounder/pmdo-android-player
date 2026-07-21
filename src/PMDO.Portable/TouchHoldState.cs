using System.Collections.Generic;
using System.Linq;

namespace PMDO.Portable;

public sealed class TouchHoldState<TSource, TBinding>
    where TSource : notnull
    where TBinding : notnull
{
    private readonly Dictionary<TSource, TBinding> sources = new();
    private readonly Dictionary<TBinding, int> counts = new();

    public bool Press(TSource source, TBinding binding)
    {
        if (sources.ContainsKey(source)) return false;
        sources[source] = binding;
        counts.TryGetValue(binding, out int count);
        counts[binding] = count + 1;
        return count == 0;
    }

    public bool Release(TSource source, out TBinding binding)
    {
        if (!sources.Remove(source, out binding!)) return false;
        int count = counts[binding];
        if (count > 1)
        {
            counts[binding] = count - 1;
            return false;
        }
        counts.Remove(binding);
        return true;
    }

    public IReadOnlyList<TBinding> Reset()
    {
        TBinding[] active = counts.Keys.ToArray();
        sources.Clear();
        counts.Clear();
        return active;
    }
}
