namespace ACadSharp.Tests.IO.DWG;

using ACadSharp.IO.DWG;
using System.IO;
using Xunit;

public class CrcStreamHandlerTests
{
	[Fact]
	public void Crc32HashesOnlyBytesActuallyRead()
	{
		byte[] data = { 1, 2 };
		CRC32StreamHandler shortRead = new CRC32StreamHandler(new MemoryStream(data), 0);
		CRC32StreamHandler exactRead = new CRC32StreamHandler(new MemoryStream(data), 0);

		Assert.Equal(2, shortRead.Read(new byte[4], 0, 4));
		Assert.Equal(2, exactRead.Read(new byte[2], 0, 2));
		Assert.Equal(exactRead.Seed, shortRead.Seed);
	}

	[Fact]
	public void Crc8HashesOnlyBytesActuallyRead()
	{
		byte[] data = { 1, 2 };
		CRC8StreamHandler shortRead = new CRC8StreamHandler(new MemoryStream(data), 0xC0C1);
		CRC8StreamHandler exactRead = new CRC8StreamHandler(new MemoryStream(data), 0xC0C1);

		Assert.Equal(2, shortRead.Read(new byte[4], 0, 4));
		Assert.Equal(2, exactRead.Read(new byte[2], 0, 2));
		Assert.Equal(exactRead.Seed, shortRead.Seed);
	}
}
