#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoParser.Parser.Components.Messages;
using DemoParser.Parser.Components.Packets;
using DemoParser.Parser.Components.Packets.StringTableEntryTypes;
using DemoParser.Utils.BitStreams;

namespace DemoParser.Parser.HelperClasses {

	public static class TableNames {
		public const string Downloadables 		= "downloadables";
		public const string ModelPreCache 		= "modelprecache";
		public const string GenericPreCache 	= "genericprecache";
		public const string SoundPreCache 		= "soundprecache";
		public const string DecalPreCache 		= "decalprecache";
		public const string InstanceBaseLine 	= "instancebaseline";
		public const string LightStyles 		= "lightstyles";
		public const string UserInfo 			= "userinfo";
		public const string ServerQueryInfo 	= "server_query_info";
		public const string ParticleEffectNames = "ParticleEffectNames";
		public const string EffectDispatch 		= "EffectDispatch";
		public const string VguiScreen 			= "VguiScreen";
		public const string Materials 			= "Materials";
		public const string InfoPanel 			= "InfoPanel";
		public const string Scenes 				= "Scenes";
		public const string MeleeWeapons 		= "MeleeWeapons";
		public const string GameRulesCreation 	= "GameRulesCreation";
		public const string BlackMarket 		= "BlackMarketTable";
	}
	

	// Keeps the original string tables passed here untouched, and keeps a separate "current copy"
	// since the tables can be updated/modified as the demo runs from SvcUpdateStringTable.
	internal class C_StringTablesManager { // todo any object taken from the tables should not be taken until the tables are updated for that tick
		
		// the list here can be updated and is meant to be separate from the list in the stringtables packet
		private readonly SourceDemo _demoRef;
		private readonly List<C_StringTable> _privateTables;
		internal readonly Dictionary<string, C_StringTable> Tables;
		
		// Before accessing any values in the tables, check to see if the respective table is readable first,
		// make sure to use GetValueOrDefault() - this will ensure that even if the corresponding SvcCreationStringTable
		// message didn't parse there won't be an exception.
		internal readonly Dictionary<string, bool> TableReadable;
		
		// I store this for later if there's a string tables packet, so that I can create the tables from this list
		// and the packet instead of the create message.
		internal readonly List<SvcCreateStringTable> CreationLookup;

		internal C_StringTablesManager(SourceDemo demoRef) {
			_demoRef = demoRef;
			_privateTables = new List<C_StringTable>();
			Tables = new Dictionary<string, C_StringTable>();
			TableReadable = new Dictionary<string, bool>();
			CreationLookup = new List<SvcCreateStringTable>();
		}


		internal void CreateStringTable(SvcCreateStringTable creationInfo) {
			CreationLookup.Add(creationInfo);
			TableReadable[creationInfo.Name] = true;
			InitNewTable(_privateTables.Count, creationInfo);
		}


		private C_StringTable InitNewTable(int id, SvcCreateStringTable creationInfo) {
			var table = new C_StringTable(id, creationInfo);
			_privateTables.Add(table);
			Tables[creationInfo.Name] = table;
			TableReadable[creationInfo.Name] = true;
			return table;
		}


		internal void ClearCurrentTables() {
			Tables.Clear();
			TableReadable.Clear();
			_privateTables.Clear();
			CreationLookup.Clear();
		}


		internal C_StringTable TableById(int id) {
			return _privateTables[id]; // should be right™
		}


		internal C_StringTableEntry AddTableEntry(C_StringTable table, BitStreamReader entryStream, string entryName) {
			if (!TableReadable[table.Name])
				return null;
			table.Entries.Add(new C_StringTableEntry(_demoRef, table, entryStream, entryName));
			return table.Entries[^1];
		}


		private void AddTableClass(C_StringTable table, string name, string? data) {
			if (!TableReadable[table.Name]) 
				return;
			var stc = new C_StringTableClass(name, data);
			table.Classes.Add(stc);
		}


		internal C_StringTableEntry SetEntryData(C_StringTable table, C_StringTableEntry entry, BitStreamReader entryStream) {
			if (!TableReadable[table.Name])
				return null;
			entry.EntryData = StringTableEntryDataFactory.CreateData(_demoRef, entryStream, table.Name, entry.EntryName);
			return entry;
		}


		internal void CreateTablesFromPacket(StringTables tablesPacket) {
			_privateTables.Clear();
			TableReadable.Clear();
			try {
				foreach (StringTable table in tablesPacket.Tables) {
					int tableId = CreationLookup.FindIndex(info => info.Name == table.Name);
					C_StringTable newTable = InitNewTable(tableId, CreationLookup[tableId]);
					table.MaxEntries = newTable.MaxEntries;
					if (table.TableEntries != null)
						foreach (StringTableEntry entry in table.TableEntries)
							AddTableEntry(newTable, entry?.EntryData?.Reader, entry?.Name);
					if (table.Classes != null)
						foreach (C_StringTableClass tableClass in newTable.Classes)
							AddTableClass(newTable, tableClass.Name, tableClass.Data);
				}
			} catch (Exception e) {
				_demoRef.AddError($"error while converting tables packet to c_tables: {e.Message}");
				TableReadable.Keys.ToList().ForEach(s => TableReadable[s] = false);
			}
		}
	}


	// classes separate from the StringTables packet for managing updateable tables, c stands for current
	
	
	public class C_StringTable {

		public int Id; // the index in the table list
		// flattened fields from SvcCreateStringTable
		public readonly string Name;
		public readonly ushort MaxEntries;
		public readonly bool UserDataFixedSize;
		public readonly int UserDataSize;
		public readonly int UserDataSizeBits;
		public readonly StringTableFlags? Flags;
		// string table fields
		public readonly List<C_StringTableEntry> Entries;
		public readonly List<C_StringTableClass> Classes;


		public C_StringTable(int id, SvcCreateStringTable creationInfo) {
			Id = id;
			Name = creationInfo.Name;
			MaxEntries = creationInfo.MaxEntries;
			UserDataFixedSize = creationInfo.UserDataFixedSize;
			UserDataSize = creationInfo.UserDataSize;
			UserDataSizeBits = (int)creationInfo.UserDataSizeBits;
			Flags = creationInfo.Flags;
			Entries = new List<C_StringTableEntry>();
			Classes = new List<C_StringTableClass>();
		}


		public override string ToString() {
			return Name;
		}
	}


	public class C_StringTableEntry {
		
		private readonly SourceDemo _demoRef;
		private readonly C_StringTable _tableRef;
		public readonly string EntryName;
		public StringTableEntryData? EntryData;
		
		
		public C_StringTableEntry(SourceDemo demoRef, C_StringTable tableRef, BitStreamReader entryStream, string entryName) {
			_demoRef = demoRef;
			_tableRef = tableRef;
			EntryName = entryName;
			if (entryStream != null) {
				EntryData = StringTableEntryDataFactory.CreateData(demoRef, entryStream, tableRef.Name, entryName, demoRef?.DataTableParser?.FlattenedProps);
				EntryData.ParseOwnStream();
			}
		}


		public override string ToString() {
			return EntryName;
		}
	}


	public class C_StringTableClass {
		
		public string Name;
		public string? Data;
		
		
		public C_StringTableClass(string name, string? data) {
			Name = name;
			Data = data;
		}


		public override string ToString() {
			return Name;
		}
	}
}