// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.ProjectDirector;

using System.Collections.Generic;
using System.Linq;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public sealed class DictionaryOfHashSets<TKey, TValue> : Dictionary<TKey, HashSet<TValue>> where TKey : notnull
{
	public void Add(TKey key, TValue value)
	{
		if (!TryGetValue(key, out HashSet<TValue>? collection))
		{
			collection = [];
			Add(key, collection);
		}

		collection.Add(value);
	}

	public void Remove(TKey key, TValue value)
	{
		if (TryGetValue(key, out HashSet<TValue>? collection))
		{
			collection.Remove(value);
			if (collection.Count == 0)
			{
				Remove(key);
			}
		}
	}

	public bool Contains(TKey key, TValue value) => TryGetValue(key, out HashSet<TValue>? collection) && collection.Contains(value);

	public bool Contains(TKey key) => ContainsKey(key);

	public bool Contains(TValue value) => Values.Any(collection => collection.Contains(value));
}
