namespace SampleWorkerRole
{
    using System.Configuration;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.WindowsAzure.ServiceRuntime;
    using SampleWorkerRoleDependency;

    public class WorkerRole : RoleEntryPoint
    {
        public override void Run()
        {
            while (true)
            {
                Trace.TraceInformation("Running from inside the SampleWorkerRole");
                Trace.TraceInformation("Value from config '{0}'", ConfigurationManager.AppSettings["test"]);
                new Foo().Bar();

                Thread.Sleep(5000);
            }
        }
    }
}