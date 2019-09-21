using System.Text;
using UncraftedDemoParser.Parser.Components.Abstract;
using UncraftedDemoParser.Utils;

namespace UncraftedDemoParser.Parser.Components.SvcNetMessages {
	
	public class SvcCmdKeyValues : SvcNetMessage {


		public int BufferLength;
		public byte[] Buffer; // todo - verify that this is in bytes
		
		
		public SvcCmdKeyValues(byte[] data, SourceDemo demoRef, int tick) : base(data, demoRef, tick) {}
		
		
		protected override void ParseBytes(BitFieldReader bfr) {
			BufferLength = bfr.Data.Length;
			Buffer = bfr.ReadBytes(BufferLength);
		}


		protected override void PopulatedBuilder(StringBuilder builder) {
			builder.Append($"\t\tbuffer length: {BufferLength}");
		}
	}
}