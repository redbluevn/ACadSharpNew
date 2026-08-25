namespace ACadSharp.Objects
{
	/// <summary>
	/// Identifies which part of an entity a subentity reference points at.
	/// </summary>
	/// <remarks>
	/// The values are AutoCAD's own <c>AcDb::SubentType</c>, from the ObjectARX SDK header
	/// <c>acdb.h</c>:
	/// <code>
	/// enum { kNullSubentType = 0, kFaceSubentType = 1, kEdgeSubentType = 2, kVertexSubentType = 3,
	///        kMlineSubentCache = 4, kClassSubentType = 5, kAxisSubentType = 6,
	///        kSilhouetteSubentType = 7 };
	/// </code>
	/// This enum used to carry <c>Edge = 1</c> and <c>Face = 2</c>, which is the two names the wrong
	/// way round, and stopped at 2. The swap was visible in real data long before the source was
	/// found: every associative dimension of a client drawing attached to a <see cref="Entities.Line"/>
	/// or an <see cref="Entities.LwPolyline"/> and reported <c>Face</c>, and a line has no face. The
	/// stored value was 2, which is an edge.
	///
	/// No file was ever affected. Both the DWG and DXF paths read the value as a short and cast it,
	/// and write it back the same way, so the number in the file always round-tripped; only the name
	/// a caller saw was wrong.
	/// </remarks>
	public enum SubentType : short
	{
		/// <summary>
		/// No subentity - <c>kNullSubentType</c>.
		/// </summary>
		None = 0,

		/// <summary>
		/// A face - <c>kFaceSubentType</c>.
		/// </summary>
		Face = 1,

		/// <summary>
		/// An edge - <c>kEdgeSubentType</c>.
		/// </summary>
		Edge = 2,

		/// <summary>
		/// A vertex - <c>kVertexSubentType</c>.
		/// </summary>
		Vertex = 3,

		/// <summary>
		/// The mline-specific subentity cache - <c>kMlineSubentCache</c>.
		/// </summary>
		MlineSubentCache = 4,

		/// <summary>
		/// A subentity identified by class rather than by type - <c>kClassSubentType</c>.
		/// </summary>
		Class = 5,

		/// <summary>
		/// An axis - <c>kAxisSubentType</c>.
		/// </summary>
		Axis = 6,

		/// <summary>
		/// A silhouette - <c>kSilhouetteSubentType</c>.
		/// </summary>
		Silhouette = 7,
	}
}
