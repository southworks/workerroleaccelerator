using System.Diagnostics;

namespace SampleWorkerRoleDependency
{
    public class Foo
    {
        public void Bar()
        {
            Trace.TraceInformation("Running from inside the SampleWorkerRoleDependency");
        }
    }
}