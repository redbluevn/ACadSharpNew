using ACadSharp.Attributes;
using ACadSharp.Classes;

namespace ACadSharp.Objects.Evaluations;

/// <summary>
/// Represents the actionless proxy expression that AutoCAD stores in some dynamic-block
/// evaluation graphs.
/// </summary>
/// <remarks>
/// The object has no payload beyond <see cref="EvaluationExpression"/>. Keeping it as a real
/// expression preserves the graph node handle when a drawing is written as DXF or DWG.
/// </remarks>
[DxfName(DxfFileToken.ObjectDynamicBlockProxyNode)]
public class DynamicBlockProxyNode : EvaluationExpression, IDxfClassDefined
{
	/// <inheritdoc/>
	public override string ObjectName => DxfFileToken.ObjectDynamicBlockProxyNode;

	/// <inheritdoc/>
	public DxfClass GetDxfClass()
	{
		return new DxfClass
		{
			CppClassName = "AcDbDynamicBlockProxyNode",
			DwgVersion = ACadVersion.AC1018,
			DxfName = DxfFileToken.ObjectDynamicBlockProxyNode,
			ItemClassId = 499,
			MaintenanceVersion = 20,
			ProxyFlags = ProxyFlags.EraseAllowed | ProxyFlags.CloningAllowed | ProxyFlags.DisablesProxyWarningDialog,
			WasZombie = false,
		};
	}
}
