using System;
using System.Collections.Generic;

namespace QuitToDesktop;

internal static class MenuList
{
    internal static List<T> AppendOnce<T>(List<T> original, T entry) where T : class
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        var result = new List<T>(original.Count + 1);
        foreach (T item in original)
        {
            if (item == null) throw new InvalidOperationException("The pause menu contains an empty entry.");
            if (!ReferenceEquals(item, entry)) result.Add(item);
        }
        result.Add(entry);
        return result;
    }
}
