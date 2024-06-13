namespace ktsu.io.ProjectDirector;

using System.Collections.Generic;
using System.Linq;

public sealed class DictionaryOfHashSets<TKey, TValue> : Dictionary<TKey, HashSet<TValue>> where TKey : notnull
{
	public void Add(TKey key, TValue value)
	{
		if (!TryGetValue(key, out var collection))
		{
			collection = [];
			Add(key, collection);
		}
		collection.Add(value);
	}

	public void Remove(TKey key, TValue value)
	{
		if (TryGetValue(key, out var collection))
		{
			collection.Remove(value);
			if (collection.Count == 0)
			{
				Remove(key);
			}
		}
	}

	public bool Contains(TKey key, TValue value) => TryGetValue(key, out var collection) && collection.Contains(value);

	public bool Contains(TKey key) => ContainsKey(key);

	public bool Contains(TValue value) => Values.Any((HashSet<TValue> collection) => collection.Contains(value));
}
