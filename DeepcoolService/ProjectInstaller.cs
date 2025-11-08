using System.ComponentModel;
using System.Configuration.Install;

namespace DeepcoolService
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
        }

        private void serviceInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {
            // Optional: post-install actions (e.g., start service) can be added here.
        }

        private void serviceProcessInstaller1_AfterInstall(object sender, InstallEventArgs e)
        {
            // Placeholder for any account-related post-install logic.
        }
    }
}
