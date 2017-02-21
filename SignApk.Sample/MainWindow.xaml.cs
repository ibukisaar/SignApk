using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Text.RegularExpressions;
using SignApk;
using System.Diagnostics;

namespace SignApk.Sample {
	/// <summary>
	/// MainWindow.xaml 的交互逻辑
	/// </summary>
	public partial class MainWindow : Window {
		public MainWindow() {
			InitializeComponent();
		}

		private void window_PreviewDragOver(object sender, DragEventArgs e) {
			if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
				e.Effects = DragDropEffects.Link;
			} else {
				e.Effects = DragDropEffects.None;
			}
			e.Handled = true;
		}

		private void window_PreviewDrop(object sender, DragEventArgs e) {
			try {
				var file = (e.Data.GetData(DataFormats.FileDrop) as string[])?[0];
				txtInput.Text = file;
				AutoFill();
			} catch { }
		}

		private static IEnumerable<string> GetLines(string text) {
			StringReader reader = new StringReader(text);
			string line;

			while ((line = reader.ReadLine()) != null) {
				yield return line.Trim().Replace('\\', '/');
			}
		}

		private void SetMessage(string message, Brush brush) {
			Dispatcher.Invoke(() => {
				lblState.Content = message;
				lblState.SetValue(ForegroundProperty, brush);
			});
		}

		private void btn提取_Click(object sender, RoutedEventArgs e) {
			SetMessage("提取中...", Brushes.Blue);
			string apk = txtInput.Text;
			string outputDir = txtOutput1.Text;
			var files = GetLines(txtFiles.Text);

			if (string.IsNullOrWhiteSpace(apk) || string.IsNullOrWhiteSpace(outputDir)) {
				SetMessage("请输入 “目标apk”,“输出目录” 。", Brushes.Red);
				return;
			}

			SaveFilesConfig();

			SetButtonEnabled(false);
			Task.Run(() => {
				try {
					Stopwatch sw = new Stopwatch();
					sw.Start();
					int unzipFileCount = ApkTool.Unzip(apk, files, outputDir);
					sw.Stop();
					SetMessage($@"提取了{unzipFileCount}个文件，耗时: {sw.Elapsed:hh\:mm\:ss\.fff}", Brushes.Green);
				} catch (Exception ex) {
					SetMessage("提取失败：" + ex.Message, Brushes.Red);
				} finally {
					SetButtonEnabled(true);
				}
			});
		}

		private void btnInput_Click(object sender, RoutedEventArgs e) {
			using (var dialog = new System.Windows.Forms.OpenFileDialog()) {
				dialog.Filter = "apk文件|*.apk;*.zip|所有文件|*.*";
				dialog.Title = "选择一个文件";
				dialog.Multiselect = false;
				dialog.RestoreDirectory = false;

				if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
					txtInput.Text = dialog.FileName;
					AutoFill();
				}
			}
		}

		private void GetDirectoryAndApkNameFromInput(out string directory, out string name, out string extension) {
			FileInfo info = new FileInfo(txtInput.Text);
			directory = info.DirectoryName;
			if (!string.IsNullOrEmpty(info.Extension)) {
				name = info.Name.Substring(0, info.Name.Length - info.Extension.Length);
				extension = info.Extension;
			} else {
				name = info.Name;
				extension = null;
			}
		}

		private void LoadFilesConfig() {
			GetDirectoryAndApkNameFromInput(out string directory, out string name, out string extension);

			string config = directory + "\\" + name + "-config.txt";
			if (File.Exists(config)) {
				txtFiles.Text = File.ReadAllText(config);
			}
		}

		private void SaveFilesConfig() {
			if (File.Exists(txtInput.Text)) {
				GetDirectoryAndApkNameFromInput(out string directory, out string name, out string extension);

				string config = directory + "\\" + name + "-config.txt";
				File.WriteAllText(config, txtFiles.Text);
			}
		}

		private void AutoFill() {
			GetDirectoryAndApkNameFromInput(out string directory, out string name, out string extension);

			string outputDirectory, outputFile;
			if (extension != null) {
				outputDirectory = directory + '\\' + name;
				outputFile = directory + '\\' + name + "-sign" + extension;
			} else {
				outputDirectory = directory + '\\' + name + "-outdir";
				outputFile = directory + '\\' + name + "-sign";
			}

			txtOutput1.Text = outputDirectory;
			txtOutput2.Text = outputFile;
			LoadFilesConfig();
		}

		private void btnOutput1_Click(object sender, RoutedEventArgs e) {
			using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
				dialog.Description = "选择一个文件夹";
				if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
					txtOutput1.Text = dialog.SelectedPath;
				}
			}
		}

		private void btnOutput2_Click(object sender, RoutedEventArgs e) {
			using (var dialog = new System.Windows.Forms.SaveFileDialog()) {
				dialog.Filter = "apk文件|*.apk|zip文件|*.zip";
				dialog.Title = "apk输出位置";
				dialog.RestoreDirectory = false;
				if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
					txtOutput2.Text = dialog.FileName;
				}
			}
		}

		private void btn打包_Click(object sender, RoutedEventArgs e) {
			SetMessage("打包并签名中...", Brushes.Blue);
			string inputApk = txtInput.Text;
			string inputDir = txtOutput1.Text;
			string outputApk = txtOutput2.Text;
			string filesText = txtFiles.Text;

			if (string.IsNullOrWhiteSpace(inputApk) || string.IsNullOrWhiteSpace(inputDir) || string.IsNullOrWhiteSpace(outputApk)) {
				SetMessage("请输入 “目标apk”,“输出目录”,“输出apk” 。", Brushes.Red);
				return;
			}

			SaveFilesConfig();

			//{
			//	ApkTool.ZipAndSign(inputApk, inputDir, GetLines(filesText), outputApk, "key.p12");
			//	SetMessage($@"打包并签名成功", Brushes.Green);
			//	return;
			//}

			SetButtonEnabled(false);
			Task.Run(() => {
				try {
					Stopwatch sw = new Stopwatch();

					sw.Start();
					int zipFileCount = ApkTool.ZipAndSign(inputApk, inputDir, GetLines(filesText), outputApk, "key.p12");
					sw.Stop();

					SetMessage($@"打包并签名了{zipFileCount}个文件，耗时: {sw.Elapsed:hh\:mm\:ss\.fff}", Brushes.Green);
				} catch (NoKeyException) {
					SetMessage("签名失败：未找到key.p12文件。", Brushes.Red);
				} catch (Exception ex) {
					SetMessage("打包失败：" + ex.Message.Trim(), Brushes.Red);
				} finally {
					SetButtonEnabled(true);
				}
			});
		}

		private void SetButtonEnabled(bool enabled) {
			Dispatcher.Invoke(() => {
				btn提取.IsEnabled = enabled;
				btn打包.IsEnabled = enabled;
			});
		}
	}
}
