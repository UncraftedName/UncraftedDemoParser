using System;
using System.Diagnostics;
using UncraftedDemoParser.Parser.Components.Abstract;

namespace UncraftedDemoParser.Parser.Components.Packets {
	
	// unknown data
	public class SyncTick : DemoPacket {

		public SyncTick(byte[] data, SourceDemo demoRef, int tick) : base(data, demoRef, tick) {}


		protected override void ParseBytes() {
			Debug.WriteLine("sync tick not parsable yet");
		}

		public override void UpdateBytes() {
			Debug.WriteLine("sync tick not parsable yet");
		}


		public override string ToString() {
			return BitConverter.ToString(Bytes).Replace("-", " ").ToLower();
		}
	}
}