using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SignApk {
	public static class Common {
		public static string StandardDirectory(string dir) {
			if (dir.EndsWith("\\") || dir.EndsWith("/")) {
				return dir.Substring(0, dir.Length - 1);
			}
			return dir;
		}

		public static bool TryCreateDirectory(string file) {
			string dir = Path.GetDirectoryName(file);
			if (!Directory.Exists(dir)) {
				Directory.CreateDirectory(dir);
				return true;
			}
			return false;
		}
	}
}
