using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using DemoParser.Parser.Components.Abstract;
using DemoParser.Utils;

namespace DemoParser.Parser.Components {
	
	public class MessageStream : DemoComponent, IEnumerable<(MessageType, DemoMessage)> {
		
		public List<(MessageType, DemoMessage)> Messages;
		
		
		public MessageStream(SourceDemo demoRef, BitStreamReader reader) : base(demoRef, reader) {}
		
		
		// this starts on the int before the messages which says the size of the message stream in bytes
		internal override void ParseStream(BitStreamReader bsr) {
			uint messagesByteLength = bsr.ReadUInt();
			BitStreamReader messageBsr = bsr.SubStream(messagesByteLength << 3);
			Messages = new List<(MessageType, DemoMessage)>();
			byte messageValue = 0;
			Exception e = null;
			try {
				do {
					messageValue = (byte)messageBsr.ReadBitsAsUInt(DemoRef.DemoSettings.NetMsgTypeBits);
					MessageType messageType =
						DemoMessage.ByteToSvcMessageType(messageValue, DemoRef.DemoSettings);
					DemoMessage demoMessage = MessageFactory.CreateMessage(DemoRef, messageBsr, messageType);
					demoMessage?.ParseStream(messageBsr);
					Messages.Add((messageType, demoMessage));
				} while (Messages[^1].Item2 != null && messageBsr.BitsRemaining >= DemoRef.DemoSettings.NetMsgTypeBits);
			} catch (Exception ex) {
				Debug.WriteLine(e = ex);
				// if the stream goes out of bounds, that's not a big deal since the messages are skipped over at the end anyway
				(MessageType, DemoMessage) pair = (DemoMessage.ByteToSvcMessageType(messageValue, DemoRef.DemoSettings), null);
				if (Messages.Count == 0)
					Messages.Add(pair);
				else
					Messages[^1] = pair;
			}
			
			#region error logging
			
			MessageType lastKey = Messages[^1].Item1;
			DemoMessage lastValue = Messages[^1].Item2;
			
			if (e != null
				|| !Enum.IsDefined(typeof(MessageType), lastKey)
				|| lastKey == MessageType.Unknown
				|| lastValue == null) 
			{
				var lastNonNopMessage = Messages.FindLast(tuple => tuple.Item1 != MessageType.NetNop && tuple.Item2 != null).Item1;
				lastNonNopMessage = lastNonNopMessage == MessageType.NetNop ? MessageType.Unknown : lastNonNopMessage;
				string errorStr = "error while parsing message stream, " +
								  $"{(Messages.Count > 1 ? $"last non-nop message: {lastNonNopMessage}," : "first message,")} ";
				errorStr += $"{messageBsr.BitsRemaining} bit{(messageBsr.BitsRemaining == 1 ? "" : "s")} left to read, ";
				if (e != null) {
					errorStr += $"exception when parsing {lastKey}";
					errorStr += $"\n\texception: {e.Message}";
				} else if (!Enum.IsDefined(typeof(MessageType), lastKey) || lastKey == MessageType.Unknown) {
					errorStr += $"unknown message value: {messageValue}";
				} else {
					errorStr += $"unimplemented message type - {Messages[^1].Item1}";
				}

				DemoRef.AddError(errorStr);
			}
			
			#endregion
			
			bsr.SkipBytes(messagesByteLength);
			SetLocalStreamEnd(bsr);
		}
		

		internal override void WriteToStreamWriter(BitStreamWriter bsw) {
			throw new NotImplementedException();
		}
		

		internal override void AppendToWriter(IndentedWriter iw) {
			int i = 0;
			while (i < Messages.Count && Messages[i].Item2 != null) {
				iw.Append($"message: {Messages[i].Item1} " +
						  $"({DemoMessage.MessageTypeToByte(Messages[i].Item1, DemoRef.DemoSettings)})");
				if (Messages[i].Item2.MayContainData) {
					iw.AddIndent();
					iw.AppendLine();
					Messages[i].Item2.AppendToWriter(iw);
					iw.SubIndent();
				}
				if (i != Messages.Count - 1)
					iw.AppendLine();
				i++;
			}
			if (i < Messages.Count) {
				iw.Append("more messages remaining... ");
				iw.Append(Enum.IsDefined(typeof(MessageType), Messages[i].Item1)
					? $"type: {Messages[i].Item1}"
					: $"unknown type: {Messages[i].Item1}");
			}
		}
		

		public IEnumerator<(MessageType, DemoMessage)> GetEnumerator() {
			return Messages.GetEnumerator();
		}
		

		IEnumerator IEnumerable.GetEnumerator() {
			return ((IEnumerable)Messages).GetEnumerator();
		}
	}
}