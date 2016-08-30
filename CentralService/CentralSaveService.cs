using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CentralService
{
    class CentralSaveService
    {
        private const string queueName = "pdfqueue";
        private const string statusQueue = "status";
        private const string changeSettingsQueue = "settings";
        private string outputDir, statusDir, configDir;
        private Thread workThread, statusThread;
        private int outputNumber = 0;
        private ManualResetEvent stopWorkEvent;
        private FileSystemWatcher watcher;

        public CentralSaveService(string outputDir, string statusDir, string configDir)
        {
            this.outputDir = outputDir;
            this.statusDir = statusDir;
            this.configDir = configDir;

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            if (!Directory.Exists(statusDir))
                Directory.CreateDirectory(statusDir);
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            watcher = new FileSystemWatcher(configDir);
            watcher.Changed += watcher_Changed;

            workThread = new Thread(Scan);
            statusThread = new Thread(Status);

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

            stopWorkEvent = new ManualResetEvent(false);
        }

        void watcher_Changed(object sender, FileSystemEventArgs e)
        {
            using (StreamReader sr = new StreamReader(e.FullPath))
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

                        var properties = channel.CreateBasicProperties();
                        properties.Persistent = true;

                        channel.BasicPublish(exchange: "",
                                             routingKey: changeSettingsQueue,
                                             basicProperties: properties,
                                             body: System.Text.Encoding.UTF8.GetBytes(sr.ReadToEnd()));
                    }
                }
            }
        }

        public void Start()
        {
            workThread.Start();
            statusThread.Start();
            watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            watcher.EnableRaisingEvents = false;
            stopWorkEvent.Set();
            workThread.Join();
            statusThread.Join();
        }

        private void Scan(object obj)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null);

                    channel.BasicQos(0, 1, false);

                    var consumer = new EventingBasicConsumer(channel);

                    consumer.Received += (model, ea) =>
                    {
                        Console.WriteLine("New message was found");
                        var body = ea.Body;
                        File.WriteAllBytes(Path.Combine(outputDir, "output" + (outputNumber++) + ".pdf"), body);
                        Console.WriteLine("PDF file saved");
                        channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    };
                    do
                    {
                        channel.BasicConsume(queue: queueName,
                                             noAck: false,
                                             consumer: consumer);
                    }
                    while (!stopWorkEvent.WaitOne(1000));
                }
            }
        }

        private void Status(object obj)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: statusQueue,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null);

                    channel.BasicQos(0, 1, false);

                    var consumer = new EventingBasicConsumer(channel);

                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body;
                        File.AppendAllText(Path.Combine(statusDir, "status.log"), System.Text.Encoding.UTF8.GetString(body) + Environment.NewLine);
                        channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    };
                    do
                    {
                        channel.BasicConsume(queue: statusQueue,
                                             noAck: false,
                                             consumer: consumer);
                    }
                    while (!stopWorkEvent.WaitOne(1000));
                }
            }
        }
    }
}
