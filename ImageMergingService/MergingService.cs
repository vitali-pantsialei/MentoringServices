using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZXing;

namespace ImageMergingService
{
    class MergingService
    {
        private int outputNumber = 0;
        private string inputDir;
        private string outputDir;
        private string wrongFilesDir;
        private Thread workThread;
        private const string fileRegex = @"([a-zA-Z]+)_([0-9]+)\.(img|png|jpeg|jpg)";
        private FileSystemWatcher watcher;
        private AutoResetEvent newFileEvent;
        private ManualResetEvent stopWorkEvent;

        public MergingService(string inputDir, string outputDir, string wrongFilesDir)
        {
            this.inputDir = inputDir;
            this.outputDir = outputDir;
            this.wrongFilesDir = wrongFilesDir;

            if (!Directory.Exists(inputDir))
                Directory.CreateDirectory(inputDir);

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            if (!Directory.Exists(wrongFilesDir))
                Directory.CreateDirectory(wrongFilesDir);

            watcher = new FileSystemWatcher(inputDir);
            watcher.Created += watcher_Created;
            workThread = new Thread(Scan);

            foreach (var file in Directory.EnumerateFiles(outputDir))
            {
                Match m = Regex.Match(Path.GetFileName(file), @"output([0-9]+)\.pdf");
                if (m.Success)
                {
                    if (outputNumber < Int32.Parse(m.Groups[1].Value))
                    {
                        outputNumber = Int32.Parse(m.Groups[1].Value);
                    }
                }
            }
            ++outputNumber;

            newFileEvent = new AutoResetEvent(false);
            stopWorkEvent = new ManualResetEvent(false);
        }

        void watcher_Created(object sender, FileSystemEventArgs e)
        {
            newFileEvent.Set();
        }

        public void Start()
        {
            workThread.Start();
            watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            watcher.EnableRaisingEvents = false;
            stopWorkEvent.Set();
            workThread.Join();
        }

        private void Scan(object obj)
        {
            do
            {
                foreach (var file in Directory.EnumerateFiles(inputDir))
                {
                    if (stopWorkEvent.WaitOne(TimeSpan.Zero))
                        return;

                    string fileName = Path.GetFileName(file);
                    Match m = Regex.Match(fileName, fileRegex);
                    if (m.Success)
                    {
                        string lastFileName;
                        var reader = new BarcodeReader() { AutoRotate = true };
                        var document = new Document();
                        var section = document.AddSection();
                        int fileNumber = Int32.Parse(m.Groups[2].Value);

                        while (TryOpen(Path.Combine(inputDir, fileName), 3))
                        {
                            // For barcodes
                            var bmp = (Bitmap)Bitmap.FromFile(file);
                            var result = reader.Decode(bmp);
                            bmp.Dispose();

                            // End of document
                            if (result != null)
                            {
                                break;
                            }

                            var img = section.AddImage(Path.Combine(inputDir, fileName));
                            img.Height = document.DefaultPageSetup.PageHeight;
                            img.Width = document.DefaultPageSetup.PageWidth;

                            // Find the same file but with increased number
                            fileName = m.Groups[1].Value + "_" + (++fileNumber) + "." + m.Groups[3].Value;
                        }
                        // Remember last file
                        lastFileName = fileName;

                        var render = new PdfDocumentRenderer();
                        render.Document = document;

                        render.RenderDocument();
                        render.Save(Path.Combine(outputDir, "output" + (outputNumber++) + ".pdf"));

                        // Remove all used images
                        fileNumber = Int32.Parse(m.Groups[2].Value);
                        fileName = m.Groups[1].Value + "_" + (fileNumber) + "." + m.Groups[3].Value;
                        while (TryOpen(Path.Combine(inputDir, fileName), 3))
                        {
                            File.Delete(Path.Combine(inputDir, fileName));

                            if (fileName == lastFileName)
                                break;

                            fileName = m.Groups[1].Value + "_" + (++fileNumber) + "." + m.Groups[3].Value;
                        }

                        break;
                    }
                    // Move wrong file
                    else
                    {
                        File.Move(file, Path.Combine(wrongFilesDir, fileName));
                    }
                }
            }
            while (WaitHandle.WaitAny(new WaitHandle[] { stopWorkEvent, newFileEvent }, 1000) != 0);
        }

        private bool TryOpen(string fileName, int tryCount)
        {
            for (int i = 0; i < tryCount; i++)
            {
                try
                {
                    var file = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.None);
                    file.Close();

                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(5000);
                }
            }

            return false;
        }
    }
}
