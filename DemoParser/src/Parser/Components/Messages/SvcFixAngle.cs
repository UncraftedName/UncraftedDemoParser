using System.Numerics;
using DemoParser.Parser.Components.Abstract;
using DemoParser.Utils;
using DemoParser.Utils.BitStreams;

namespace DemoParser.Parser.Components.Messages {
	
	public class SvcFixAngle : DemoMessage {
		
		public bool Relative;
		public Vector3 Angle;
		
		public SvcFixAngle(SourceDemo demoRef, BitStreamReader reader) : base(demoRef, reader) {}


		internal override void ParseStream(BitStreamReader bsr) {
			Relative = bsr.ReadBool();
			Angle = new Vector3 {
				X = bsr.ReadBitAngle(16),
				Y = bsr.ReadBitAngle(16),
				Z = bsr.ReadBitAngle(16),
			};
			SetLocalStreamEnd(bsr);
		}
		

		internal override void WriteToStreamWriter(BitStreamWriter bsw) {
			throw new System.NotImplementedException();
		}


		internal override void AppendToWriter(IndentedWriter iw) {
			iw.AppendLine($"relative: {Relative}");
			iw.Append($"Angle: <{Angle.X:F4}°, {Angle.Y:F4}°, {Angle.Z:F4}°>");
		}
	}
}