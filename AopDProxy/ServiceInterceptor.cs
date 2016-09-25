using Castle.DynamicProxy;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AopDProxy
{
    public class ServiceInterceptor : IInterceptor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public void Intercept(IInvocation invocation)
        {
            logger.Info("{0} - {1} Params:", DateTime.Now.ToString(), invocation.Method.Name);
            foreach(object o in invocation.Arguments)
            {
                logger.Info(" " + o.ToString());
            }
            invocation.Proceed();
        }
    }
}
