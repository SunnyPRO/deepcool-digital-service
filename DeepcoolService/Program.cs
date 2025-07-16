using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace DeepcoolService
{
    internal static class Program
    {
        static void Main(string[] args)
        {

            if (args.Contains("/startService"))
            {
                ServiceController controller = new ServiceController("DeepcoolService");
                controller.Start();
                return;
            }

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new Service1()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
