using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DemoParser.Parser;
using DemoParser.Parser.HelperClasses;
using DemoParser.Utils;
using static DemoParser.Utils.ParserTextUtils;

namespace ConsoleApp {
	
	public static partial class ConsoleFunctions {
		
		private static string UsageStr => $"Usage: \"{AppDomain.CurrentDomain.FriendlyName}\" <demos/dirs> [options]";
		private static readonly List<Option> OptionsRequiringFolder = new List<Option>();
		private static bool _recursive;
		
		// todo add prop tracker
		public static void Main(string[] args) {
			
			if (!_recursive)
				Console.WriteLine("UntitledParser by UncraftedName");

			switch (args.Length) {
				case 0:
					PrintVersionInfo();
					PrintShortUsageString();
					Console.Read();
					Environment.Exit(0);
					break;
				case 1 when File.Exists(args[0]): // behave just like listdemo+
					Demos.Add(new SourceDemo(args[0]));
					try {
						CurDemo.Parse();
						ConsFunc_ListDemo(default, false, false, true);
					} catch (Exception e) {
						ConsFunc_ListDemo(default, false, true, true);
						ConsoleWriteWithColor(e.ToString(), ConsoleColor.Red);
						Console.Read();
						Environment.Exit(1);
					}
					Console.Read();
					Environment.Exit(0);
					break;
				default:
					if (!_recursive && args.All(s => File.Exists(s) || Directory.Exists(s))) {
						_recursive = true;
						Main(args.Concat(new [] {"--listdemo", ListdemoOption.DisplayHeader.ToString()}).ToArray());
						Console.Read();
						Environment.Exit(0);
					}
					break;
			}

			// directory info should work here, will probably change to something better in the future™
			var pathsArg = new Argument<DirOrPath[]>("paths") {
				Description = "demo paths or folders",
				Arity = ArgumentArity.ZeroOrMore // I check for none later because the default help message is unhelpful
			};
			pathsArg.AddValidator(symbol => {
				string s = symbol.Tokens[0].Value;
				Console.WriteLine(Path.GetFullPath(s));
				bool d = Directory.Exists(s);
				bool f = File.Exists(s);
				if (!d && !f)
					return $"path \"{s}\" does not exist";
				if (f && !s.EndsWith(".dem"))
					return $"\"{s}\" is not a demo file";
				return null;
			});
			
			
			var regexOpt = new Option(new[] {"-R", "--regex"},
				"search for console commands by regex (.* will display everything)") {
				Required = false,
				Argument = new Argument<string>("regex") {
					Arity = ArgumentArity.ExactlyOne,
					Description = "regex used to find console commands",
				}
			};
			regexOpt.AddValidator(symbol => 
				symbol.Tokens.Count == 0 
					? "try '.*' to print all console commands" 
					: CheckIfValidRegex(symbol.Tokens[0].Value));
			
			
			var verboseOpt = new Option(new [] {"-v", "--verbose"},
				"dump all parsable info in the demo to a text file (needs -f)") {
				Required = false
			};
			OptionsRequiringFolder.Add(verboseOpt);
			
			
			var folderOpt = new Option(new [] {"-f", "--folder"},
				"specify a folder to dump the parser output to") {
				Required = false,
				Argument = new Argument<DirectoryInfo>("folder") {
					Arity = ArgumentArity.ExactlyOne,
					Description = "the folder to dump the parser output to"
				}
			};
			folderOpt.AddValidator(symbol => 
				symbol.Tokens.Count == 0 
					? "requires a folder name"
					: CheckIfValidFolderName(symbol.Tokens[0].Value));
			

			// probs should figure out how to use separate commands, since i want to determine when the option is triggered
			// and what the argument is separately
			var listDemoOpt = new Option(new [] {"-l", "--listdemo"},
				"print demo info in a format similar to listdemo+") {
				Required = false,
				Argument = new Argument<ListdemoOption>("listdemoOption", default) {
					Arity = ArgumentArity.ZeroOrOne,
					Description = "determines whether or not to display demo header info"
				}
			};
			

			var cheatOpt = new Option(new [] {"-c", "--cheats"},
				"shows any cheats detected in the demo") {
				Required = false
			};
			

			var jumpOpt = new Option(new [] {"-j", "--jumps"},
				"shows any detection of jumps") {
				Required = false
			};
			
			var recursiveOpt = new Option(new [] {"-r", "--recursive"},
				"specifies whether the search for demos should be recursive") {
				Required = false
			};
			
			
			var linkOpt = new Option(new [] {"--link"},
				"attempt to link demos together to check if they are part of a continuous run (not implemented)") {
					Required = false
			};
			linkOpt.AddValidator(_ => "link option not implemented yet");
			OptionsRequiringFolder.Add(linkOpt);
			
			
			var removeCaptionOpt = new Option(new [] {"-C", "--remove-captions"}, 
				"creates a copy of the original demo(s) without captions (needs -f)") {
				Required = false
			};
			OptionsRequiringFolder.Add(removeCaptionOpt);
			
			
			var dTableDumpOpt = new Option(new [] {"-d", "--dump-datatables"},
				"dumps the data tables packet and creates a tree of the server class hierarchy (needs -f)") {
				Required = false,
				Argument = new Argument<DataTablesDispType>("datatableDisplayType", default) {
					Arity = ArgumentArity.ZeroOrOne,
					Description = "determines how to display the datatables"
				}
			};
			OptionsRequiringFolder.Add(dTableDumpOpt);
			
			
			var actionsPressedOpt = new Option(new [] {"-a", "--actions-pressed"},
				"displays when game actions (attack, use, etc.) were pressed during the demo") {
				Required = false,
				Argument = new Argument<ActPressedDispType>("actionsPressedOption", default) {
					Arity = ArgumentArity.ZeroOrOne,
					Description = "determines how to display the actions pressed"
				}
			};
			
			
			var passThroughPortalsOpt = new Option(new [] {"-p", "--portals-passed"},
				"shows any time an entity passes through a portal") {
				Required = false,
				Argument = new Argument<PtpFilterType>("filterType", default) {
					Arity = ArgumentArity.ZeroOrOne,
					Description = "determines what information to display about the entities"
				}
			};
			
			
			/*var errorsOpt = new Option(new [] {"-e", "--errors"}, 
				"display all errors that occured during parsing") {
				Required = false
			};*/
			
			var patchJumpOpt = new Option(new [] {"--patch-jumps"}, 
				"smooth out (most) jumps in a demo") {
				Required = false
			};
			OptionsRequiringFolder.Add(patchJumpOpt);
			
			
			var changeDirOpt = new Option(new [] {"-D", "--change-demo-dir"},
				"changes the directory of the given demos") {
				Required = false,
				Argument = new Argument<string>("dir") {
					Arity = ArgumentArity.ExactlyOne,
					Description = "directory to change to",
				}
			};
			OptionsRequiringFolder.Add(changeDirOpt);
			
			
			var pausesOpt = new Option(new [] {"-u", "--pauses"},
				"detects pausing and unpausing") {
				Required = false
			};


			var rootCommand = new RootCommand {
				pathsArg,
				regexOpt,
				verboseOpt,
				folderOpt,
				listDemoOpt,
				cheatOpt,
				jumpOpt,
				recursiveOpt,
				removeCaptionOpt,
				dTableDumpOpt,
				actionsPressedOpt,
				passThroughPortalsOpt,
				patchJumpOpt,
				changeDirOpt,
				pausesOpt
			};
			
			if (Debugger.IsAttached) // link option not implemented yet
				rootCommand.AddOption(linkOpt);
			
			
			rootCommand.AddValidator(res => {
				var runnable = res.Children.Where(
					result => result.GetType() != typeof(ArgumentResult)          // don't care about arguments
							  && result.Name != folderOpt.Name                    // don't care about --folder option
							  && result.Name != recursiveOpt.Name                 // don't care about --recursive option
							  && !((result as OptionResult)?.IsImplicit ?? true)) // don't care about implicit options (anything with an arg)
					.ToList();
				// if there are no options left after those ^ conditions, then none have been provided
				if ((_runnableOptionCount = runnable.Count) == 0)
					return "no runnable options given";

				// if no -f but something like -v explicitly set, cry
				if (res[folderOpt.Aliases[0]] == null) {
					return OptionsRequiringFolder
						.Where(option => res[option.Name] != null && runnable.Any(result => result.Name == option.Name))
						.Select(option => $"{option.Name} option requires -f").FirstOrDefault();
				}
				return null;
			});


			// make sure the order here is the same as below (the names of the params must match the option names)
			void CommandAction(
				DirOrPath[] paths,
				bool verbose,
				bool cheats,
				bool recursive,
				bool jumps,
				bool removeCaptions,
				bool patchJumps,
				bool pauses,
				string changeDemoDir,
				DataTablesDispType dumpDatatables,
				string regex,
				DirectoryInfo folder,
				ListdemoOption listdemo,
				ActPressedDispType actionsPressed,
				PtpFilterType portalsPassed,
				ParseResult parseResult) // this is part of System.CommandLine - it will automatically put the result in here
			{
				if (paths == null) // provides more detailed message 
					PrintErrorAndExit("you must provide at least one demo or folder with demos");
				
				// add paths to set to make sure i don't parse the same demo several times
				HashSet<FileInfo> demoPaths = new HashSet<FileInfo>();
				foreach (DirOrPath dirOrPath in paths!) {
					if (dirOrPath.IsDir) {
						demoPaths.UnionWith(dirOrPath.DirectoryInfo!.EnumerateFiles("*.dem", 
							recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
					} else {
						if (!dirOrPath.Name.EndsWith(".dem"))
							PrintErrorAndExit($"{dirOrPath} is not a demo file");
						else
							demoPaths.Add(dirOrPath.FileInfo!);
					}
				}
				
				if (!demoPaths.Any())
					PrintErrorAndExit("No demos to parse...");
				
				/*if (link && demoPaths.Count < 2)
					PrintErrorAndExit("The link option should have at least 2 demos");*/
				
				if (folder != null) {
					folder.Create();
					_folderPath = folder.FullName;
				}

				string commonParent;
				if (demoPaths.Count == 1) {
					commonParent = demoPaths.Single().DirectoryName;
				} else {
					var ordered = demoPaths.OrderBy(f => f.Name, new AlphanumComparator()).ToList();
					// any similarities between the first and last path must be shared between all paths
					commonParent = SharedPathSubstring(ordered.First().FullName, ordered.Last().FullName);
				}

				// if the common path is empty, that prolly means the demos span multiple drives, so use the full name
				var orderedPaths = demoPaths.Select(
					f => (info: f, displayPath: commonParent == "" 
						? f.FullName
						: PathExt.GetRelativePath(commonParent!, f.FullName)))
					.OrderBy(t => t.displayPath, new AlphanumComparator())
					.ToList();
				
				// the check for options with arguments is pretty dumb, not sure of a better way tho
				var implicitOptions = new {
					listDemo = parseResult.Tokens.Any(token => listDemoOpt.HasAlias(token.Value)),
					actionsPressed = parseResult.Tokens.Any(token => actionsPressedOpt.HasAlias(token.Value)),
					portalsPassed = parseResult.Tokens.Any(token => passThroughPortalsOpt.HasAlias(token.Value)),
					dumpDatatables = parseResult.Tokens.Any(token => dTableDumpOpt.HasAlias(token.Value))
				};
				
				bool quickHashesMatch = (implicitOptions.listDemo /*|| link*/) && orderedPaths.Count > 1;

				int prevDemoQuickHash = 0; // if demo dir or version is not the same as prev demo, don't count total time
				
				// main loop
				for (var i = 0; i < orderedPaths.Count; i++) {
					bool exceptionDuringParsing = false;
					try {
						Console.Write($"Parsing \"{orderedPaths[i].displayPath}\"... ");

						using (var progressBar = new ProgressBar()) {
							// if i'm not linking demos there's no point in keeping all of them
							/*if (!link)
								Demos.Clear();*/
							Demos.Add(new SourceDemo(orderedPaths[i].info.FullName, progressBar));
							CurDemo.Parse();
						}

						ConsoleWriteWithColor("done.\n", ConsoleColor.Green);

						// run all the standard options

						if (implicitOptions.actionsPressed)
							ConsFunc_DumpActions(actionsPressed);
						if (implicitOptions.portalsPassed)
							ConsFunc_PortalsPassed(portalsPassed);
						if (implicitOptions.dumpDatatables)
							ConsFunc_DumpDataTables(dumpDatatables);
						if (regex != null)
							ConsFunc_RegexSearch(regex);
						if (cheats)
							ConsFunc_DumpCheats();
						if (jumps)
							ConsFunc_DumpJumps();
						if (removeCaptions)
							ConsFunc_RemoveCaptions();
						/*if (errors)
							ConsFunc_Errors();*/
						if (patchJumps)
							ConsFunc_PatchJumps();
						if (changeDemoDir != null)
							ConsFunc_ChangeDemoDir(changeDemoDir);
						if (pauses)
							ConsFunc_Pauses();
					}
					catch (Exception e) {
						Debug.WriteLine(e.ToString());
						ConsoleWriteWithColor($"failed.\nMessage: {e.Message}\n", ConsoleColor.Red);
						if (quickHashesMatch)
							DemoTickCounts.Add((int.MinValue, int.MinValue));
						exceptionDuringParsing = true;
					}
					// other options might still recover from a parsing exception so put these in a separate try-catch

					try {
						if (implicitOptions.listDemo) {
							bool excludedMap =
								TimingAdjustment.ExcludedMaps.Contains((CurDemo.DemoInfo.Game,
									CurDemo.Header.MapName));
							_displayMapExcludedMsg = quickHashesMatch && excludedMap;
							ConsFunc_ListDemo(listdemo, true, exceptionDuringParsing, false);
							if (quickHashesMatch && !excludedMap)
								DemoTickCounts.Add((CurDemo.TickCount(), CurDemo.AdjustedTickCount()));
						}

						if (verbose && Demos.Count > 0)
							ConsFunc_VerboseDump();
						
					} catch (Exception e) {
						ConsoleWriteWithColor($"Tried to continue, but something went terrible wrong:\n{e}\n", ConsoleColor.Red);
					}

					if (i == 0)
						prevDemoQuickHash = CurDemoHeaderQuickHash();
					else if (prevDemoQuickHash != (prevDemoQuickHash = CurDemoHeaderQuickHash()))
						quickHashesMatch = false;
				}

				/*if (link) {
					if (quickHashesMatch)
						ConsFunc_LinkDemos();
					else
						ConsoleWriteWithColor("Cannot link demos - The game directory and/or demo version doesn't match!\n", ConsoleColor.Red);
				}*/

				if (quickHashesMatch && implicitOptions.listDemo)
					ConsFunc_DisplayTotalTime();
				
				DisposeWriters();
			}


			// this is getting a tiny bit out of hand
			rootCommand.Handler = CommandHandler.Create((Action<
				DirOrPath[],bool,bool,bool,bool,bool,bool,bool,string,DataTablesDispType,
				string,DirectoryInfo,ListdemoOption,ActPressedDispType,
				PtpFilterType,ParseResult>)CommandAction);

			try {
				rootCommand.Invoke(args);
			} finally {
				_folderPath = null;
				DisposeWriters();
			}
		}


		// set to null after so that I can't flush it again, cuz that would cause a crash
		private static void DisposeWriters() {
			_curTextWriter?.Flush();
			_curTextWriter?.Dispose();
			_curTextWriter = null;
			_curBinWriter?.Flush();
			_curBinWriter?.Dispose();
			_curBinWriter = null;
		}
		
		
		private static void PrintShortUsageString() {
			Console.WriteLine($"{UsageStr}");
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Use -h, --help, or /? to get full help\n");
			Console.ResetColor();
		}
		
		
		private static string? CheckIfValidRegex(string pattern) {
			try {
				new Regex(pattern);
			} catch (Exception e) {
				return e.Message;
			}
			return null;
		}

		private static readonly HashSet<char> InvalidChars = new HashSet<char>(Path.GetInvalidPathChars().AsEnumerable());
		private static string? CheckIfValidFolderName(string name) {
			try {
				if (name.Length > 240)
					return "path name too long";
				if (InvalidChars.Intersect(name.ToCharArray().AsEnumerable()).Any())
					return "path name contains invalid characters";
			} catch (Exception e) {
				return e.Message;
			}
			return null;
		}


		private static void PrintErrorAndExit(string s) {
			ConsoleColor c = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(s);
			Console.ForegroundColor = c;
			Console.Out.Flush();
			Environment.Exit(1);
		}


		private static void PrintVersionInfo() {
			DateTime dt = BuildDateAttribute.GetBuildDate(Assembly.GetExecutingAssembly());
			Console.WriteLine($"Build time: {dt.ToString("R", CultureInfo.CurrentCulture)}");
		}
	}

	// keep these in order unless you want to change the default values
	
	public enum ListdemoOption {
		DisplayHeader,
		SuppressHeader
	}


	public enum ActPressedDispType {
		AsTextFlags,
		AsInt,
		AsBinaryFlags
	}


	public enum PtpFilterType { // pass through portals
		PlayerOnly,
		PlayerOnlyVerbose,
		AllEntities,
		AllEntitiesVerbose,
	}


	public enum DataTablesDispType {
		Default,
		Flattened
	}
}