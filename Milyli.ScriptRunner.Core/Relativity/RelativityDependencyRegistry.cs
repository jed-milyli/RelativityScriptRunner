﻿namespace Milyli.ScriptRunner.Core.Relativity
{
    using Configuration;
    using Email;
    using Factories;
    using Interfaces;
    using Repositories;
    using Repositories.Interfaces;
    using MilyliDependencies.Framework.Relativity;
    using global::Relativity.API;
    using StructureMap;
    public class RelativityDependencyRegistry : Registry
    {
        public RelativityDependencyRegistry(IHelper helper, int workspaceId)
        {
            this.For<IHelper>().Use(helper);
            this.For<IServicesMgr>().Use(helper.GetServicesManager());
            this.For<IRelativityContext>().Use(new RelativityContext(workspaceId));

            this.For<IInstanceConnectionFactory>().Use<RelativityInstanceConnectionFactory>();
            this.For<IWorkspaceConnectionFactory>().Use<RelativityWorkspaceConnectionFactory>();
            this.For<IWorkspaceDependencyFactory>().Use<RelativityWorkspaceDependencyFactory>();

            this.For<IConfigurationCryptoService>().Use<ConfigurationCryptoService>();
            this.For<IConfigurationRepository<EmailConfiguration>>().Use<ConfigurationRepository<EmailConfiguration>>();
            this.For<IEmailService>().Use<EmailService>();
        }
    }
}
