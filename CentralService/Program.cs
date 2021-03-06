﻿using Ninject;
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
            CentralServiceModule csm = new CentralServiceModule();

            using (IKernel kernel = new StandardKernel(csm))
            {
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
                    hostConf => hostConf.Service<ICentralService>(
                        s =>
                        {
                            s.ConstructUsing(() => kernel.Get<ICentralService>());
                            s.WhenStarted(serv => serv.Start());
                            s.WhenStopped(serv => serv.Stop());
                        }
                        ).UseNLog(logFactory));
            }
        }
    }
}
