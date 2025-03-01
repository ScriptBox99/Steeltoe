// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#pragma warning disable 0436

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Steeltoe.Common;
using Steeltoe.Common.HealthChecks;
using Steeltoe.Common.Kubernetes;
using Steeltoe.Common.Reflection;
using Steeltoe.Connector;
using Steeltoe.Connector.MongoDb;
using Steeltoe.Connector.MySql;
using Steeltoe.Connector.Oracle;
using Steeltoe.Connector.PostgreSql;
using Steeltoe.Connector.RabbitMQ;
using Steeltoe.Connector.Redis;
using Steeltoe.Connector.SqlServer;
using Steeltoe.Discovery.Client;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Extensions.Configuration.ConfigServer;
using Steeltoe.Extensions.Configuration.Kubernetes;
using Steeltoe.Extensions.Configuration.Placeholder;
using Steeltoe.Extensions.Configuration.RandomValue;
using Steeltoe.Extensions.Logging.DynamicSerilog;
using Steeltoe.Management.CloudFoundry;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.Kubernetes;
using Steeltoe.Management.Tracing;
using Steeltoe.Security.Authentication.CloudFoundry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Steeltoe.Bootstrap.Autoconfig
{
    public static class HostBuilderExtensions
    {
        private const string _loggerName = "Steeltoe.Autoconfig";
        private static readonly HashSet<string> _missingAssemblies = new ();
        private static IEnumerable<string> _excludedAssemblies;
        private static ILoggerFactory _loggerFactory;

        static HostBuilderExtensions()
        {
            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += LoadAnyVersion;

            static Assembly LoadAnyVersion(object sender, ResolveEventArgs args)
            {
                // Load whatever version available - strip out version and culture info
                static string GetSimpleName(string assemblyName) => new Regex(",.*").Replace(assemblyName, string.Empty);
                var name = GetSimpleName(args.Name);
                if (_missingAssemblies.Contains(name))
                {
                    return null;
                }

                var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToDictionary(x => x.GetName().Name, x => x);
                if (assemblies.TryGetValue(name, out var assembly))
                {
                    return assembly;
                }

                if (args.Name?.Contains(".resources") ?? false)
                {
                    return args.RequestingAssembly;
                }

                _missingAssemblies.Add(name); // throw it in there to prevent recursive attempts to resolve
                assembly = Assembly.Load(name);
                _missingAssemblies.Remove(name);
                return assembly;
            }
        }

        /// <summary>
        /// Automatically configure Steeltoe packages that have been added as NuGet references.<para />
        /// PLEASE NOTE: No extensions to IApplicationBuilder will be configured!
        /// </summary>
        /// <param name="hostBuilder">Your <see cref="IHostBuilder" /></param>
        /// <param name="exclusions">A list of assemblies to exclude from auto-configuration. For ease of use, select from <see cref="SteeltoeAssemblies" /></param>
        /// <param name="loggerFactory">For logging within auto-configuration</param>
        public static IHostBuilder AddSteeltoe(this IHostBuilder hostBuilder, IEnumerable<string> exclusions = null, ILoggerFactory loggerFactory = null)
        {
            _excludedAssemblies = exclusions ?? new List<string>();
            _loggerFactory = loggerFactory;
            var logger = loggerFactory?.CreateLogger(_loggerName) ?? NullLogger.Instance;
            hostBuilder.Properties[_loggerName] = logger;

            if (!hostBuilder.WireIfAnyLoaded(WireConfigServer, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_ConfigServerBase, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_ConfigServerCore))
            {
                hostBuilder.WireIfAnyLoaded(WireCloudFoundryConfiguration, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_CloudFoundryBase, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_CloudFoundryCore);
            }

            if ((IsAssemblyLoaded(SteeltoeAssemblies.Steeltoe_Extensions_Configuration_KubernetesBase) || IsAssemblyLoaded(SteeltoeAssemblies.Steeltoe_Extensions_Configuration_KubernetesCore)) && Platform.IsKubernetes)
            {
                WireKubernetesConfiguration(hostBuilder);
            }

            hostBuilder.WireIfLoaded(WireRandomValueProvider, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_RandomValueBase);
            hostBuilder.WireIfAnyLoaded(WirePlaceholderResolver, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_PlaceholderBase, SteeltoeAssemblies.Steeltoe_Extensions_Configuration_PlaceholderCore);

            if (hostBuilder.WireIfLoaded(WireConnectorConfiguration, SteeltoeAssemblies.Steeltoe_Connector_ConnectorCore))
            {
                hostBuilder.WireIfAnyLoaded(WireMySqlConnection, MySqlTypeLocator.Assemblies);
                hostBuilder.WireIfAnyLoaded(WireMongoClient, MongoDbTypeLocator.Assemblies);
                hostBuilder.WireIfAnyLoaded(WireOracleConnection, OracleTypeLocator.Assemblies);
                hostBuilder.WireIfAnyLoaded(WirePostgresConnection, PostgreSqlTypeLocator.Assemblies);
                hostBuilder.WireIfAnyLoaded(WireRabbitMqConnection, RabbitMQTypeLocator.Assemblies);
                hostBuilder.WireIfAnyLoaded(WireRedisConnectionMultiplexer, RedisTypeLocator.StackExchangeAssemblies);
                hostBuilder.WireIfAnyLoaded(WireDistributedRedisCache, RedisTypeLocator.MicrosoftAssemblies.Except(new[] { "Microsoft.Extensions.Caching.Abstractions" }).ToArray());
                hostBuilder.WireIfAnyLoaded(WireSqlServerConnection, SqlServerTypeLocator.Assemblies);
            }

            hostBuilder.WireIfLoaded(WireDynamicSerilog, SteeltoeAssemblies.Steeltoe_Extensions_Logging_DynamicSerilogCore);
            hostBuilder.WireIfAnyLoaded(WireDiscoveryClient, SteeltoeAssemblies.Steeltoe_Discovery_ClientBase, SteeltoeAssemblies.Steeltoe_Discovery_ClientCore);

            if (IsAssemblyLoaded(SteeltoeAssemblies.Steeltoe_Management_KubernetesCore) || IsAssemblyLoaded(SteeltoeAssemblies.Steeltoe_Management_CloudFoundryCore))
            {
                hostBuilder.WireIfLoaded(WireKubernetesActuators, SteeltoeAssemblies.Steeltoe_Management_KubernetesCore);
                hostBuilder.WireIfLoaded(WireCloudFoundryActuators, SteeltoeAssemblies.Steeltoe_Management_CloudFoundryCore);
            }
            else
            {
                hostBuilder.WireIfLoaded(WireAllActuators, SteeltoeAssemblies.Steeltoe_Management_EndpointCore);
            }

            if (!hostBuilder.WireIfLoaded(WireDistributedTracingCore, SteeltoeAssemblies.Steeltoe_Management_TracingCore))
            {
                hostBuilder.WireIfLoaded(WireDistributedTracingBase, SteeltoeAssemblies.Steeltoe_Management_TracingBase);
            }

            hostBuilder.WireIfLoaded(WireCloudFoundryContainerIdentity, SteeltoeAssemblies.Steeltoe_Security_Authentication_CloudFoundryCore);
            return hostBuilder;
        }

        private static bool IsAssemblyLoaded(string assemblyName)
        {
            if (_excludedAssemblies.Contains(assemblyName))
            {
                return false;
            }

            return ReflectionHelpers.IsAssemblyLoaded(assemblyName);
        }

        private static bool WireIfLoaded(this IHostBuilder hostBuilder, Action<IHostBuilder> action, params string[] assembly)
        {
            if (assembly.All(IsAssemblyLoaded))
            {
                action(hostBuilder);
                return true;
            }

            return false;
        }

        private static bool WireIfAnyLoaded(this IHostBuilder hostBuilder, Action<IHostBuilder> action, params string[] assembly)
        {
            if (assembly.Any(IsAssemblyLoaded))
            {
                action(hostBuilder);
                return true;
            }

            return false;
        }

        private static IHostBuilder Log(this IHostBuilder host, string message)
        {
            var logger = (ILogger)host.Properties[_loggerName];
            logger.LogInformation(message);
            return host;
        }

        #region Config Providers
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireConfigServer(this IHostBuilder hostBuilder) =>
            hostBuilder
                .ConfigureAppConfiguration((context, cfg) => cfg.AddConfigServer(context.HostingEnvironment, _loggerFactory))
                .ConfigureServices((context, services) =>
                {
                    services.ConfigureConfigServerClientOptions(context.Configuration);
                    services.TryAddSingleton(serviceProvider => serviceProvider.GetRequiredService<IConfiguration>() as IConfigurationRoot);
                    services.TryAddSingleton<IHostedService, ConfigServerHostedService>();
                    services.TryAddSingleton<IHealthContributor, ConfigServerHealthContributor>();
                })
                .Log(LogMessages.WireConfigServer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireCloudFoundryConfiguration(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureAppConfiguration(cfg => cfg.AddCloudFoundry()).Log(LogMessages.WireCloudFoundryConfiguration);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireKubernetesConfiguration(this IHostBuilder hostBuilder) =>
            hostBuilder
                .ConfigureAppConfiguration(cfg => cfg.AddKubernetes(loggerFactory: _loggerFactory))
                .ConfigureServices(serviceCollection => serviceCollection.AddKubernetesApplicationInstanceInfo().AddHostedService<KubernetesHostedService>())
                .Log(LogMessages.WireKubernetesConfiguration);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireRandomValueProvider(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureAppConfiguration(cfg => cfg.AddRandomValueSource(_loggerFactory)).Log(LogMessages.WireRandomValueProvider);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WirePlaceholderResolver(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureAppConfiguration(cfg => cfg.AddPlaceholderResolver(_loggerFactory)).Log(LogMessages.WirePlaceholderResolver);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireConnectorConfiguration(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureAppConfiguration((host, svc) => svc.AddConnectionStrings()).Log(LogMessages.WireConnectorsConfiguration);
        #endregion

        #region Connectors
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireMySqlConnection(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddMySqlConnection(host.Configuration)).Log(LogMessages.WireMySqlConnection);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireMongoClient(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddMongoClient(host.Configuration)).Log(LogMessages.WireMongoClient);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireOracleConnection(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddOracleConnection(host.Configuration)).Log(LogMessages.WireOracleConnection);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WirePostgresConnection(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddPostgresConnection(host.Configuration)).Log(LogMessages.WirePostgresConnection);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireRabbitMqConnection(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddRabbitMQConnection(host.Configuration)).Log(LogMessages.WireRabbitMqConnection);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireRedisConnectionMultiplexer(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddRedisConnectionMultiplexer(host.Configuration)).Log(LogMessages.WireRedisConnectionMultiplexer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireDistributedRedisCache(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddDistributedRedisCache(host.Configuration)).Log(LogMessages.WireDistributedRedisCache);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireSqlServerConnection(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddSqlServerConnection(host.Configuration)).Log(LogMessages.WireSqlServerConnection);
        #endregion

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireDiscoveryClient(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddDiscoveryClient()).Log(LogMessages.WireDiscoveryClient);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireDistributedTracingBase(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddDistributedTracing()).Log(LogMessages.WireDistributedTracing);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireDistributedTracingCore(this IHostBuilder hostBuilder) =>
            hostBuilder.ConfigureServices((host, svc) => svc.AddDistributedTracingAspNetCore()).Log(LogMessages.WireDistributedTracing);

        [MethodImpl(MethodImplOptions.NoInlining)]
#pragma warning disable CS0618 // Type or member is obsolete
        private static void WireCloudFoundryActuators(this IHostBuilder hostBuilder) => hostBuilder.AddCloudFoundryActuators().Log(LogMessages.WireCloudFoundryActuators);
#pragma warning restore CS0618 // Type or member is obsolete

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireKubernetesActuators(this IHostBuilder hostBuilder) =>
            hostBuilder.AddKubernetesActuators().Log(LogMessages.WireKubernetesActuators);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireAllActuators(this IHostBuilder hostBuilder) => hostBuilder.AddAllActuators().Log(LogMessages.WireAllActuators);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireDynamicSerilog(this IHostBuilder hostBuilder) => hostBuilder.AddDynamicSerilog().Log(LogMessages.WireDynamicSerilog);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireCloudFoundryContainerIdentity(this IHostBuilder hostBuilder) => hostBuilder
            .ConfigureAppConfiguration(cfg => cfg.AddCloudFoundryContainerIdentity())
            .ConfigureServices((host, svc) => svc.AddCloudFoundryCertificateAuth())
            .Log(LogMessages.WireCloudFoundryContainerIdentity);
    }
}

#pragma warning restore 0436
