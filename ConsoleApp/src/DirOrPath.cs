#nullable enable
using System.IO;

#pragma warning disable 8602

namespace ConsoleApp {
	
	public class DirOrPath : FileSystemInfo {

		public readonly DirectoryInfo? DirectoryInfo;
		public readonly FileInfo? FileInfo;
		public bool IsDir => (Attributes & FileAttributes.Directory) != 0;
		public override bool Exists => IsDir ? DirectoryInfo.Exists : FileInfo.Exists;
		public override string Name => IsDir ? DirectoryInfo.Name : FileInfo.Name;
		

		public DirOrPath(string path) {
			OriginalPath = path;
			FullPath = Path.GetFullPath(path);
			Attributes = File.GetAttributes(path);
			if (IsDir)
				DirectoryInfo = new DirectoryInfo(path);
			else
				FileInfo = new FileInfo(path);
		}
		
		
		public override void Delete() {
			if (IsDir)
				DirectoryInfo.Delete();
			else
				FileInfo.Delete();
		}
		
		
		public override string ToString() {
			return IsDir ? DirectoryInfo.ToString() : FileInfo.ToString();
		}
	}
}