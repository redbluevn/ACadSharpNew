using ACadSharp.Classes;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.XData;
using System;
using System.Collections.Generic;

namespace ACadSharp.IO.Templates;

internal abstract class CadTemplate : ICadObjectTemplate
{
	public CadObject CadObject { get; set; }

	public DxfClass DxfClass { get; set; }

	//A template is built for every object in the file and almost none of them carry extended data,
	//so these two dictionaries were created empty nearly a million times per drawing. They are
	//created on the first access instead, which is what a reader that has extended data to store
	//does anyway.
	public Dictionary<ulong, List<ExtendedDataRecord>> EDataTemplate
	{
		get { return this._eDataTemplate ??= new(); }
		set { this._eDataTemplate = value; }
	}

	public Dictionary<string, List<ExtendedDataRecord>> EDataTemplateByAppName
	{
		get { return this._eDataTemplateByAppName ??= new(); }
		set { this._eDataTemplateByAppName = value; }
	}

	private Dictionary<ulong, List<ExtendedDataRecord>> _eDataTemplate;

	private Dictionary<string, List<ExtendedDataRecord>> _eDataTemplateByAppName;

	public bool HasBeenBuilt { get; private set; } = false;

	public ulong? OwnerHandle { get; set; }

	//Same story as the extended data dictionaries above: one set per template, and most objects
	//have no reactors at all.
	public HashSet<ulong> ReactorsHandles
	{
		get { return this._reactorsHandles ??= new(); }
		set { this._reactorsHandles = value; }
	}

	private HashSet<ulong> _reactorsHandles;

	public ulong? XDictHandle { get; set; }

	public CadTemplate(CadObject cadObject)
	{
		this.CadObject = cadObject;
	}

	public void Build(CadDocumentBuilder builder)
	{
		if (this.HasBeenBuilt)
		{
			return;
		}
		else
		{
			this.HasBeenBuilt = true;
		}

		this.build(builder);

		if (this.DxfClass != null && this.CadObject is not IDxfClassDefined)
		{
			switch (this.CadObject)
			{
				case ProxyEntity:
				case UnknownEntity:
				case UnknownNonGraphicalObject:
					break;
				default:
					builder.Notify($"{this.CadObject.GetType().FullName} does not implement {nameof(IDxfClassDefined)}", NotificationType.Warning);
					break;
			}
		}

		builder.NotifyProgress(ReadStage.Build, this);
	}

	public virtual CadObjectData GetObjectData()
	{
		return new CadObjectData(this.CadObject, this.OwnerHandle, this.XDictHandle);
	}

	public override string ToString()
	{
		return $"{this.CadObject?.ToString()}";
	}

	protected virtual void build(CadDocumentBuilder builder)
	{
		if (builder.TryGetCadObject(this.XDictHandle, out CadDictionary cadDictionary))
		{
			this.CadObject.XDictionary = cadDictionary;
		}

		//These three collections are read through their fields, not their properties: going through
		//a property would build the collection the loop is about to find empty, for every object in
		//the file. The null check is a plain if rather than a fallback to an empty sequence, so the
		//non-empty case keeps the struct enumerator instead of boxing one per template - that
		//difference measured 300 ms on a drawing with 909,016 objects.
		if (this._reactorsHandles != null)
		{
			//A reactor handle that resolves to nothing is stale - the object it named was deleted and
			//the back-reference was not. AutoCAD drops those silently on open (checked against its own
			//save of a production drawing: none of 150 such handles names any object in it). Say it
			//once per object, not once per handle.
			int stale = 0;
			ulong firstStale = 0;
			foreach (ulong handle in this._reactorsHandles)
			{
				if (builder.TryGetCadObject(handle, out CadObject reactor))
				{
					this.CadObject.AddReactor(reactor);
				}
				else
				{
					if (stale == 0)
					{
						firstStale = handle;
					}

					stale++;
				}
			}

			if (stale > 0)
			{
				builder.Notify(
					$"{this.CadObject.GetType().Name} {this.CadObject.Handle}: {stale} of {this._reactorsHandles.Count} reactors refer to objects that are not in the drawing (first: {firstStale}); dropped, as AutoCAD does",
					NotificationType.Warning);
			}
		}

		if (this._eDataTemplate != null)
		{
			foreach (var item in this._eDataTemplate)
			{
				if (builder.TryGetCadObject(item.Key, out AppId app))
				{
					this.CadObject.ExtendedData.Add(app, item.Value);
				}
				else
				{
					builder.Notify($"AppId in extended data with handle {item.Key} not found", NotificationType.Warning);
				}
			}
		}

		if (this._eDataTemplateByAppName != null)
		{
			foreach (var item in this._eDataTemplateByAppName)
			{
				if (builder.TryGetTableEntry(item.Key, out AppId app))
				{
					this.CadObject.ExtendedData.Add(app, item.Value);
				}
				else
				{
					builder.Notify($"AppId in extended data with handle {item.Key} not found", NotificationType.Warning);
				}
			}
		}
	}

	protected IEnumerable<T> getEntitiesCollection<T>(CadDocumentBuilder builder, ulong firstHandle, ulong endHandle)
		where T : Entity
	{
		CadEntityTemplate template = builder.GetObjectTemplate<CadEntityTemplate>(firstHandle);

		if (template == null)
		{
			builder.Notify($"Leading entity with handle {firstHandle} not found.", NotificationType.Warning);
			template = builder.GetObjectTemplate<CadEntityTemplate>(endHandle);
		}

		while (template != null)
		{
			yield return (T)template.CadObject;

			if (template.CadObject.Handle == endHandle)
			{
				break;
			}

			if (template.NextEntity.HasValue)
			{
				template = builder.GetObjectTemplate<CadEntityTemplate>(template.NextEntity.Value);
			}
			else
			{
				template = builder.GetObjectTemplate<CadEntityTemplate>(template.CadObject.Handle + 1);
			}
		}
	}

	protected bool getTableReference<T>(CadDocumentBuilder builder, ulong? handle, string name, out T reference)
		where T : TableEntry
	{
		if (builder.TryGetCadObject<T>(handle, out reference) || builder.TryGetTableEntry<T>(name, out reference))
		{
			return true;
		}
		else
		{
			if (!string.IsNullOrEmpty(name) || (handle.HasValue && handle.Value != 0))
			{
				builder.Notify($"{typeof(T).FullName} table reference with handle: {handle} | name: {name} not found for {this.CadObject.GetType().FullName} with handle {this.CadObject.Handle}", NotificationType.Warning);
			}

			return false;
		}
	}
}