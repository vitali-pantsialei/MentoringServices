using NLog;
using PostSharp.Aspects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeRewritingInterceptor
{
    [Serializable]
    public class PostSharpInterceptorAspect : OnMethodBoundaryAspect
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public override void OnEntry(MethodExecutionArgs args)
        {
            logger.Info("{0} - ENTER METHOD {1} Params:", DateTime.Now.ToString(), args.Method.Name);
            foreach (var o in args.Arguments)
            {
                logger.Info(" " + o.ToString());
            }
            args.FlowBehavior = FlowBehavior.Default;
        }

        public override void OnSuccess(MethodExecutionArgs args)
        {
            logger.Info("{0} - EXIT METHOD {1} Params:", DateTime.Now.ToString(), args.Method.Name);
            foreach (var o in args.Arguments)
            {
                logger.Info(" " + o.ToString());
            }
            args.FlowBehavior = FlowBehavior.Default;
        }
    }
}
