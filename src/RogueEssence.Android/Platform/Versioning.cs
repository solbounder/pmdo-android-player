using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RogueEssence
{
    public static class Versioning
    {
        public static Version GetVersion()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "PMDC");
            return (gameAssembly ?? Assembly.GetEntryAssembly() ?? typeof(Versioning).Assembly).GetName().Version ?? new Version(0, 8, 12, 0);
        }

        public static string GetDotNetInfo() => RuntimeInformation.FrameworkDescription;
    }
}
