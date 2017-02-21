using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

namespace SignApk {
	public class Manifest {
		private static readonly Regex keyRegex = new Regex(@"^Name:\s*(.*)$", RegexOptions.Compiled);
		private static readonly Regex valueRegex = new Regex(@"^SHA1-Digest:\s*(.*)$", RegexOptions.Compiled);

		private const int DefaultCapacity = 65537;

		private Dictionary<string, string> map;

		public Manifest() {
			map = new Dictionary<string, string>(DefaultCapacity);
		}

		public Manifest(Stream input) {
			map = new Dictionary<string, string>(DefaultCapacity);

			StreamReader reader = new StreamReader(input);
			string line;

			while ((line = reader.ReadLine()) != null) {
				if (string.IsNullOrWhiteSpace(line)) continue;

				Match match = keyRegex.Match(line);
				if (!match.Success) continue;
				string key = match.Groups[1].Value;

				line = reader.ReadLine();
				match = valueRegex.Match(line);
				if (!match.Success) continue;
				string value = match.Groups[1].Value;

				//if (!line.StartsWith("Name:")) continue;
				//string key = line.Substring(6);

				//line = reader.ReadLine();
				//if (!line.StartsWith("SHA1-Digest:")) continue;
				//string value = line.Substring(13);

				map.Add(key, value);
			}

		}

		public Manifest(IDictionary<string, string> map) {
			this.map = new Dictionary<string, string>(map);
		}

		public void WriteTo(Stream output) {
			StreamWriter writer = new StreamWriter(output);
			writer.WriteLine(Header);

			foreach (var kv in map) {
				writer.Write("Name: ");
				writer.WriteLine(kv.Key);
				writer.Write("SHA1-Digest: ");
				writer.WriteLine(kv.Value);
				writer.WriteLine();
			}

			writer.Flush();
		}

		public string Header { get; set; } =
@"Manifest-Version: 1.0
Created-By: 1.0 (Saar Tool)
";

		public IDictionary<string, string> Map => map;
	}
}
