#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DemoParser.Parser.Components.Abstract;
using DemoParser.Parser.HelperClasses;
using DemoParser.Utils;
using DemoParser.Utils.BitStreams;

namespace DemoParser.Parser.Components.Messages {
	
	public class SvcUpdateStringTable : DemoMessage {

		public byte TableId;
		public string? TableName; // i want to keep a copy of this if I convert to string later
		public ushort ChangedEntriesCount;
		public StringTableUpdates TableUpdates;


		public SvcUpdateStringTable(SourceDemo demoRef, BitStreamReader reader) : base(demoRef, reader) {}
		
		
		internal override void ParseStream(BitStreamReader bsr) {
			TableId = (byte)bsr.ReadBitsAsUInt(5);
			TableName = DemoRef.CurStringTablesManager.TableById(TableId).Name;
			ChangedEntriesCount = bsr.ReadUShortIfExists() ?? 1;
			uint dataLen = bsr.ReadBitsAsUInt(20);

			TableUpdates = new StringTableUpdates(DemoRef, bsr.SubStream(dataLen), TableName, ChangedEntriesCount);
			TableUpdates.ParseOwnStream();
			
			bsr.SkipBits(dataLen);
			SetLocalStreamEnd(bsr);
		}
		

		internal override void WriteToStreamWriter(BitStreamWriter bsw) {
			throw new NotImplementedException();
		}


		public override void AppendToWriter(IndentedWriter iw) {
			iw.Append(TableName != null ? $"table: {TableName}" : "table id:");
			iw.AppendLine($" ({TableId})");
			iw.AppendLine($"number of changed entries: {ChangedEntriesCount}");
			iw.Append("table update:");
			iw.FutureIndent++;
			iw.AppendLine();
			TableUpdates.AppendToWriter(iw);
			iw.FutureIndent--;
		}
	}
	


	public class StringTableUpdates : DemoComponent {
		
		private readonly int _updatedEntries; // just used when parsing
		private readonly string _tableName;
		private bool _exceptionWhileParsing;
		public readonly List<TableUpdate?> TableUpdates;
		
		public static implicit operator List<TableUpdate?>(StringTableUpdates u) => u.TableUpdates;
		
		
		
		public StringTableUpdates(SourceDemo demoRef, BitStreamReader reader, string tableName, int updatedEntries)
			: base(demoRef, reader)
		{
			_tableName = tableName;
			_updatedEntries = updatedEntries;
			TableUpdates = new List<TableUpdate?>(_updatedEntries);
		}
		
		internal override void ParseStream(BitStreamReader bsr) { 
			
			CurStringTablesManager manager = DemoRef.CurStringTablesManager;
			if (!manager.TableReadable.GetValueOrDefault(_tableName)) {
				DemoRef.LogError($"{_tableName} table is marked as non-readable, can't update :/");
				_exceptionWhileParsing = true;
				return;
			}

			if (manager.CreationLookup.Single(table => table.TableName == _tableName).Flags == StringTableFlags.Fake) {
				DemoRef.LogError($"{_tableName} table was created manually - not parsed in SvcServerInfo");
				_exceptionWhileParsing = true;
				return;
			}
			
			try { // se2007/engine/networkstringtable.cpp  line 595
				CurStringTable tableToUpdate = manager.Tables[_tableName];
				int entryIndex = -1;
				List<string> history = new List<string>();

				for (int i = 0; i < _updatedEntries; i++) {
					
					entryIndex++;
					if (!bsr.ReadBool()) {
						if (DemoRef.Header.NetworkProtocol > 14) // i'm actually not sure if this is where this goes
							throw new NotImplementedException("encoded with dictionary");
						entryIndex = (int)bsr.ReadBitsAsUInt(BitUtils.HighestBitIndex(tableToUpdate.MaxEntries));
					}

					string? entryName = null;
					if (bsr.ReadBool()) {
						if (bsr.ReadBool()) { // the first part of the string may be the same as for other entries
							int index = (int)bsr.ReadBitsAsUInt(5);
							int subStrLen = (int)bsr.ReadBitsAsUInt(DemoSettings.SubStringBits);
							entryName = history[index].Substring(0, subStrLen);
							entryName += bsr.ReadNullTerminatedString();
						} else {
							entryName = bsr.ReadNullTerminatedString();
						}
					}

					BitStreamReader? entryStream = null;

					int streamLen = 0;
					if (bsr.ReadBool()) {
						if (tableToUpdate.UserDataFixedSize)
							streamLen = tableToUpdate.UserDataSizeBits;
						else
							streamLen = (int)bsr.ReadBitsAsUInt(DemoSettings.MaxUserDataBits) * 8;
						entryStream = bsr.SubStream(streamLen);
					}

					// Check if we are updating an old entry or adding a new one
					if (entryIndex < tableToUpdate.Entries.Count) {
						// if client-side then negative index, otherwise positive
						entryName = tableToUpdate.Entries[Math.Abs(entryIndex)].EntryName;
					} else { // Grow the table (entryIndex must be the next empty slot)
						entryName ??= ""; // avoid crash because of NULL strings
						int j = tableToUpdate.Entries.FindIndex(tableEntry => tableEntry.EntryName == entryName);
						if (j == -1) {
							TableUpdates.Add(new TableUpdate(
								manager.AddTableEntry(tableToUpdate, entryStream, entryName), 
								TableUpdateType.NewEntry,
								tableToUpdate.Entries.Count - 1)); // sub 1 since we update the table 2 lines up
						} else {
							TableUpdates.Add(new TableUpdate(
								manager.SetEntryData(tableToUpdate, tableToUpdate.Entries[j], entryStream!), 
								TableUpdateType.ChangeEntryData,
								j));
						}
						bsr.SkipBits(streamLen);
					}

					if (history.Count > 31)
						history.RemoveAt(0);
					history.Add(entryName);
				}
			} catch (Exception e) {
				// there was an update I couldn't parse, assume this C_table contain irrelevant data from here
				DemoRef.LogError($"error while parsing {GetType().Name} for table {_tableName}: {e.Message}");
				_exceptionWhileParsing = true;
				manager.TableReadable[_tableName] = false;
			}
		}
		

		internal override void WriteToStreamWriter(BitStreamWriter bsw) {
			throw new NotImplementedException();
		}


		public override void AppendToWriter(IndentedWriter iw) {
			if (_exceptionWhileParsing) {
				iw.Append("error while parsing");
				return;
			}
			if (TableUpdates.Count == 0) {
				iw.Append("no entries");
			} else {
				int padCount = TableUpdates
					.Select(update => (Name: update.TableEntry.EntryName, Data: update.TableEntry.EntryData))
					.Where(t => !(t.Data?.ContentsKnown ?? true))
					.Select(t => t.Name.Length + 2)
					.DefaultIfEmpty(2)
					.Max();
				
				for (var i = 0; i < TableUpdates.Count; i++) {
					if (i != 0)
						iw.AppendLine();
					TableUpdates[i].PadCount = padCount;
					TableUpdates[i].AppendToWriter(iw);
				}
			}
		}
	}


	public class TableUpdate : Appendable {

		internal int PadCount; // just for toString()
		public readonly CurStringTableEntry? TableEntry;
		public readonly int Index;
		public readonly TableUpdateType UpdateType;


		public TableUpdate(CurStringTableEntry? tableEntry, TableUpdateType updateType, int index) {
			TableEntry = tableEntry;
			UpdateType = updateType;
			Index = index;
		}
		
		
		public override void AppendToWriter(IndentedWriter iw) { // similar logic to that in string tables
			iw.Append($"[{Index}] {ParserTextUtils.CamelCaseToUnderscore(UpdateType.ToString())}: {TableEntry.EntryName}");
			if (TableEntry?.EntryData != null) {
				if (TableEntry.EntryData.InlineToString) {
					iw.Append("; ");
					TableEntry.EntryData.AppendToWriter(iw);
				} else {
					iw.FutureIndent++;
					if (TableEntry.EntryData.ContentsKnown)
						iw.AppendLine();
					else
						iw.PadLastLine(PadCount + 15, '.');
					TableEntry.EntryData.AppendToWriter(iw);
					iw.FutureIndent--;
				}
			}
		}
	}


	public enum TableUpdateType {
		NewEntry,
		ChangeEntryData
	}
}