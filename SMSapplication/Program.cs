using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SMSapplication
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AWMS_Server server = new AWMS_Server();
            server.connectDatabase();
            server.connectPort();
            //server.readSMS();
            server.runSystem();
            //SMSapplication app = new SMSapplication();
            //app.ShowDialog();
        }
    }
}