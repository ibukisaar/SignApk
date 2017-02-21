using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SignApk {
	public struct StaticDataSource : IStaticDataSource {
		private Stream stream;

		public StaticDataSource(Stream stream) {
			this.stream = stream;
		}

		public StaticDataSource(byte[] data) {
			stream = new MemoryStream(data);
		}

		public Stream GetSource() {
			return stream;
		}
	}
}
