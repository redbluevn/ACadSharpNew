namespace ACadSharp.IO
{
	/// <summary>
	/// Configuration for reading DWG files.
	/// </summary>
	public class DwgReaderConfiguration : CadReaderConfiguration
	{
		/// <summary>
		/// Use the Standard Cycling Redundancy Check to verify the integrity of the file, default value is set to false.
		/// </summary>
		/// <remarks>
		/// DWG file format uses a modification of a standard Cyclic Redundancy Check as an error detecting mechanism, 
		/// if this flag is enabled the reader will perform this verification to detect any possible error, but it will greatly increase the reading time.
		/// </remarks>
		public bool CrcCheck { get; set; } = false;

		/// <summary>
		/// If set to false the reader will skip the summary info section.
		/// </summary>
		/// <value>
		/// default: true
		/// </value>
		public bool ReadSummaryInfo { get; set; } = true;

		/// <summary>
		/// If set to true the reader will skip the proxy graphics section.
		/// </summary>
		/// <value>
		/// default: true
		/// </value>
		public bool IgnoreProxyGraphics { get; set; } = true;

		/// <summary>
		/// If set to true the reader reports every object whose data stream it did not consume
		/// exactly, default value is set to false.
		/// </summary>
		/// <remarks>
		/// A DWG object is length delimited, so a reader that stops short of the end, or runs past
		/// it into the string stream, is not punished by the format: the next object is found by
		/// offset either way and the mistake is silent. Every field the reader gets wrong from that
		/// point on is silent too. Turning this on makes the reader say so, and it is how a missing
		/// or surplus field is found without knowing in advance which one it is.
		///
		/// Reported from R2000 on, which is every version whose object header names the boundary:
		/// R2010 and later carry it in the header itself, and R2000 through R2007 give it as the
		/// size of the pre-handles section. It is off by default: the unimplemented classes a
		/// drawing happens to carry each raise one, so the count is a property of the drawing as
		/// much as of the reader.
		/// </remarks>
		public bool ReportUnreadObjectBits { get; set; } = false;
	}
}
