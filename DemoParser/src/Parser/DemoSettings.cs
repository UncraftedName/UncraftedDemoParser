using System;
using System.Diagnostics.CodeAnalysis;
using DemoParser.Parser.Components;
using static DemoParser.Parser.SourceGame;

namespace DemoParser.Parser {
	
	/// <summary>
	/// A class containing several useful constants used while parsing the demo.
	/// </summary>
	public class DemoSettings {

		private readonly DemoHeader _header;
		
		// these seem to be constant in all games
		public const int MaxEdictBits = 11;
		public const int MaxEdicts = 1 << MaxEdictBits;
		public const int SubStringBits = 5;
		
		// initialized below
		public readonly SourceGame Game;
		public readonly int MaxSplitscreenPlayers;
		public readonly int SignOnGarbageBytes;
		public float TickInterval; // to be determined while parsing in SvcServerInfo
		public readonly bool ProcessEnts; // don't try to do entity stuff unless I'm testing or have explicit support
		
		// these can be evaluated using simple expressions
		public bool NewEngine => _header.DemoProtocol == 4;
		public bool HasPlayerSlot => NewEngine; // "alignment byte" in nekz' parser
		public int NetMsgTypeBits => _header.NetworkProtocol == 14 ? 5 : 6;
		public int UserMessageLengthBits => NewEngine && Game != L4D2_2042 ? 12 : 11;
		public int MaxUserDataBits => 14;
		public uint HandleSerialNumberBits => 10;


		public DemoSettings(DemoHeader h) {
			_header = h;
			switch (h.DemoProtocol) {
				case 3 when h.NetworkProtocol == 14:
					Game = PORTAL_1_3420;
					break;
				case 3 when h.NetworkProtocol == 15:
					Game = PORTAL_1_UNPACK;
					break;
				case 3 when h.NetworkProtocol == 24:
					Game = PORTAL_1_STEAMPIPE;
					break;
				case 4 when h.NetworkProtocol == 2001:
					Game = PORTAL_2;
					break;
				case 4 when h.NetworkProtocol == 2000:
					Game = L4D2_2000;
					break;
				case 4 when h.NetworkProtocol == 2042:
					Game = L4D2_2042;
					break;
				default:
					Game = UNKNOWN;
					Console.WriteLine($"\nUnknown game, demo might not parse correctly. Update in {GetType().FullName}.\n");
					break;
			}

			switch (Game) {
				case PORTAL_1_UNPACK:
				case PORTAL_1_3420:
					ProcessEnts = true;
					goto case PORTAL_1_STEAMPIPE;
				case PORTAL_1_STEAMPIPE:
					MaxSplitscreenPlayers = 1;
					SignOnGarbageBytes = 76;
					break;
				case PORTAL_2:
					MaxSplitscreenPlayers = 2;
					SignOnGarbageBytes = 152;
					break;
				case L4D2_2000:
				case L4D2_2042:
					MaxSplitscreenPlayers = 4;
					SignOnGarbageBytes = 304;
					break;
				case UNKNOWN:
					MaxSplitscreenPlayers = 4;
					SignOnGarbageBytes = 152;
					break;
				default:
					throw new Exception("You fool, you absolute buffoon! " +
										"You added a game type but didn't add a case for it!");
			}
#if FORCE_PROCESS_ENTS
			ProcessEnts = true; // be prepared for lots of exceptions
#endif
		}
	}
	
	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public enum SourceGame {
		PORTAL_1_UNPACK,
		PORTAL_1_3420,
		PORTAL_1_STEAMPIPE,
		PORTAL_2,
		L4D2_2000,
		L4D2_2042,
		UNKNOWN
	}
}