using System.ServiceProcess;
using DeepcoolService.Monitoring;

namespace DeepcoolService
{
    public partial class Service1 : ServiceBase
    {
        private MonitorWorker worker;
        public Service1()
        {
            InitializeComponent();
            this.CanStop = true;
            this.AutoLog = false;
            this.CanPauseAndContinue = false;
        }

        protected override void OnStart(string[] args)
        {
            worker = new MonitorWorker();
            worker.Start();
        }

        protected override void OnStop()
        {
            worker.Stop();
        }
    }
}
