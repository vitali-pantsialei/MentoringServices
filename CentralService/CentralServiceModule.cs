using Ninject.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentralService
{
    public class CentralServiceModule : NinjectModule
    {

        public override void Load()
        {
            this.Bind<ICentralService>().ToProvider<CentralServiceProvider>();
        }
    }
}
