#nullable enable
using System;
using System.Numerics;
using DemoParser.Parser.Components.Abstract;
using DemoParser.Parser.HelperClasses;
using DemoParser.Utils;
using DemoParser.Utils.BitStreams;
using static DemoParser.Parser.DemoInfo;

namespace DemoParser.Parser.Components.Messages {
	
	public class SvcSounds : DemoMessage {
		
		public bool Reliable;
		public SoundInfo[]? Sounds;
		
		
		public SvcSounds(SourceDemo? demoRef) : base(demoRef) {}


		protected override void Parse(ref BitStreamReader bsr) {
			Reliable = bsr.ReadBool();
			int soundCount = Reliable ? 1 : bsr.ReadByte();
			int dataBitLen = (int)bsr.ReadBitsAsUInt(Reliable ? 8 : 16);
			
			BitStreamReader soundBsr = bsr.SplitAndSkip(dataBitLen);

			SoundInfo sound = new SoundInfo(DemoRef);
			SoundInfo delta = new SoundInfo(DemoRef);
			delta.SetDefault();
			
			Exception? e = null;
			try {
				Sounds = new SoundInfo[soundCount];
				for (int i = 0; i < soundCount; i++) {
					sound.ParseDelta(ref soundBsr, delta);
					delta = sound;
					if (Reliable) { // client is incrementing the reliable sequence numbers itself
						DemoRef.ClientSoundSequence = ++DemoRef.ClientSoundSequence & SndSeqNumMask;
						if (sound.SequenceNumber != 0)
							throw new ArgumentException($"expected sequence number 0, got: {sound.SequenceNumber}");
						sound.SequenceNumber = DemoRef.ClientSoundSequence;
					}
					Sounds[i] = new SoundInfo(sound);
				}
			} catch (Exception exp) {
				e = exp;
			}
			if (e != null) {
				Sounds = null;
				DemoRef.LogError($"exception while parsing {nameof(SoundInfo)}: {e.Message}");
			} else if (soundBsr.BitsRemaining != 0) {
				Sounds = null;
				DemoRef.LogError($"exception while parsing {nameof(SoundInfo)}: {soundBsr.BitsRemaining} bits left to read");
			}
		}
		
		
		internal override void WriteToStreamWriter(BitStreamWriter bsw) {
			throw new NotImplementedException();
		}
		
		
		public override void PrettyWrite(IPrettyWriter iw) {
			iw.Append($"reliable: {Reliable}");
			if (Sounds != null) {
				for (int i = 0; i < Sounds.Length; i++) {
					iw.AppendLine();
					iw.Append($"sound #{i + 1}:");
					iw.FutureIndent++;
					iw.AppendLine();
					Sounds[i].PrettyWrite(iw);
					iw.FutureIndent--;
				}
			} else {
				iw.AppendLine();
				iw.Append("sound parsing failed");
			}
		}
	}
	
	
	public class SoundInfo : DemoComponent {
		
		public uint EntityIndex;
		public int? SoundNum;
		public uint? ScriptHash;
		public string? SoundName;
		private bool _soundTableReadable;
		public SoundFlags Flags;
		public Channel Chan;
		public bool IsAmbient;
		public bool IsSentence;
		public uint SequenceNumber;
		public float Volume;
		public uint SoundLevel;
		public uint Pitch;
		public int? RandomSeed; // demo protocol 4 only
		public float Delay;
		public Vector3 Origin;
		public int SpeakerEntity;

		private SoundInfo? _deltaTmp;
		
		
		public SoundInfo(SourceDemo? demoRef) : base(demoRef) {}


		public SoundInfo(SoundInfo si) : base(si.DemoRef) {
			EntityIndex = si.EntityIndex;
			SoundNum = si.SoundNum;
			SoundName = si.SoundName;
			_soundTableReadable = si._soundTableReadable;
			Flags = si.Flags;
			Chan = si.Chan;
			IsAmbient = si.IsAmbient;
			IsSentence = si.IsSentence;
			SequenceNumber = si.SequenceNumber;
			Volume = si.Volume;
			SoundLevel = si.SoundLevel;
			Pitch = si.Pitch;
			RandomSeed = si.RandomSeed;
			Delay = si.Delay;
			Origin = si.Origin;
			SpeakerEntity = si.SpeakerEntity;
		}
		
		
		internal void SetDefault() {
			Delay = 0.0f;
			Volume = 1.0f;
			SoundLevel = 75;
			Pitch = 100;
			RandomSeed = DemoInfo.NewDemoProtocol ? 0 : (int?)null;
			EntityIndex = 0;
			SpeakerEntity = -1;
			Chan = Channel.Static;
			SoundNum = 0;
			Flags = SoundFlags.None;
			SequenceNumber = 0;
			IsSentence = false;
			IsAmbient = false;
			Origin = Vector3.Zero;
		}
		
		
		private void ClearStopFields() {
			Volume = 0;
			SoundLevel = 0;
			Pitch = 100;
			SoundName = null;
			Delay = 0.0f;
			SequenceNumber = 0;
			Origin = Vector3.Zero;
			SpeakerEntity = -1;
		}


		public new void ParseStream(ref BitStreamReader bsr) {
			throw new InvalidOperationException();
		}

		
		// ReadDelta(SoundInfo_t *this,SoundInfo_t *delta,bf_read *buf)
		public void ParseDelta(ref BitStreamReader bsr, SoundInfo delta) {
			_deltaTmp = delta;
			base.ParseStream(ref bsr);
			_deltaTmp = null;
		}
		
		
		protected override void Parse(ref BitStreamReader bsr) {
			EntityIndex = bsr.ReadBool() ? bsr.ReadBitsAsUInt(bsr.ReadBool() ? 5 : MaxEdictBits) : _deltaTmp.EntityIndex;
			
#pragma warning disable 8629
			if (DemoInfo.NewDemoProtocol) {
				Flags = (SoundFlags?)bsr.ReadBitsAsUIntIfExists(DemoInfo.SoundFlagBits) ?? _deltaTmp.Flags;
				if ((Flags & SoundFlags.IsScriptHandle) != 0)
					ScriptHash = bsr.ReadUInt();
				else
					SoundNum = (int?)bsr.ReadBitsAsUIntIfExists(MaxSndIndexBits) ?? _deltaTmp.SoundNum;
			} else {
				SoundNum = (int?)bsr.ReadBitsAsUIntIfExists(MaxSndIndexBits) ?? _deltaTmp.SoundNum;
				Flags = (SoundFlags?)bsr.ReadBitsAsUIntIfExists(DemoInfo.SoundFlagBits) ?? _deltaTmp.Flags;
			}
			Chan = (Channel?)bsr.ReadBitsAsUIntIfExists(3) ?? _deltaTmp.Chan;
#pragma warning restore 8629
			
			#region get sound name

			if (SoundNum.HasValue) {
				var mgr = DemoRef.CurStringTablesManager;

				if (mgr.TableReadable.GetValueOrDefault(TableNames.SoundPreCache)) {
					_soundTableReadable = true;
					if (SoundNum >= mgr.Tables[TableNames.SoundPreCache].Entries.Count)
						DemoRef.LogError($"{GetType().Name} - sound index out of range: {SoundNum}");
					else if (SoundNum != 0)
						SoundName = mgr.Tables[TableNames.SoundPreCache].Entries[SoundNum.Value].EntryName;
				}
			}

			#endregion
			
			IsAmbient = bsr.ReadBool();
			IsSentence = bsr.ReadBool();
			
			if (Flags != SoundFlags.Stop) {

				if (bsr.ReadBool())
					SequenceNumber = _deltaTmp.SequenceNumber;
				else if (bsr.ReadBool())
					SequenceNumber = _deltaTmp.SequenceNumber + 1;
				else
					SequenceNumber = bsr.ReadBitsAsUInt(SndSeqNumberBits);
				
				Volume = bsr.ReadBitsAsUIntIfExists(7) / 127.0f ?? _deltaTmp.Volume;
				SoundLevel = bsr.ReadBitsAsUIntIfExists(MaxSndLvlBits) ?? _deltaTmp.SoundLevel;
				Pitch = bsr.ReadBitsAsUIntIfExists(8) ?? _deltaTmp.Pitch;

				if (DemoInfo.NewDemoProtocol) {
					RandomSeed = bsr.ReadBitsAsSIntIfExists(6) ?? _deltaTmp.RandomSeed; // 6, 18, or 29
					Delay = bsr.ReadFloatIfExists() ?? _deltaTmp.Delay;
				} else {
					if (bsr.ReadBool()) {
						Delay = bsr.ReadBitsAsSInt(MaxSndDelayMSecEncodeBits) / 1000.0f;
						if (Delay < 0)
							Delay *= 10.0f;
						Delay -= SndDelayOffset;
					}
					else {
						Delay = _deltaTmp.Delay;
					}
				}

				Origin = new Vector3 {
					X = bsr.ReadBitsAsSIntIfExists(PropDecodeConsts.CoordIntBits - 2) * 8 ?? _deltaTmp.Origin.X,
					Y = bsr.ReadBitsAsSIntIfExists(PropDecodeConsts.CoordIntBits - 2) * 8 ?? _deltaTmp.Origin.Y,
					Z = bsr.ReadBitsAsSIntIfExists(PropDecodeConsts.CoordIntBits - 2) * 8 ?? _deltaTmp.Origin.Z
				};
				SpeakerEntity = bsr.ReadBitsAsSIntIfExists(MaxEdictBits + 1) ?? _deltaTmp.SpeakerEntity;
			} else {
				ClearStopFields();
			}
		}
		
		
		internal override void WriteToStreamWriter(BitStreamWriter bsw) {
			throw new NotImplementedException();
		}
		
		
		public override void PrettyWrite(IPrettyWriter iw) {
			iw.AppendLine($"entity index: {EntityIndex}");

			if ((Flags & SoundFlags.IsScriptHandle) != 0) {
				iw.Append($"scriptable sound hash: {ScriptHash}");
			} else {
				if (_soundTableReadable && SoundName != null)
					iw.Append($"sound: \"{SoundName}\"");
				else
					iw.Append("sound num:");
				iw.AppendLine($" [{SoundNum}]");
			}

			iw.AppendLine($"flags: {Flags}");
			iw.AppendLine($"channel: {Chan}");
			iw.AppendLine($"is ambient: {IsAmbient}");
			iw.AppendLine($"is sentence: {IsSentence}");
			iw.AppendLine($"sequence number: {SequenceNumber}");
			iw.AppendLine($"volume: {Volume}");
			iw.AppendLine($"sound level: {SoundLevel}");
			iw.AppendLine($"pitch: {Pitch}");
			if (DemoInfo.NewDemoProtocol)
				iw.AppendLine($"random seed: {RandomSeed}");
			iw.AppendLine($"origin: {Origin}");
			iw.Append($"speaker entity: {SpeakerEntity}");
		}
		
		
		[Flags]
		public enum SoundFlags : uint {
			None                 = 0,
			ChangeVol            = 1,
			ChangePitch          = 1 << 1,
			Stop                 = 1 << 2,
			Spawning             = 1 << 3, // we're spawning, used in some cases for ambients, not sent over net
			Delay                = 1 << 4,
			StopLooping          = 1 << 5,
			Speaker              = 1 << 6, // being played again by a microphone through a speaker
			ShouldPause          = 1 << 7, // this sound should be paused if the game is paused
			IgnorePhonemes       = 1 << 8,
			IgnoreName           = 1 << 9,
			// not present in portal 1
			IsScriptHandle       = 1 << 10,
			UpdateDelayForChoreo = 1 << 11, // True if we have to update snd_delay_for_choreo with the IO latency
			GenerateGuid         = 1 << 12, // True if we generate the GUID when we send the sound
			OverridePitch        = 1 << 13
		}
		
		
		public enum Channel {
			Replace = -1,
			Auto,
			Weapon,
			Voice,
			Item,
			Body,
			Stream,    // allocate stream channel from the static or dynamic area
			Static,    // allocate channel from the static area 
			VoiceBase, // allocate channel for network voice data
			
			UserBase = VoiceBase + 128 // Anything >= this number is allocated to game code.
		}
	} 
}
