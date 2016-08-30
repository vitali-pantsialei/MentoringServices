using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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
        private const string queueName = "pdfqueue";
        private const string statusQueue = "status";
        private const string changeSettingsQueue = "settings";
        private string inputDir;
        private string wrongFilesDir;
        private Thread workThread, changeThread;
        private const string fileRegex = @"([a-zA-Z]+)_([0-9]+)\.(img|png|jpeg|jpg)";
        private FileSystemWatcher watcher;
        private AutoResetEvent newFileEvent;
        private ManualResetEvent stopWorkEvent;
        private int fileCheckCount = 3;
        private object checkCountLock = new object();

        public MergingService(string inputDir, string wrongFilesDir)
        {
            this.inputDir = inputDir;
            this.wrongFilesDir = wrongFilesDir;

            if (!Directory.Exists(inputDir))
                Directory.CreateDirectory(inputDir);

            if (!Directory.Exists(wrongFilesDir))
                Directory.CreateDirectory(wrongFilesDir);

            watcher = new FileSystemWatcher(inputDir);
            watcher.Created += watcher_Created;
            workThread = new Thread(Scan);
            changeThread = new Thread(Settings);

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
            changeThread.Start();
            watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            watcher.EnableRaisingEvents = false;
            stopWorkEvent.Set();
            workThread.Join();
            changeThread.Join();
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
                        Send(statusQueue, System.Text.Encoding.UTF8.GetBytes(DateTime.Now + ": process files!"));
                        string lastFileName;
                        var reader = new BarcodeReader() { AutoRotate = true };
                        var document = new Document();
                        var section = document.AddSection();
                        int fileNumber = Int32.Parse(m.Groups[2].Value);

                        while (TryOpen(Path.Combine(inputDir, fileName), fileCheckCount))
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

                        byte[] fileContents = null;
                        var render = new PdfDocumentRenderer();
                        render.Document = document;
                        render.RenderDocument();
                        using (MemoryStream stream = new MemoryStream())
                        {
                            render.Save(stream, true);
                            fileContents = stream.ToArray();
                        }

                        if (fileContents != null)
                        {
                            Send(queueName, fileContents);
                            Console.WriteLine("File has been sent");
                        }

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
                        Send(statusQueue, System.Text.Encoding.UTF8.GetBytes(DateTime.Now + ": wrong file format!"));
                        try
                        {
                            File.Move(file, Path.Combine(wrongFilesDir, fileName));
                        }
                        catch
                        {
                            File.Delete(Path.Combine(inputDir, fileName));
                        }
                    }
                }
                Send(statusQueue, System.Text.Encoding.UTF8.GetBytes(DateTime.Now + ": Free!"));
            }
            while (WaitHandle.WaitAny(new WaitHandle[] { stopWorkEvent, newFileEvent }, 1000) != 0);
        }

        private void Settings(object obj)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: changeSettingsQueue,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null);

                    channel.BasicQos(0, 1, false);

                    var consumer = new EventingBasicConsumer(channel);

                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body;
                        lock (checkCountLock)
                        {
                            fileCheckCount = Int32.Parse(System.Text.Encoding.UTF8.GetString(body));
                        }
                        Console.WriteLine("Checking count updated!");
                        channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    };
                    do
                    {
                        channel.BasicConsume(queue: changeSettingsQueue,
                                             noAck: false,
                                             consumer: consumer);
                    }
                    while (!stopWorkEvent.WaitOne(1000));
                }
            }
        }

        private void Send(string address, byte[] content)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: address,
                                         durable: true,
                                         exclusive: false,
                                         autoDelete: false,
                                         arguments: null);

                    var properties = channel.CreateBasicProperties();
                    properties.Persistent = true;

                    channel.BasicPublish(exchange: "",
                                         routingKey: address,
                                         basicProperties: properties,
                                         body: content);
                }
            }
        }

        private bool TryOpen(string fileName, int tryCount)
        {
            lock (checkCountLock)
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
            }

            return false;
        }
    }
}
