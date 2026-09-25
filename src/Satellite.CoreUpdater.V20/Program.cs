using System;
using System.Runtime.CompilerServices;

using Satellite.CoreUpdater.Tia;

namespace Satellite.CoreUpdater
{
    /// <summary>
    /// The V17-V20 executable. Everything it does is install the assembly resolver and hand
    /// the shared application a way to reach TIA Portal.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// **`STAThread` because this is a WPF application whose `Main` is written by hand.**
        /// An `App.xaml` would have generated one with the attribute already on it; there is
        /// no `App.xaml` here precisely so that one application can serve two executables.
        /// </summary>
        [STAThread]
        public static int Main(string[] arguments)
        {
            OpennessAssemblies.Install();

            return Start(arguments);
        }

        /// <summary>
        /// **Kept out of `Main` and never inlined, and that is load-bearing.** The JIT
        /// resolves the types a method mentions when it compiles that method, so a `Main`
        /// naming `TiaSession` would try to load `Siemens.Engineering` *before* its own first
        /// line ran - which is the line that installs the resolver. One method boundary the
        /// compiler is told to keep is what orders the two correctly.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Start(string[] arguments) =>
            CoreUpdaterApp.Run(log => new TiaSession(log), OpennessAssemblies.Report, arguments, "V20");
    }
}
