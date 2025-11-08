using System.ServiceProcess;
using DeepcoolService.Monitoring;
using DeepcoolService.Utils;

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
            Logger.Info("Service starting...");
            worker = new MonitorWorker();
            worker.Start();
            Logger.Info("MonitorWorker started.");
        }

        protected override void OnStop()
        {
            Logger.Info("Service stopping...");

            // Request additional time from SCM (Service Control Manager) if needed
            // This prevents SCM from killing the service prematurely
            RequestAdditionalTime(5000); // 5 seconds

            try
            {
                worker?.Stop();
            }
            catch (System.Exception ex)
            {
                Logger.Error("Error during worker stop", ex);
            }

            Logger.Info("Service stopped.");
        }
    }
}
