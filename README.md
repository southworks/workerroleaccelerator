Worker Role Accelerator (Windows Azure)
=============

This accelerator will allow you to deploy your worker roles faster by using blob storage instead of the portal.

First time deploy
-------

1. Add the `WorkerRoleAccelerator.Core` project to your Visual Studio Solution
2. Right click on the "Roles" folder on your Windows Azure project and select Add -> Worker Role Project in solution...
3. Select the WorkerRoleAccelerator.Core projet and click OK
4. Open the ServiceDefinition.csdef file and replace the <WorkerRole> node with this
    <WorkerRole name="WorkerRoleAccelerator.Core">
      <Imports>
          <Import moduleName="Diagnostics" />
      </Imports>
      <ConfigurationSettings>
          <Setting name="DataConnectionString" />
          <Setting name="WorkerRoleEntryPointContainerName" />
       </ConfigurationSettings>
       <LocalResources>
           <LocalStorage name="ConfigFiles" cleanOnRoleRecycle="true" sizeInMB="10" />
       </LocalResources>
    </WorkerRole>
5. Open the ServiceConfiguration.cscfg file and replace the <WorkerRole> node with the following. 
NOTE: make sure to replace the connection strings with your Windows Azure Storage account. 
    <Role name="WorkerRoleAccelerator.Core">
      <Instances count="1" />
      <ConfigurationSettings>
        <Setting name="Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString" value="{replace with your storage account}" />
        <Setting name="DataConnectionString" value="{replace with your storage account}" />
        <Setting name="WorkerRoleEntryPointContainerName" value="worker-role-accelerator" />
      </ConfigurationSettings>
    </Role>
6. Publish, deploy and wait for the deployment to be ready. Hopefully you will do this only once :)


Once you have the accelerator running...
-------

1. Use your tool of choice and connect to your Windows Azure blob storagte (e.g.: [CloudBerry](http://cloudberrylab.com/?page=explorer-azure), [Azure Storage Explorer](http://azurestorageexplorer.codeplex.com/)) 
2. Browse to the `worker-role-accelerator` container and drop your real worker role assembly and its dependencies in there
3. The accelerator will look at the `__entrypoint.txt` file to know what is the assembly that contains the RoleEntryPoint. So open that file and write down the file name of your dll (e.g.: SampleWorkerRole.dll).
4. The accelerator polls every 30 seconds the container looking for changes. So if you update the dll, it will pick it up, unload the AppDomain with the previous version and load the new one.

Remarks
-------

* You can add a configuration file that can be read by the worker role with the typical ConfigurationManager. The config file should be named the same as the entry point assembly + .config (usual .net convention).
* If anything wrong happens, the accelerator will write a file named `__an_error_occured.txt` in the container so you can see the exception
