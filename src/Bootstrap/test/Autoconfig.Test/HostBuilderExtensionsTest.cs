﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Serilog.Core;
using Steeltoe.Common.Diagnostics;
using Steeltoe.Common.Options;
using Steeltoe.Common.Security;
using Steeltoe.Connector;
using Steeltoe.Discovery;
using Steeltoe.Discovery.Eureka;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Extensions.Configuration.ConfigServer;
using Steeltoe.Extensions.Configuration.Kubernetes;
using Steeltoe.Extensions.Configuration.Placeholder;
using Steeltoe.Extensions.Configuration.RandomValue;
using Steeltoe.Extensions.Logging;
using Steeltoe.Extensions.Logging.DynamicSerilog;
using Steeltoe.Management;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.Endpoint.CloudFoundry;
using Steeltoe.Management.Endpoint.Hypermedia;
using Steeltoe.Management.OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Steeltoe.Bootstrap.Autoconfig.Test
{
    public class HostBuilderExtensionsTest
    {
        private static readonly Dictionary<string, string> _fastTests = new ()
        {
            { "spring:cloud:config:timeout", "10" },
            { "eureka:client:shouldRegister", "true" },
            { "eureka:client:eurekaServer:connectTimeoutSeconds", "1" },
            { "eureka:client:eurekaServer:retryCount", "0" },
            { "redis:client:abortOnConnectFail", "false" }
        };

        [Fact]
        public void ConfigServerConfiguration_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string>
                {
                    SteeltoeAssemblies.Steeltoe_Extensions_Configuration_ConfigServerCore,
                    SteeltoeAssemblies.Steeltoe_Extensions_Configuration_CloudFoundryCore
                });
            var hostBuilder = new HostBuilder().ConfigureAppConfiguration(cbuilder => cbuilder.AddInMemoryCollection(_fastTests));

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetServices<IConfiguration>().SingleOrDefault() as ConfigurationRoot;

            Assert.Equal(4, config.Providers.Count());
            Assert.Single(config.Providers.OfType<CloudFoundryConfigurationProvider>());
            Assert.Single(config.Providers.OfType<ConfigServerConfigurationProvider>());
        }

        [Fact]
        public void CloudFoundryConfiguration_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Extensions_Configuration_CloudFoundryCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetServices<IConfiguration>().SingleOrDefault() as ConfigurationRoot;

            Assert.Equal(2, config.Providers.Count());
            Assert.Single(config.Providers.OfType<CloudFoundryConfigurationProvider>());
        }

        [Fact(Skip = "Requires Kubernetes")]
        public void KubernetesConfiguration_IsAutowired()
        {
            Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "TEST");
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Extensions_Configuration_KubernetesCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetServices<IConfiguration>().SingleOrDefault() as ConfigurationRoot;

            Assert.Equal(5, config.Providers.Count());
            Assert.Equal(2, config.Providers.OfType<KubernetesConfigMapProvider>().Count());
            Assert.Equal(2, config.Providers.OfType<KubernetesSecretProvider>().Count());
        }

        [Fact]
        public void RandomValueConfiguration_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Extensions_Configuration_RandomValueBase })
                .ToList();
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetService<IConfiguration>() as ConfigurationRoot;

            Assert.Equal(2, config.Providers.Count());
            Assert.Single(config.Providers.OfType<RandomValueProvider>());
        }

        [Fact]
        public void PlaceholderResolver_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Extensions_Configuration_PlaceholderCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetServices<IConfiguration>().SingleOrDefault() as ConfigurationRoot;

            Assert.Single(config.Providers);
            Assert.Single(config.Providers.OfType<PlaceholderResolverProvider>());
        }

        [Fact]
        public void Connectors_AreAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Connector_ConnectorCore });
            var hostBuilder = new HostBuilder().ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(_fastTests));

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetService<IConfiguration>() as ConfigurationRoot;
            var services = host.Services;

            Assert.Equal(3, config.Providers.Count());
            Assert.Single(config.Providers.OfType<ConnectionStringConfigurationProvider>());
            Assert.NotNull(services.GetService<MySql.Data.MySqlClient.MySqlConnection>());
            Assert.NotNull(services.GetService<MongoDB.Driver.MongoClient>());
            Assert.NotNull(services.GetService<Oracle.ManagedDataAccess.Client.OracleConnection>());
            Assert.NotNull(services.GetService<Npgsql.NpgsqlConnection>());
            Assert.NotNull(services.GetService<RabbitMQ.Client.ConnectionFactory>());
            Assert.NotNull(services.GetService<StackExchange.Redis.ConnectionMultiplexer>());
            Assert.NotNull(services.GetService<System.Data.SqlClient.SqlConnection>());
        }

        [Fact]
        public void DynamicSerilog_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Extensions_Logging_DynamicSerilogCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();

            var loggerProvider = (IDynamicLoggerProvider)host.Services.GetService(typeof(IDynamicLoggerProvider));

            Assert.IsType<SerilogDynamicProvider>(loggerProvider);
        }

        [Fact]
        public void ServiceDiscoveryBase_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Discovery_ClientBase });
            var hostBuilder = new HostBuilder().ConfigureAppConfiguration(cbuilder => cbuilder.AddInMemoryCollection(_fastTests));

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var discoveryClient = host.Services.GetServices<IDiscoveryClient>();

            Assert.Single(discoveryClient);
            Assert.IsType<EurekaDiscoveryClient>(discoveryClient.First());
        }

        [Fact]
        public void ServiceDiscoveryCore_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Discovery_ClientCore });

            var host = new HostBuilder()
                .ConfigureAppConfiguration(cbuilder => cbuilder.AddInMemoryCollection(_fastTests))
                .AddSteeltoe(exclusions).Build();
            var discoveryClient = host.Services.GetServices<IDiscoveryClient>();

            Assert.Single(discoveryClient);
            Assert.IsType<EurekaDiscoveryClient>(discoveryClient.First());
        }

        [Fact]
        public async Task KubernetesActuators_AreAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Management_KubernetesCore });
            var hostBuilder = new HostBuilder().ConfigureWebHost(_testServerWithRouting);

            var host = await hostBuilder.AddSteeltoe(exclusions).StartAsync();
            var managementEndpoint = host.Services.GetServices<ActuatorEndpoint>();
            var filter = host.Services.GetServices<IStartupFilter>().FirstOrDefault();
            var testClient = host.GetTestServer().CreateClient();

            var response = await testClient.GetAsync("/actuator");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response = await testClient.GetAsync("/actuator/info");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response = await testClient.GetAsync("/actuator/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response = await testClient.GetAsync("/actuator/health/liveness");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"LivenessState\":\"CORRECT\"", await response.Content.ReadAsStringAsync());
            response = await testClient.GetAsync("/actuator/health/readiness");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"ReadinessState\":\"ACCEPTING_TRAFFIC\"", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CloudFoundryActuators_AreAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Management_CloudFoundryCore });
            var hostBuilder = new HostBuilder().ConfigureWebHost(_testServerWithRouting);

            var host = await hostBuilder.AddSteeltoe(exclusions).StartAsync();
            var managementOptions = host.Services.GetServices<IManagementOptions>();
            var managementEndpoint = host.Services.GetServices<ActuatorEndpoint>();
            var filter = host.Services.GetServices<IStartupFilter>().FirstOrDefault();
            var testClient = host.GetTestServer().CreateClient();

            Assert.Contains(managementOptions, t => t.GetType() == typeof(CloudFoundryManagementOptions));
            var response = await testClient.GetAsync("/actuator");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response = await testClient.GetAsync("/actuator/info");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response = await testClient.GetAsync("/actuator/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response = await testClient.GetAsync("/actuator/health/liveness");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"LivenessState\":\"CORRECT\"", await response.Content.ReadAsStringAsync());
            response = await testClient.GetAsync("/actuator/health/readiness");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"ReadinessState\":\"ACCEPTING_TRAFFIC\"", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public void AllActuators_AreAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Management_EndpointCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var managementEndpoint = host.Services.GetServices<ActuatorEndpoint>();
            var filter = host.Services.GetServices<IStartupFilter>().FirstOrDefault();

            Assert.Single(managementEndpoint);
            Assert.NotNull(filter);
            Assert.IsType<AllActuatorsStartupFilter>(filter);
        }

        [Fact]
        public void TracingBase_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Management_TracingBase });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();

            Assert.NotNull(host.Services.GetService<IHostedService>());
            Assert.NotNull(host.Services.GetService<ITracingOptions>());
            var tracerProvider = host.Services.GetService<TracerProvider>();
            Assert.NotNull(tracerProvider);
            Assert.NotNull(host.Services.GetService<IDynamicMessageProcessor>());

            // confirm instrumentation(s) were added as expected
            var instrumentations = tracerProvider.GetType().GetField("instrumentations", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tracerProvider) as List<object>;
            Assert.NotNull(instrumentations);
            Assert.Single(instrumentations);
            Assert.Contains(instrumentations, obj => obj.GetType().Name.Contains("Http"));
            Assert.DoesNotContain(instrumentations, obj => obj.GetType().Name.Contains("AspNetCore"));
        }

        [Fact]
        public void TracingCore_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Management_TracingCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();

            Assert.NotNull(host.Services.GetService<IHostedService>());
            Assert.NotNull(host.Services.GetService<ITracingOptions>());
            var tracerProvider = host.Services.GetService<TracerProvider>();
            Assert.NotNull(tracerProvider);
            Assert.NotNull(host.Services.GetService<IDynamicMessageProcessor>());

            // confirm instrumentation(s) were added as expected
            var instrumentations = tracerProvider.GetType().GetField("instrumentations", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tracerProvider) as List<object>;
            Assert.NotNull(instrumentations);
            Assert.Equal(2, instrumentations.Count);
            Assert.Contains(instrumentations, obj => obj.GetType().Name.Contains("Http"));
            Assert.Contains(instrumentations, obj => obj.GetType().Name.Contains("AspNetCore"));
        }

        [Fact]
        public void CloudFoundryContainerSecurity_IsAutowired()
        {
            var exclusions = SteeltoeAssemblies.AllAssemblies
                .Except(new List<string> { SteeltoeAssemblies.Steeltoe_Security_Authentication_CloudFoundryCore });
            var hostBuilder = new HostBuilder();

            var host = hostBuilder.AddSteeltoe(exclusions).Build();
            var config = host.Services.GetServices<IConfiguration>().SingleOrDefault() as ConfigurationRoot;

            Assert.Equal(2, config.Providers.Count());
            Assert.Single(config.Providers.OfType<PemCertificateProvider>());
            Assert.NotNull(host.Services.GetRequiredService<IOptions<CertificateOptions>>());
            Assert.NotNull(host.Services.GetRequiredService<ICertificateRotationService>());
            Assert.NotNull(host.Services.GetRequiredService<IAuthorizationHandler>());
        }

        private readonly Action<IWebHostBuilder> _testServerWithRouting = builder =>
                        builder.UseTestServer().ConfigureServices(s => s.AddRouting()).Configure(a => a.UseRouting());
    }
}
