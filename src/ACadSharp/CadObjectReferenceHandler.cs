using CSUtilities.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp;

internal class CadObjectReferenceHandler<TKey, TValue>
{
	private readonly Dictionary<TKey, HashSet<ReferenceHolder>> _references = new();
	//Link additional assignments through their holders; the common one-property entity needs
	//no separate per-owner collection. Static assignment delegates avoid capturing each entity.
	private readonly Dictionary<CadObject, ReferenceHolder> _referencesByOwner = new();

	public void AddReference(TKey key, CadObject owner, Action<TValue> assignTo)
	{
		this.addReference(key, owner, assignTo, null);
	}

	public void AddReference(TKey key, CadObject owner, Action<CadObject, TValue> assignToOwner)
	{
		this.addReference(key, owner, null, assignToOwner);
	}

	private void addReference(TKey key, CadObject owner, Action<TValue> assignTo, Action<CadObject, TValue> assignToOwner)
	{
		this._referencesByOwner.TryGetValue(owner, out ReferenceHolder first);
		ReferenceHolder holder = first;
		while (holder != null && (holder.AssignTo != assignTo || holder.AssignToOwner != assignToOwner)) holder = holder.NextByOwner;
		if (holder == null)
		{
			holder = new ReferenceHolder(owner, assignTo, assignToOwner) { NextByOwner = first };
			this._referencesByOwner[owner] = holder;
		}
		else if (EqualityComparer<TKey>.Default.Equals(holder.Key, key)) return;
		else this.unlinkKey(holder);

		holder.Key = key;
		if (!this._references.TryGetValue(key, out HashSet<ReferenceHolder> holders))
		{
			holders = new HashSet<ReferenceHolder>();
			this._references[key] = holders;
		}
		holders.Add(holder);
	}

	public void RemoveReference(TKey key, CadObject owner)
	{
		this._referencesByOwner.TryGetValue(owner, out ReferenceHolder holder);
		while (holder != null)
		{
			ReferenceHolder next = holder.NextByOwner;
			if (EqualityComparer<TKey>.Default.Equals(holder.Key, key)) this.removeHolder(holder);
			holder = next;
		}
	}

	public void RemoveReference(CadObject owner, Action<TValue> assignTo)
	{
		this.removeAssignment(owner, assignTo, null);
	}

	public void RemoveReference(CadObject owner, Action<CadObject, TValue> assignToOwner)
	{
		this.removeAssignment(owner, null, assignToOwner);
	}

	private void removeAssignment(CadObject owner, Action<TValue> assignTo, Action<CadObject, TValue> assignToOwner)
	{
		this._referencesByOwner.TryGetValue(owner, out ReferenceHolder holder);
		while (holder != null)
		{
			ReferenceHolder next = holder.NextByOwner;
			if (holder.AssignTo == assignTo && holder.AssignToOwner == assignToOwner) this.removeHolder(holder);
			holder = next;
		}
	}

	public void ChangeKey(TKey current, TKey newKey)
	{
		if (this._references.Remove(current, out HashSet<ReferenceHolder> holders))
		{
			foreach (ReferenceHolder holder in holders) holder.Key = newKey;
			if (this._references.TryGetValue(newKey, out HashSet<ReferenceHolder> existing)) existing.UnionWith(holders);
			else this._references[newKey] = holders;
		}
	}

	public IEnumerable<CadObject> GetReferences(TKey key)
	{
		if (this._references.TryGetValue(key, out HashSet<ReferenceHolder> holders)) return holders.Select(h => h.Owner).Distinct();
		return Enumerable.Empty<CadObject>();
	}

	public void RemoveReference(TKey key, TValue value)
	{
		if (this._references.Remove(key, out HashSet<ReferenceHolder> holders))
		{
			//Detach every owner before callbacks, which may reenter through setters and register
			//the replacement entry. Those fresh registrations must remain intact.
			foreach (ReferenceHolder holder in holders) this.unlinkOwner(holder);
			foreach (ReferenceHolder holder in holders) holder.Assign(value);
		}
	}

	private void removeHolder(ReferenceHolder holder)
	{
		this.unlinkKey(holder);
		this.unlinkOwner(holder);
	}

	private void unlinkKey(ReferenceHolder holder)
	{
		if (this._references.TryGetValue(holder.Key, out HashSet<ReferenceHolder> holders))
		{
			holders.Remove(holder);
			if (holders.Count == 0) this._references.Remove(holder.Key);
		}
	}

	private void unlinkOwner(ReferenceHolder holder)
	{
		if (!this._referencesByOwner.TryGetValue(holder.Owner, out ReferenceHolder first)) return;
		if (first == holder)
		{
			if (holder.NextByOwner == null) this._referencesByOwner.Remove(holder.Owner);
			else this._referencesByOwner[holder.Owner] = holder.NextByOwner;
		}
		else
		{
			ReferenceHolder previous = first;
			while (previous != null && previous.NextByOwner != holder) previous = previous.NextByOwner;
			if (previous != null) previous.NextByOwner = holder.NextByOwner;
		}
		holder.NextByOwner = null;
	}

	private sealed class ReferenceHolder
	{
		public Action<TValue> AssignTo { get; }
		public Action<CadObject, TValue> AssignToOwner { get; }
		public CadObject Owner { get; }
		public TKey Key { get; set; }
		public ReferenceHolder NextByOwner { get; set; }

		public ReferenceHolder(CadObject owner, Action<TValue> assignTo, Action<CadObject, TValue> assignToOwner)
		{
			this.Owner = owner;
			this.AssignTo = assignTo;
			this.AssignToOwner = assignToOwner;
		}

		public void Assign(TValue value)
		{
			if (this.AssignToOwner != null) this.AssignToOwner(this.Owner, value);
			else this.AssignTo(value);
		}
	}
}
