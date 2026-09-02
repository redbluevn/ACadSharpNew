namespace ACadSharp.Tests.IO;

using ACadSharp.IO;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

public class HugeMemoryStreamTests
{
	[Fact]
	public void ReadsStopAtTheLogicalEnd()
	{
		using HugeMemoryStream stream = new HugeMemoryStream(new List<byte[]> { new byte[] { 1, 2, 3 } });
		byte[] buffer = new byte[8];

		Assert.Equal(3, stream.Read(buffer, 0, buffer.Length));
		Assert.Equal(new byte[] { 1, 2, 3 }, buffer[..3]);
		Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
		Assert.Equal(-1, stream.ReadByte());
	}

	[Fact]
	public void SupportsSeekingAndFlushAtTheLogicalEnd()
	{
		using HugeMemoryStream stream = new HugeMemoryStream(new List<byte[]> { new byte[] { 1, 2, 3 } });

		Assert.Equal(3, stream.Seek(0, SeekOrigin.End));
		stream.Flush();
		Assert.Equal(-1, stream.ReadByte());
		Assert.Equal(1, stream.Seek(-2, SeekOrigin.End));
		Assert.Equal(2, stream.ReadByte());
	}

	[Fact]
	public void FixedLengthWritesCannotRunPastTheEnd()
	{
		using HugeMemoryStream stream = new HugeMemoryStream(new List<byte[]> { new byte[] { 1, 2, 3 } });
		stream.Position = 2;

		Assert.Throws<NotSupportedException>(() => stream.Write(new byte[2], 0, 2));
		Assert.Equal(2, stream.Position);
	}
}
