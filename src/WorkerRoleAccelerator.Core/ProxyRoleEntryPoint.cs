namespace WorkerRoleAccelerator.Core
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.ServiceRuntime;
    using Microsoft.WindowsAzure.StorageClient;

    public class ProxyRoleEntryPoint : MarshalByRefObject
    {
        private RoleEntryPoint workerRole;

        public ProxyRoleEntryPoint(string containerName, string entryPointAssemblyName)
        {
            CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) =>
            {
                configSetter(RoleEnvironment.GetConfigurationSettingValue(configName));
            });

            var storageAccount = CloudStorageAccount.FromConfigurationSetting("DataConnectionString");
            var blobStorage = storageAccount.CreateCloudBlobClient();

            byte[] workerAssemblyBytes = blobStorage.GetContainerReference(containerName)
                                                    .GetBlobReference(entryPointAssemblyName)
                                                    .DownloadByteArray();

            Assembly entryPoint = Assembly.Load(workerAssemblyBytes);

            Type roleEntryPointType = entryPoint.GetTypes().Where(t => typeof(RoleEntryPoint).IsAssignableFrom(t)).FirstOrDefault();
            if (roleEntryPointType == null)
            {
                throw new ArgumentException("The assembly does not contain a RoleEntryPoint derived class");
            }

            this.workerRole = entryPoint.CreateInstance(roleEntryPointType.FullName) as RoleEntryPoint;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                string dependencyName = eventArgs.Name.Split(',')[0] + ".dll";
                CloudBlob dependency = blobStorage.GetContainerReference(containerName)
                                            .GetBlobReference(dependencyName);

                if (!dependency.Exists())
                {
                    throw new ArgumentException(string.Format("Assembly '{0}' does not exists in container '{1}'", dependencyName, containerName));
                }

                byte[] dependencyAssemblyBytes = dependency.DownloadByteArray();
                Assembly dependencyAssembly = Assembly.Load(dependencyAssemblyBytes);

                return dependencyAssembly;
            };
        }

        public bool OnStart()
        {
            if (this.workerRole == null)
            {
                return false;
            }

            return this.workerRole.OnStart();
        }

        public void OnStop()
        {
            if (this.workerRole != null)
            {
                this.workerRole.OnStop();
            }
        }

        public void Run()
        {
            if (this.workerRole != null)
            {
                this.workerRole.Run();
            }
        }
    }
}