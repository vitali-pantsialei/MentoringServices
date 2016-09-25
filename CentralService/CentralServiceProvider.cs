using AopDProxy;
using Castle.DynamicProxy;
using Ninject.Activation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentralService
{
    public class CentralServiceProvider : Provider<ICentralService>
    {
        private string outputDir;
        private string statusDir;
        private string configDir;

        public CentralServiceProvider()
        {
            var currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            this.configDir = Path.Combine(currentDir, "config");
            this.outputDir = Path.Combine(currentDir, "out");
            this.statusDir = Path.Combine(currentDir, "status");
        }

        protected override ICentralService CreateInstance(IContext context)
        {
            var generator = new ProxyGenerator();
            return generator.CreateInterfaceProxyWithTarget<ICentralService>(
                new CentralSaveService(this.outputDir, this.statusDir, this.configDir),
                new ServiceInterceptor());
        }
    }
}
