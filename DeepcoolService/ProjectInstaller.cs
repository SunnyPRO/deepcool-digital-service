using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace DeepcoolService
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
            this.serviceInstaller1.StartType = ServiceStartMode.Automatic;
            this.serviceProcessInstaller1.Account = ServiceAccount.LocalSystem;
        }

        private void serviceInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {

        }

        private void serviceProcessInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {

        }
    }
}
