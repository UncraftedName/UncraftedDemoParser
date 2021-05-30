using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using DemoParser.Parser;
using DemoParser.Parser.Components;
using DemoParser.Parser.Components.Abstract;
using DemoParser.Parser.Components.Messages;
using DemoParser.Parser.Components.Messages.UserMessages;
using DemoParser.Parser.Components.Packets;
using DemoParser.Parser.HelperClasses.EntityStuff;
using DemoParser.Utils;
using DemoParser.Utils.BitStreams;
using static System.Text.RegularExpressions.RegexOptions;
using static DemoParser.Utils.ParserTextUtils;

namespace ConsoleApp {
	
	public static partial class ConsoleFunctions {

		private static string? _folderPath;
		private static TextWriter? _curTextWriter;
		private static BinaryWriter? _curBinWriter;
		// I only save all demos if I need to
		private static readonly List<SourceDemo> Demos = new List<SourceDemo>();
		private static SourceDemo CurDemo => Demos[^1];
		private static readonly List<(int ticks, int adjustedTicks)> DemoTickCounts = new List<(int, int)>();
		private static int _runnableOptionCount;
		private static bool _displayMapExcludedMsg;


		private static void SetTextWriter(string suffix, int bufferSize = 1024) {
			if (_folderPath != null) {
				_curTextWriter?.Flush(); // i can't flush this after it's been closed
				_curTextWriter?.Close();
				_curTextWriter = new StreamWriter(CreateFileStream(suffix), Encoding.UTF8, bufferSize);
			} else {
				_curTextWriter = Console.Out;
			}
		}


		private static FileStream CreateFileStream(string suffix) =>
			new FileStream(Path.Combine(_folderPath!, $"{CurDemo.FileName![..^4]} - {suffix}.txt"), FileMode.Create);


		// if changing how the output is named, change the tests as well
		private static void SetBinaryWriter(string suffix, string fileFormat) {
			if (_folderPath == null)
				throw new InvalidOperationException($"The option for '{suffix}' requires the folder option to be set.");
			_curBinWriter?.Flush();
			_curBinWriter?.Close();
			_curBinWriter = new BinaryWriter(new FileStream(
				Path.Combine(_folderPath, $"{CurDemo.FileName![..^4]}-{suffix}.{fileFormat}"), FileMode.Create));
		}


		private static int CurDemoHeaderQuickHash() {
			DemoHeader h = CurDemo.Header;
			//return HashCode.Combine(h.GameDirectory, h.NetworkProtocol, h.DemoProtocol);
			return HashCode.Combine(h.NetworkProtocol, h.DemoProtocol);
		}

		
		private static string FormatTime(double seconds) {
			if (Math.Abs(seconds) > 8640000) // 10 days, probably happens from initializing the tick interval to garbage 
				return "invalid";
			string sign = seconds < 0 ? "-" : "";
			// timespan truncates values, (makes sense since 0.5 minutes is still 0 total minutes) so I round manually
			TimeSpan t = TimeSpan.FromSeconds(Math.Abs(seconds));
			int roundedMilli = t.Milliseconds + (int)Math.Round(t.TotalMilliseconds - (long)t.TotalMilliseconds);
			if (t.Days > 0)
				return $"{sign}{t.Days:D1}.{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}.{roundedMilli:D3}";
			else if (t.Hours > 0)
				return $"{sign}{t.Hours:D1}:{t.Minutes:D2}:{t.Seconds:D2}.{roundedMilli:D3}";
			else if (t.Minutes > 0)
				return $"{sign}{t.Minutes:D1}:{t.Seconds:D2}.{roundedMilli:D3}";
			else
				return $"{sign}{t.Seconds:D1}.{roundedMilli:D3}";
		}
		
		
		#region ConsFunc
		

		private static void ConsFunc_VerboseDump() {
			Console.WriteLine("Dumping verbose output...");
			var fs = CreateFileStream("verbose dump");
			CurDemo.WriteVerboseString(fs, disposeAfterWriting: true);
		}


		private static void ConsFunc_ListDemo(
			ListdemoOption listdemoOption,
			bool printWriteMsg,
			bool exceptionDuringParsing,
			bool printFileName)
		{
			SetTextWriter("listdemo+");
			if (printWriteMsg && _runnableOptionCount > 1)
				Console.WriteLine("Writing listdemo+ output...");
			ConsoleColor originalColor = Console.ForegroundColor;

			Console.ForegroundColor = ConsoleColor.Gray;
			if (listdemoOption == ListdemoOption.DisplayHeader) {
				DemoHeader h = CurDemo.Header;
				if (printFileName)
					_curTextWriter!.WriteLine($"{"File name",-25}: {CurDemo.FileName}");
				_curTextWriter!.Write(
					$"{"Demo protocol",    -25}: {h.DemoProtocol}"               +
					$"\n{"Network protocol", -25}: {h.NetworkProtocol}"          +
					$"\n{"Server name",      -25}: {h.ServerName}"               +
					$"\n{"Client name",      -25}: {h.ClientName}"               +
					$"\n{"Map name",         -25}: {h.MapName}"                  +
					$"\n{"Game directory",   -25}: {h.GameDirectory}"            +
					$"\n{"Playback time",    -25}: {FormatTime(h.PlaybackTime)}" +
					$"\n{"Ticks",            -25}: {h.TickCount}"                +
					$"\n{"Frames",           -25}: {h.FrameCount}"               +
					$"\n{"SignOn Length",    -25}: {h.SignOnLength}\n\n");
			}
			if (exceptionDuringParsing) // only header
				return;
			
			if (!CurDemo.FilterForPacket<Packet>().Any()) {
				ConsoleWriteWithColor("This demo is janked yo, it doesn't have any packets!", ConsoleColor.Red);
				return;
			}

			(string str, Regex re)[] customRe = {
				("Autosave",                new Regex(@"^\s*autosave\s*$",                IgnoreCase)),
				("Autosavedangerous",       new Regex(@"^\s*autosavedangerous\s*$",       IgnoreCase)),
				("Autosavedangerousissafe", new Regex(@"^\s*autosavedangerousissafe\s*$", IgnoreCase))
			};
			
			var customGroups = customRe.SelectMany(
					t => CurDemo.CmdRegexMatches(t.re),
					(t, matchTup) => (t.str, matchTup.cmd.Tick))
				.OrderBy(reInfo => reInfo.Tick)
				.GroupBy(reInfo => reInfo.str, reInfo => reInfo.Tick)
				.OrderBy(grouping => grouping.Min())
				.ToList();

			Regex flagRe = new Regex(@"^\s*echo\s+#(?<flag_name>.*)#\s*$", IgnoreCase);

			var flagGroups = CurDemo.CmdRegexMatches(flagRe)
				.OrderBy(t => t.cmd.Tick)
				.GroupBy(t => t.matches[0].Groups["flag_name"].Value.ToUpper(), t => t.cmd.Tick)
				.OrderBy(grouping => grouping.Min())
				.ToList();

			double tickInterval = CurDemo.DemoInfo.TickInterval;

			List<int> ticks = customGroups.SelectMany(g => g)
				.Concat(flagGroups.SelectMany(g => g)).ToList();

			List<string>
				descriptions = customGroups.Select(g => Enumerable.Repeat(g.Key, g.Count()))
					.Concat(flagGroups.Select(g => Enumerable.Repeat($"'{g.Key}' flag", g.Count())))
					.SelectMany(e => e).ToList(),
				tickStrs = ticks.Select(i => i.ToString()).ToList(),
				times = ticks.Select(i => FormatTime(i * tickInterval)).ToList();

			if (descriptions.Any()) {
				int maxDescPad = descriptions.Max(s => s.Length),
					tickMaxPad = tickStrs.Max(s => s.Length),
					timeMadPad = times.Max(s => s.Length);

				int customCount = customGroups.SelectMany(g => g).Count();
				string previousDesc = null!;
				for (int i = 0; i < descriptions.Count; i++) {
					if (i < customCount) {
						Console.ForegroundColor = ConsoleColor.DarkYellow;
					} else {
						if (previousDesc == null || descriptions[i] != previousDesc) {
							Console.ForegroundColor = Console.ForegroundColor == ConsoleColor.Yellow
								? ConsoleColor.Magenta
								: ConsoleColor.Yellow;
						}
					}
					previousDesc = descriptions[i];
					_curTextWriter!.WriteLine($"{descriptions[i].PadLeft(maxDescPad)} on tick " +
											 $"{tickStrs[i].PadLeft(tickMaxPad)}, time: {times[i].PadLeft(timeMadPad)}");
				}
				_curTextWriter!.WriteLine();
			}
			
			Console.ForegroundColor = ConsoleColor.Cyan;
			_curTextWriter!.WriteLine(
				$"{"Measured time ",  -25}: {FormatTime((CurDemo.TickCount() - 1) * tickInterval)}" +
				$"\n{"Measured ticks ", -25}: {CurDemo.TickCount() - 1}");

			if (CurDemo.TickCount() != CurDemo.AdjustedTickCount()) {
				_curTextWriter!.WriteLine(
					$"{"Adjusted time ",-25}: {FormatTime((CurDemo.AdjustedTickCount() - 1) *  tickInterval)}");
				_curTextWriter!.Write($"{"Adjusted ticks ",-25}: {CurDemo.AdjustedTickCount() - 1}");
				_curTextWriter!.WriteLine($" ({CurDemo.StartAdjustmentTick}-{CurDemo.EndAdjustmentTick})");
			}

			
			/*_curTextWriter.WriteLine();
			// THIS IS JUST FOR THE CHALLENGE
			var ticksWithPortals = CurDemo.AdjustedTickCount() - 1;
			var portals = CurDemo.CmdRegexMatches("#ORANGE#|#BLUE#").Count();
			if (portals <= 2)
				ticksWithPortals += (int)(2 / 0.015);
			else
				ticksWithPortals += portals * 190;
			_curTextWriter.WriteLine($"{"Adjusted time with portals ",-28}: {FormatTime(ticksWithPortals *  tickInterval)}");
			_curTextWriter.WriteLine($"{"Adjusted ticks with portals ",-28}: {ticksWithPortals}");*/
			

			if (_displayMapExcludedMsg)
				ConsoleWriteWithColor("(This map will be excluded from the total time)\n", ConsoleColor.DarkCyan);

			Console.ForegroundColor = originalColor;
			
			if (listdemoOption == ListdemoOption.DisplayHeader && _curTextWriter! == Console.Out)
				Console.WriteLine();
		}

		
		private static void ConsFunc_DisplayTotalTime() {
			ConsoleColor originalColor = Console.ForegroundColor;
			
			if (DemoTickCounts.Any(tuple => tuple.ticks == int.MinValue)) { // some exception was thrown
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("There was an issue while calculating the total time, timing may be incorrect.");
			}
			
			Console.ForegroundColor = ConsoleColor.Green;
			
			int totalTicks = DemoTickCounts
				.Where(tuple => tuple.ticks != int.MinValue)
				.Select(tuple => tuple.ticks - 1)
				.Sum();
			int totalAdjustedTicks = DemoTickCounts
				.Where(tuple => tuple.adjustedTicks != int.MinValue)
				.Select(tuple => tuple.adjustedTicks - 1)
				.Sum();
			float tickInterval = CurDemo.DemoInfo.TickInterval;
			Console.WriteLine($"\n{"Total measured time ",  -25}: {FormatTime(totalTicks * tickInterval)}");
			Console.WriteLine($"{"Total measured ticks ",   -25}: {totalTicks}");
			if (totalTicks != totalAdjustedTicks) {
				Console.WriteLine($"{"Total adjusted time ",  -25}: {FormatTime(totalAdjustedTicks * tickInterval)}");
				Console.WriteLine($"{"Total adjusted ticks ", -25}: {totalAdjustedTicks}");
			}
			
			Console.ForegroundColor = originalColor;
		}


		private static void ConsFunc_RegexSearch(string pattern) {
			if (_runnableOptionCount > 1)
				Console.Write("Finding regex matches...");
			List<ConsoleCmd> matches = CurDemo.CmdRegexMatches(pattern).Select(t => t.cmd).ToList();
			if (matches.Count == 0) {
				Console.WriteLine(" no matches found");
			} else {
				if (_runnableOptionCount > 1)
					Console.WriteLine();
				SetTextWriter("regex matches");
				matches.ForEach(cmd => {_curTextWriter!.WriteLine($"[{cmd.Tick}] {cmd.Command}");});
			}
		}


		private static void ConsFunc_DumpJumps() {
			SetTextWriter("jump dump");
			if (_runnableOptionCount > 1)
				Console.Write("Finding jumps...");
			List<ConsoleCmd> matches = CurDemo.CmdRegexMatches("[-+]jump").Select(t => t.cmd).ToList();
			if (matches.Count == 0) {
				const string s = "no jumps found";
				Console.WriteLine($" {s}");
				if (_curTextWriter! != Console.Out)
					_curTextWriter!.WriteLine(s);
			} else {
				Console.WriteLine();
				matches.ForEach(cmd => {_curTextWriter!.WriteLine($"[{cmd.Tick}] {cmd.Command}");});
			}
		}


		// add exceptions for some commands (like sv_funnel, sv_unloackedchapters, etc.)
		private static void ConsFunc_DumpCheats() {
			SetTextWriter("cheat dump");
			if (_runnableOptionCount > 1)
				Console.WriteLine("Finding cheats...");
			List<ConsoleCmd> matches = CurDemo.CmdRegexMatches(new Regex(new[] {
				"host_timescale", "god", "sv_cheats +1", "buddha", "host_framerate", "sv_accelerate", "gravity", 
				"sv_airaccelerate", "noclip", "impulse", "ent_", "sv_gravity", "upgrade_portalgun", 
				"phys_timescale", "notarget", "give", "fire_energy_ball", "_spt_", "plugin_load", 
				"!picker", "(?<!swit)ch_", "tas_", "r_", "sv_", "mat_"
			}.SequenceToString(")|(", "(", ")"), IgnoreCase)).Select(t => t.cmd).ToList();
			if (matches.Count == 0) {
				const string s = "no cheats found";
				Console.WriteLine($" {s}");
				if (_curTextWriter != Console.Out)
					_curTextWriter!.WriteLine(s);
			} else {
				Console.WriteLine();
				matches.ForEach(cmd => {_curTextWriter!.WriteLine($"[{cmd.Tick}] {cmd.Command}");});
			}
		}


		private static void ConsFunc_RemoveCaptions() {
			if (_runnableOptionCount > 1)
				Console.Write("Removing captions...");
			
			Packet[] closeCaptionPackets = CurDemo.FilterForPacket<Packet>()
				.Where(packet => packet.FilterForMessage<SvcUserMessage>()
					.Any(frame => frame.MessageType == UserMessageType.CloseCaption)).ToArray();

			if (closeCaptionPackets.Length == 0) {
				Console.WriteLine(" no captions found");
				return;
			}
			SetBinaryWriter("captions_removed", "dem");
			Console.WriteLine();
			
			int changedPackets = 0;
			_curBinWriter!.Write(CurDemo.Header.Reader.ReadRemainingBits().bytes);
			
			foreach (PacketFrame frame in CurDemo.Frames) {
				if (frame.Packet != closeCaptionPackets[changedPackets]) {
					_curBinWriter!.Write(frame.Reader.ReadRemainingBits().bytes); // write frames that aren't changed
				} else {
					Packet p = (Packet)frame.Packet;
					BitStreamWriter bsw = new BitStreamWriter(frame.Reader.ByteLength);
					var last = p.MessageStream.Last().message;
					int len = last.Reader.AbsoluteStart - frame.Reader.AbsoluteStart + last.Reader.BitLength;
					bsw.WriteBits(frame.Reader.ReadBits(len), len);
					int msgSizeOffset = p.MessageStream.Reader.AbsoluteStart - frame.Reader.AbsoluteStart;
					int typeInfoLen = CurDemo.DemoInfo.NetMsgTypeBits + CurDemo.DemoInfo.UserMessageLengthBits + 8;
					bsw.RemoveBitsAtIndices(p.FilterForUserMessage<CloseCaption>()
						.Select(caption => (caption.Reader.AbsoluteStart - frame.Reader.AbsoluteStart - typeInfoLen, 
							caption.Reader.BitLength + typeInfoLen)));
					bsw.WriteUntilByteBoundary();
					bsw.EditIntAtIndex((bsw.BitLength - msgSizeOffset - 32) >> 3, msgSizeOffset, 32);
					_curBinWriter!.Write(bsw.AsArray);
					
					// if we've edited all the packets, write the rest of the data in the demo
					if (++changedPackets == closeCaptionPackets.Length) {
						BitStreamReader tmp = CurDemo.Reader;
						tmp.SkipBits(frame.Reader.AbsoluteStart + frame.Reader.BitLength);
						_curBinWriter!.Write(tmp.ReadRemainingBits().bytes);
						break;
					}
				}
			}
		}


		private static void ConsFunc_DumpDataTables(DataTablesDispType dispType) {
			SetTextWriter("data tables dump");
			if (_runnableOptionCount > 1)
				Console.WriteLine("Dumping data tables...");
			foreach ((DataTables tables, int j) in CurDemo.FilterForPacket<DataTables>().Select((tables, i) => (tables, i))) {
				if (j > 0)
					_curTextWriter!.Write("\n\n\n");
				if (dispType == DataTablesDispType.Default) {
					_curTextWriter!.WriteLine(tables.ToString());
				} else {
					var tableParser = new DataTableParser(CurDemo, tables);
					tableParser.FlattenClasses();
					foreach ((ServerClass sClass, List<FlattenedProp> fProps) in tableParser.FlattenedProps!) {
						_curTextWriter!.WriteLine($"{sClass.ClassName} ({sClass.DataTableName}) ({fProps.Count} props):");
						for (var i = 0; i < fProps.Count; i++) {
							FlattenedProp fProp = fProps[i];
							_curTextWriter!.Write($"\t({i}): ");
							_curTextWriter!.Write(fProp.TypeString().PadRight(12));
							_curTextWriter!.WriteLine(fProp.ArrayElementPropInfo == null
								? fProp.PropInfo.ToStringNoType()
								: fProp.ArrayElementPropInfo.ToStringNoType());
						}
					}
				}
				_curTextWriter!.Write("\n\nClass hierarchy:\n\n");
				_curTextWriter!.Write(new DataTableTree(tables, true));
			}
		}


		private static void ConsFunc_DumpActions(ActPressedDispType opt) {
			SetTextWriter("actions pressed");
			if (_runnableOptionCount > 1)
				Console.WriteLine("Getting actions pressed...");
			foreach (UserCmd userCmd in CurDemo.FilterForPacket<UserCmd>()) {
				_curTextWriter!.Write($"[{userCmd.Tick}] ");
				if (!userCmd.Buttons.HasValue) {
					_curTextWriter!.WriteLine("null");
					continue;
				}
				Buttons b = userCmd.Buttons.Value;
				switch (opt) {
					case ActPressedDispType.AsTextFlags:
						_curTextWriter!.WriteLine(b.ToString());
						break;
					case ActPressedDispType.AsInt:
						_curTextWriter!.WriteLine((uint)b);
						break;
					case ActPressedDispType.AsBinaryFlags:
						_curTextWriter!.WriteLine(Convert.ToString((uint)b, 2).PadLeft(32, '0'));
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(opt), opt, "unsupported buttons pressed option");
				}
			}
		}


		private static void ConsFunc_PortalsPassed(PtpFilterType filterType) {
			SetTextWriter("portals passed");
			if (_runnableOptionCount > 1)
				Console.Write("Finding passing through portals...");
			bool playerOnly = filterType == PtpFilterType.PlayerOnly || filterType == PtpFilterType.PlayerOnlyVerbose;
			bool verbose = filterType == PtpFilterType.PlayerOnlyVerbose || filterType == PtpFilterType.AllEntitiesVerbose;
			var msgList = CurDemo.FilterForUserMessage<EntityPortalled>().ToList();
			if (playerOnly)
				msgList = msgList.Where(tuple => tuple.userMessage.Portalled.EntIndex == 1).ToList();
			if (msgList.Any()) {
				if (_runnableOptionCount > 1)
					Console.WriteLine();
				foreach ((EntityPortalled userMessage, int tick) in msgList) {
					_curTextWriter!.Write($"[{tick}]");
					_curTextWriter!.WriteLine(verbose
						? $"\n{userMessage.ToString()}"
						: $" entity {userMessage.Portalled} went through portal {userMessage.Portal}");
				}
			} else {
				string s = playerOnly ? "player never went through a portal" : "no entities went through portals";
				Console.WriteLine($" {s}");
				if (_curTextWriter! != Console.Out)
					_curTextWriter!.WriteLine(s);
			}
		}


		/*private static void ConsFunc_Errors() {
			if (_runnableOptionCount > 1)
				Console.Write("Getting errors during parsing... ");
			if (CurDemo.ErrorList.Count == 0) {
				Console.WriteLine("no errors");
			} else {
				Console.WriteLine();
				SetTextWriter("errors");
				CurDemo.ErrorList.ForEach(s => _curTextWriter!.WriteLine(s));
			}
		}*/


		private static void ConsFunc_PatchJumps() {
			SetBinaryWriter("patchjumps", "dem");
			
			var jumpTicks =
				(from msgTup in CurDemo.FilterForMessage<SvcPacketEntities>()
					from update in msgTup.message.Updates
					where update.ServerClass.ClassName == "CPortal_Player" && update is Delta
					from deltaProp in ((Delta)update).Props
					where deltaProp.prop.Name == "m_Local.m_flJumpTime" &&
						  ((SingleEntProp<float>)deltaProp.prop).Value == 510
					select msgTup.tick).ToList();

			Console.WriteLine($"detected jump ticks: {jumpTicks.SequenceToString()}");
			
			BitStreamWriter bsw = new BitStreamWriter(CurDemo.Reader.Data);

			const int maxTicksBeforeJump = 5;
			const int maxTicksBeyondJump = 1;
			const float interpThreshold = 15; // this many units off the ground is considered a jump

			var frames = CurDemo.Frames;
			foreach (int jumpTick in jumpTicks) {
				
				int jumpTickIdx = 0;
				for (; frames[jumpTickIdx].Type != PacketType.Packet; jumpTickIdx++); // find first packet
				while (frames[++jumpTickIdx].Tick < jumpTick); // find the first frame with the matching tick
				for (; frames[jumpTickIdx].Type != PacketType.Packet; jumpTickIdx++); // find the packet on this tick
				
				Packet groundPacket = (Packet)frames[jumpTickIdx].Packet!;
				Vector3 groundPos = groundPacket.PacketInfo[0].ViewOrigin; // roughly the ground pos

				int endTick = -1;
				Vector3 endVec = new Vector3(float.PositiveInfinity);
				bool shouldInterp = true;
				
				int idx = jumpTickIdx;
				for (int ticksBeyondJump = 0; ticksBeyondJump < maxTicksBeyondJump; ticksBeyondJump++) {
					
					while (frames[++idx].Type != PacketType.Packet) ; // find the next next packet
					
					Packet curPacket = (Packet)frames[idx].Packet!;
					Vector3 curViewOrigin = curPacket.PacketInfo[0].ViewOrigin;
					
					if (curViewOrigin.Z - groundPos.Z > interpThreshold) {
						endTick = curPacket.Tick;
						endVec = curViewOrigin;
						break;
					}

					if (ticksBeyondJump == maxTicksBeyondJump - 1) {
						shouldInterp = false;
						Console.WriteLine($"not patching jump on tick {jumpTick}, too many ticks before air time detected");
						break;
					}
				}
				
				if (!shouldInterp)
					continue;

				Vector3 startVec = new Vector3(float.PositiveInfinity);
				
				idx = jumpTickIdx;
				for (int ticksBeforeJump = 0; ticksBeforeJump < maxTicksBeforeJump; ticksBeforeJump++) {
					
					while (frames[--idx].Type != PacketType.Packet); // find the previous packet
					
					Packet curPacket = (Packet)frames[idx].Packet!;
					Vector3 curViewOrigin = curPacket.PacketInfo[0].ViewOrigin;
					
					if (curViewOrigin.Z - groundPos.Z > interpThreshold) {
						startVec = curViewOrigin;
						break;
					}

					if (ticksBeforeJump == maxTicksBeforeJump - 1) {
						shouldInterp = false;
						Console.WriteLine($"not patching jump on tick {jumpTick}, too many ticks on ground (at least {maxTicksBeforeJump}) before this jump");
						break;
					}
				}
				if (!shouldInterp)
					continue;
				
				int startTick = frames[idx].Tick; // idx is currently the start tick (right before the first interp tick)
				
				while (frames[idx].Tick < endTick) {
					
					while (frames[++idx].Type != PacketType.Packet) ; // look for next packet
					
					BitStreamWriter simpleVecBytes = new BitStreamWriter();
					Packet packet = (Packet)frames[idx].Packet!;
					float lerpAmount = (float)(frames[idx].Tick - startTick) / (endTick - startTick);
					Vector3 lerpVec = Vector3.Lerp(startVec, endVec, lerpAmount);
					simpleVecBytes.WriteVector3(lerpVec);
					// edit the pos in the cmdinfo
					bsw.EditBitsAtIndex(simpleVecBytes, packet.Reader.AbsoluteStart + 32);
					// edit the pos in the entity delta
					var originProp = (SingleEntProp<Vector3>?)(
							(Delta?)packet.FilterForMessage<SvcPacketEntities>().Single().Updates!
								.SingleOrDefault(update => update.ServerClass.ClassName == "CPortal_Player"))
						?.Props.SingleOrDefault(tuple => tuple.prop.Name == "m_vecOrigin").prop;
					if (originProp != null) {
						bool tryWrite = true;
						while (tryWrite) {
							tryWrite = false;
							BitStreamWriter newOriginBytes = new BitStreamWriter();
							newOriginBytes.WriteBitCoord(originProp.Value.X);
							newOriginBytes.WriteBitCoord(originProp.Value.Y);
							newOriginBytes.WriteBitCoord(lerpVec.Z);
							// only edit the pos if the new value is encoded with the same number of bits
							switch (newOriginBytes.BitLength - originProp.BitLength) {
								case 0:
									bsw.EditBitsAtIndex(newOriginBytes, originProp.Offset, originProp.BitLength);
									break;
								case 5:
									lerpVec.Z = (float)Math.Round(lerpVec.Z);
									tryWrite = true;
									break;
								case -5:
									lerpVec.Z += 0.04f;
									tryWrite = true;
									break;
								default:
									Console.WriteLine($"bit length doesn't match, new value uses {newOriginBytes.BitLength} bits but old value has {originProp.BitLength} bits");
									break;
							}
						}
					}
					
					var viewOffsetProp = (SingleEntProp<float>?)(
							(Delta?)packet.FilterForMessage<SvcPacketEntities>().Single().Updates!
								.SingleOrDefault(update => update.ServerClass.ClassName == "CPortal_Player"))
						?.Props.SingleOrDefault(tuple => tuple.prop.Name == "m_vecViewOffset[2]").prop;

					if (viewOffsetProp != null) {
						BitStreamWriter viewOffsetBytes = new BitStreamWriter();
						viewOffsetBytes.WriteFloat(28);
						bsw.EditBitsAtIndex(viewOffsetBytes, viewOffsetProp.Offset);
					}
				}
			}
			_curBinWriter!.Write(bsw);
			Console.WriteLine("done");
		}


		private static void ConsFunc_ChangeDemoDir(string dir) {
			Console.Write("Changing demo dir");
			string old = CurDemo.Header.GameDirectory;
			if (CurDemo.Header.GameDirectory == dir) {
				Console.WriteLine("... demo directories match!");
			} else {
				Console.Write($@" from ""{old}"" to ""{dir}""... ");
				SetBinaryWriter("changed_dir", "dem");
				int lenDiff = dir.Length - old.Length;
				BitStreamReader bsr = CurDemo.Reader;
				byte[] dirBytes = Encoding.ASCII.GetBytes(dir);
				
				_curBinWriter!.Write(bsr.ReadBytes(796));
				_curBinWriter!.Write(dirBytes); // header doesn't matter but I change it anyway
				_curBinWriter!.Write(new byte[260 - dir.Length]);
				bsr.SkipBytes(260);
				_curBinWriter!.Write(bsr.ReadBytes(12));
				// change header signOn byteCount; this number might be wrong if the endianness is big but whatever
				_curBinWriter!.Write((uint)(bsr.ReadUInt() + lenDiff)); 

				foreach (SignOn signOn in CurDemo.FilterForPacket<SignOn>().Where(signOn => signOn.FilterForMessage<SvcServerInfo>().Any())) {
					// catch up to signOn packet
					int byteCount = (signOn.Reader.AbsoluteBitIndex - bsr.AbsoluteBitIndex) / 8;
					_curBinWriter!.Write(bsr.ReadBytes(byteCount));
					bsr.SkipBits(signOn.Reader.BitLength);
					
					BitStreamWriter bsw = new BitStreamWriter();
					BitStreamReader signOnReader = signOn.Reader;
					bsw.WriteBits(signOnReader.ReadRemainingBits());
					signOnReader = signOnReader.FromBeginning();
					int bytesToMessageStreamSize = CurDemo.DemoInfo.SignOnGarbageBytes + 8;
					signOnReader.SkipBytes(bytesToMessageStreamSize);
					// edit the message stream length - read uint, and edit at index before the reading of said uint
					bsw.EditIntAtIndex((int)(signOnReader.ReadUInt() + lenDiff), signOnReader.CurrentBitIndex - 32, 32);
					
					// actually change the game dir
					SvcServerInfo serverInfo = signOn.FilterForMessage<SvcServerInfo>().Single();
					int editIndex = serverInfo.Reader.AbsoluteBitIndex - signOn.Reader.AbsoluteBitIndex + 186 + CurDemo.DemoInfo.SvcServerInfoUnknownBits;
					bsw.RemoveBitsAtIndex(editIndex, old.Length * 8);
					bsw.InsertBitsAtIndex(dirBytes, editIndex, dir.Length * 8);
					_curBinWriter!.Write(bsw.AsArray);
				}
				_curBinWriter!.Write(bsr.ReadRemainingBits().bytes);
				
				Console.WriteLine("done.");
			}
		}


		private static void ConsFunc_Pauses() {
			if (_runnableOptionCount > 1)
				Console.Write("Finding pauses...");
			
			var pauses =
				CurDemo.FilterForPacket<Packet>()
					.SelectMany(p => p.MessageStream,
						(p, msgs) => (msgs.message, p.Tick))
					.Where(tuple => tuple.message.GetType() == typeof(SvcSetPause))
					.Select(tuple => (message: (SvcSetPause)tuple.message, tuple.Tick))
					.Where(tuple => tuple.message.IsPaused)
					.ToList();
			
			if (pauses.Any()) {
				if (_runnableOptionCount > 1)
					Console.WriteLine();
				SetTextWriter("pauses");
				foreach ((_, int tick) in pauses) {
					if (tick > CurDemo.EndAdjustmentTick)
						_curTextWriter!.Write("(after last adjusted tick) ");
					else if (tick < CurDemo.StartAdjustmentTick)
						_curTextWriter!.Write("(before first adjusted tick) ");
					_curTextWriter!.WriteLine($"paused on tick {tick}");
				}
			} else {
				Console.WriteLine(" no pauses found");
			}
			
		}
		
		
		// todo add portals shot to csv


		private static void ConsFunc_LinkDemos() { // when this sets the writer, give it a custom name
			Console.WriteLine("Link demos feature not implemented yet.");
		}
		
		
		#endregion
	}
}