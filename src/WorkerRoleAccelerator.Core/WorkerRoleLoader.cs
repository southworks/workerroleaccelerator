namespace WorkerRoleAccelerator.Core
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Security.Permissions;
    using System.Threading;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.ServiceRuntime;
    using Microsoft.WindowsAzure.StorageClient;

    public class WorkerRoleLoader
    {
        private const string AppDomainName = "WorkerRoleAccelerator";
        private const string LocalStorageConfigFileFolder = "ConfigFiles";
        private const string DataConfigurationKey = "DataConnectionString";
        private const string ReadmeFileName = "__readme.txt";
        private const string EntryPointFileName = "__entrypoint.txt";
        private const string ExceptionFileName = "__an_error_occured.txt";

        private AppDomain pluginDomain;
        private CloudStorageAccount storageAccount;
        private CloudBlobClient blobStorage;

        public WorkerRoleLoader()
        {
            this.LastModified = DateTime.MinValue;

            this.storageAccount = CloudStorageAccount.FromConfigurationSetting(DataConfigurationKey);
            this.blobStorage = storageAccount.CreateCloudBlobClient();
        }

        public DateTime LastModified { get; private set; }

        /// <summary>
        /// If there is a new plugin assembly in the storage account specified in "DataConnectionString".
        /// The worker role entry point and its dependencies must be loaded to the container specified int he ServiceConfiguration.cscfg.
        /// </summary>
        /// <returns>An instance of WorkerRoleLoader or null</returns>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public void Poll()
        {
            var containerName = RoleEnvironment.GetConfigurationSettingValue("WorkerRoleEntryPointContainerName");

            if (containerName == null)
            {
                this.EnsureContainer("WorkerRoleAcceleratorError");
                this.Log("WorkerRoleAcceleratorError", "No configuration setting was found. Make sure to provide a value for the PluginContainer key in the ServiceConfiguration.cscfg file");

                return;
            }

            this.EnsureContainer(containerName);
            this.EnsureReadmeFile(containerName);
            this.EnsureBlob(containerName, EntryPointFileName);

            string assemblyName = this.ReadAssemblyNameFromEntryPointFile(containerName, EntryPointFileName);

            if (string.IsNullOrEmpty(assemblyName))
                return;

            if (!this.BlobExists(containerName, assemblyName))
            {
                this.Log(containerName, string.Format("Assembly not found:'{0}' in container '{1}'", assemblyName, containerName));

                return;
            }

            string configPath = DownloadConfigurationFileToLocalStorage(containerName, assemblyName + ".config");

            DateTime lastModified = GetFileLastModifiedDateFromBlob(containerName, assemblyName);

            if (lastModified > this.LastModified)
            {
                this.UnloadAppdomain();
                this.Execute(containerName, assemblyName, configPath);

                this.DeleteBlob(containerName, ExceptionFileName);
                this.LastModified = lastModified;
            }
        }

        private void DeleteBlob(string containerName, string blobName)
        {
            var blob = this.blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(blobName)
                                       .DeleteIfExists();
        }

        private DateTime GetFileLastModifiedDateFromBlob(string containerName, string blobName)
        {
            var blob = this.blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(blobName);
            blob.FetchAttributes();
            var lastModified = blob.Attributes.Properties.LastModifiedUtc;

            return lastModified;
        }

        private string DownloadConfigurationFileToLocalStorage(string containerName, string configFileBlobName)
        {
            var blob = this.blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(configFileBlobName);

            string path = null;
            if (blob.Exists())
            {
                LocalResource localStorage = RoleEnvironment.GetLocalResource(LocalStorageConfigFileFolder);
                path = Path.Combine(localStorage.RootPath, configFileBlobName);
                File.WriteAllBytes(path, blob.DownloadByteArray());
            }

            return path;
        }

        private string ReadAssemblyNameFromEntryPointFile(string containerName, string blobName)
        {
            string content = this.blobStorage.GetContainerReference(containerName)
                                            .GetBlobReference(blobName)
                                            .DownloadText();

            return content;
        }

        private bool BlobExists(string containerName, string blobName)
        {
            var blob = this.blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(blobName);

            return blob.Exists();
        }

        private void Log(string container, string message)
        {
            try
            {
                blobStorage.GetContainerReference(container)
                           .GetBlobReference(ExceptionFileName)
                           .UploadText(message);
            }
            catch (StorageClientException ex)
            {
                Trace.TraceError(ex.Message);
            }
        }

        private void EnsureReadmeFile(string containerName)
        {
            if (!this.BlobExists(containerName, ReadmeFileName))
            {
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WorkerRoleAccelerator.Core.Readme.txt");
                string readme = new StreamReader(stream).ReadToEnd();

                this.blobStorage.GetContainerReference(containerName)
                            .GetBlobReference(ReadmeFileName)
                            .UploadText(readme);
            }
        }

        private void EnsureBlob(string containerName, string blobName)
        {
            if (!this.BlobExists(containerName, blobName))
            {
                this.blobStorage.GetContainerReference(containerName)
                            .GetBlobReference(blobName)
                            .UploadByteArray(new byte[] { });
            }
        }

        private void EnsureContainer(string containerName)
        {
            CloudBlobContainer container = blobStorage.GetContainerReference(containerName);
            container.CreateIfNotExist();
        }

        /// <summary>
        /// If a new plugin assembly is detected, this method sets a new application domain and loads the plugin on it.
        /// Finally, creates an instance of the proxy and call its methods (OnStart and Run) in a new thread.
        /// </summary>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        private void Execute(string containerName, string entryPointAssemblyName, string configFilePath)
        {
            try
            {
                if (this.pluginDomain == null)
                {
                    // setup a new application domain and load the plug-in
                    var setupInfo = new AppDomainSetup()
                    {
                        ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                        ConfigurationFile = !string.IsNullOrEmpty(configFilePath) ? configFilePath : null
                    };

                    this.pluginDomain = AppDomain.CreateDomain(AppDomainName, null, setupInfo);
                }

                object[] args = new[] { containerName, entryPointAssemblyName };
                var proxy = this.pluginDomain.CreateInstanceAndUnwrap(typeof(ProxyRoleEntryPoint).Assembly.FullName,
                                                                      typeof(ProxyRoleEntryPoint).FullName,
                                                                      false,
                                                                      BindingFlags.Default,
                                                                      null,
                                                                      args,
                                                                      null,
                                                                      null) as ProxyRoleEntryPoint;

                new Thread(() =>
                {
                    try
                    {
                        if (proxy.OnStart())
                        {
                            proxy.Run();
                        }
                    }
                    catch (AppDomainUnloadedException)
                    {
                        // A new Plugin was found and the AppDomain was unloaded
                    }
                    catch (Exception ex)
                    {
                        this.Log(containerName, string.Format("'Plugin.Run()' method throws and unhandled exception. Exception message: '{0}'.", ex.ToString()));
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                this.Log(containerName, string.Format("Unrecoverable error in plug-in '{0}'.\n{1}", entryPointAssemblyName, ex.ToString()));
                this.UnloadAppdomain();
            }
        }

        /// <summary>
        /// Unload the Application Domain.
        /// This will abort any threads currently executing in the domain.
        /// </summary>
        public void UnloadAppdomain()
        {
            if (this.pluginDomain != null)
            {
                Trace.TraceInformation("Unloading AppDomain for plugin '{0}'.", AppDomainName);
                AppDomain.Unload(this.pluginDomain);
                this.pluginDomain = null;
            }
        }
    }
}