using ACadSharp.Classes;

namespace ACadSharp.IO;

/// <summary>
/// Configuration for the <see cref="CadWriterBase{T}"/> class.
/// </summary>
public class CadWriterConfiguration
{
	/// <summary>
	/// The writer will close the stream once the operation is completed.
	/// </summary>
	/// <value>
	/// default: true
	/// </value>
	public bool CloseStream { get; set; } = true;

	/// <summary>
	/// The writer leaves out an object it cannot write instead of failing the whole file.
	/// </summary>
	/// <remarks>
	/// The mirror of <c>CadReaderConfiguration.Failsafe</c>, and it was missing for as long as this
	/// library existed: neither writer had a single catch, so any one object that could not be
	/// written - an evaluation expression carrying a group code nobody implemented, an extended data
	/// record of an unknown type, a visual style property of an unknown kind - threw out of
	/// <c>Write()</c> and the caller got <b>no file at all</b>. That is the worst possible trade:
	/// losing a drawing to keep one object. Off, the exception is raised as before, for a caller who
	/// would rather fail loudly than ship an incomplete drawing.
	/// <para>
	/// Nothing is silent either way. A skipped object is reported through
	/// <c>OnNotification</c> as <see cref="NotificationType.Error"/>, naming the object and carrying
	/// the exception.
	/// </para>
	/// <para>
	/// One exception is never swallowed: <see cref="System.InvalidOperationException"/>, which is how
	/// a writer says the DOCUMENT is inconsistent rather than that the library cannot do something -
	/// a spline carrying a weight for some of its control points and not others, for one. That is
	/// the caller's own data to fix, and hiding it would be the opposite of a favour.
	/// </para>
	/// </remarks>
	/// <value>
	/// default: true
	/// </value>
	public bool Failsafe { get; set; } = true;

	/// <summary>
	/// Resets the <see cref="DxfClass"/> collection in the <see cref="CadDocument"/> before writing it.
	/// </summary>
	/// <remarks>
	/// Sometimes the files are corrupted by badly formed dxf classes, is recommended to keep this flag set.
	/// </remarks>
	/// <value>
	/// default: false
	/// </value>
	public bool ResetDxfClasses { get; set; } = false;

	/// <summary>
	///  Update the blocks that visualize the dimensions in the model space.
	/// </summary>
	/// <remarks>
	/// When creating or modifying a dimension in a block, it needs to be updated in order to appear in the drawing.
	/// </remarks>
	/// <value>
	/// default: false
	/// </value>
	public bool UpdateDimensionsInBlocks { get; set; } = false;

	/// <summary>
	/// Update the blocks that visualize the dimensions in the blocks.
	/// </summary>
	/// <remarks>
	/// The dimensions in the model space are automatically updated by the drawing software, is not recommended to update them if is not needed.
	/// </remarks>
	/// <value>
	/// default: false
	/// </value>
	public bool UpdateDimensionsInModel { get; set; } = false;

	/// <summary>
	/// The writer keeps the dynamic block data: the <see cref="ACadSharp.Objects.Evaluations.EvaluationGraph"/>
	/// of each dynamic block, the representation data of its references and the purge preventer.
	/// </summary>
	/// <remarks>
	/// Off, every dynamic block in the file is saved as a static block: its parameters, actions and
	/// grips are gone and AutoCAD shows a plain block in their place. Seventeen production drawings
	/// taken at random each held between 2 and 25 dynamic blocks, so the default is on. The former
	/// default was off, out of a worry that the graph could corrupt some documents; written on, the
	/// ten dynamic-block samples and all seventeen production drawings audit in AutoCAD exactly as
	/// they did with it off, and AutoCAD keeps the blocks dynamic. Switch it off to get the old
	/// behaviour back for a document that turns out to need it.
	/// </remarks>
	/// <value>
	/// default: true
	/// </value>
	public bool WriteDynamicBlockData { get; set; } = true;

	/// <summary>
	/// The writer will not ignore the <see cref="ACadSharp.Entities.Shape"/> entities in the document.
	/// </summary>
	/// <remarks>
	/// The shape file the entity is drawn with is referenced through its text style; that handle is
	/// written since the fix in <c>writeShape</c>, and AutoCAD audits such a file with no errors for
	/// AC1015 and AC1032. The library still does not read the shx file itself, so it cannot check
	/// that the shape index exists in it.
	/// </remarks>
	/// <value>
	/// default: true
	/// </value>
	public bool WriteShapes { get; set; } = true;

	/// <summary>
	/// The writer will not ignore the <see cref="ACadSharp.XData.ExtendedData"/> collection in the <see cref="CadObject"/>.
	/// </summary>
	/// <value>
	/// default: true
	/// </value>
	public bool WriteXData { get; set; } = true;

	/// <summary>
	/// The writer will not ignore the <see cref="ACadSharp.Objects.XRecord"/> objects in the document.
	/// </summary>
	/// <remarks>
	/// Due the complexity of XRecords, if this flag is set to true, it may cause a corruption of the file.
	/// </remarks>
	/// <value>
	/// default: true
	/// </value>
	public bool WriteXRecords { get; set; } = true;
}