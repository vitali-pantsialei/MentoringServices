using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentralService
{
    public interface ICentralService
    {
        void Start();
        void Stop();
        void Scan(object obj);
        void Status(object obj);
    }
}
