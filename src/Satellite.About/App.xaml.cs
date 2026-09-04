using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UI.Shared;

namespace Satellite.About
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!SingleInstance.Claim("Satellite.About"))
            {
                Shutdown();
                return;
            }

            new MainWindow().Show();
        }
    }
}
