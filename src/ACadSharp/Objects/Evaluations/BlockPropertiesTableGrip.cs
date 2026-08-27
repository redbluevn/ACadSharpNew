using ACadSharp.Attributes;
using ACadSharp.Classes;

namespace ACadSharp.Objects.Evaluations;

/// <summary>
/// Represents a BLOCKPROPERTIESTABLEGRIP object.
/// </summary>
[DxfName(DxfFileToken.ObjectBlockPropertiesTableGrip)]
[DxfSubClass(DxfSubclassMarker.BlockPropertiesTableGrip)]
public class BlockPropertiesTableGrip : BlockGrip, IDxfClassDefined
{
	/// <inheritdoc/>
	public override string ObjectName => DxfFileToken.ObjectBlockPropertiesTableGrip;

	/// <inheritdoc/>
	public override string SubclassMarker => DxfSubclassMarker.BlockPropertiesTableGrip;

	/// <inheritdoc/>
	public DxfClass GetDxfClass()
	{
		return new DxfClass
		{
			CppClassName = DxfSubclassMarker.BlockPropertiesTableGrip,
			DwgVersion = ACadVersion.AC1018,
			DxfName = DxfFileToken.ObjectBlockPropertiesTableGrip,
			ItemClassId = 499,
			MaintenanceVersion = 55,
			ProxyFlags = ProxyFlags.EraseAllowed | ProxyFlags.CloningAllowed | ProxyFlags.DisablesProxyWarningDialog,
			WasZombie = false,
		};
	}
}
