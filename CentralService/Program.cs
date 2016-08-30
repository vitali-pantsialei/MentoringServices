using NLog;
using NLog.Config;
using NLog.Targets;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Topshelf;

namespace CentralService
{
    class Program
    {
        static void Main(string[] args)
        {
            var currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var outDir = Path.Combine(currentDir, "out");
            var statDir = Path.Combine(currentDir, "status");
            var confDir = Path.Combine(currentDir, "config");

            var conf = new LoggingConfiguration();
            var fileTarget = new FileTarget()
            {
                Name = "Default",
                FileName = Path.Combine(currentDir, "log.txt"),
                Layout = "${date} ${message} ${onexception:inner=${exception:format=toString}}"
            };
            conf.AddTarget(fileTarget);
            conf.AddRuleForAllLevels(fileTarget);

            var logFactory = new LogFactory(conf);

            HostFactory.Run(
                hostConf => hostConf.Service<CentralSaveService>(
                    s =>
                    {
                        s.ConstructUsing(() => new CentralSaveService(outDir, statDir, confDir));
                        s.WhenStarted(serv => serv.Start());
                        s.WhenStopped(serv => serv.Stop());
                    }
                    ).UseNLog(logFactory));
        }
    }
}
