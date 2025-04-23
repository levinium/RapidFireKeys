using RapidFireKeys;
using System;
using System.Windows.Forms;

namespace RapidFireKeys
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize(); // .NET 6+ only
            Application.Run(new Form1());
        }
    }
}
