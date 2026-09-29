using System;
using System.Runtime.CompilerServices;

using Openness;
using Openness.Shared;

namespace Satellite.ImportObjects
{
    /// <summary>
    /// The V21 executable. Everything it does is install the assembly resolver and hand the
    /// shared application a way to reach TIA Portal.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// **`STAThread` because this is a WPF application whose `Main` is written by hand.** There
        /// is no `App.xaml`, precisely so that one application can serve two executables.
        /// </summary>
        [STAThread]
        public static int Main(string[] arguments)
        {
            OpennessAssemblies.Install("21.0");

            return Start(arguments);
        }

        /// <summary>
        /// **Kept out of `Main` and never inlined, and that is load-bearing.** The JIT resolves the
        /// types a method mentions when it compiles that method, so a `Main` naming the session
        /// would try to load `Siemens.Engineering.Base` before its own first line - the one that
        /// installs the resolver - had run.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Start(string[] arguments) =>
            ImportObjectsApp.Run(log => new ImportSession(log), OpennessAssemblies.Report, arguments, "V21");
    }
}
