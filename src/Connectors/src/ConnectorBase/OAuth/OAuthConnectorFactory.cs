﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using Steeltoe.Connector.Services;
using System;

namespace Steeltoe.Connector.OAuth
{
    public class OAuthConnectorFactory
    {
        private readonly SsoServiceInfo _info;
        private readonly OAuthConnectorOptions _config;
        private readonly OAuthConfigurer _configurer = new ();

        public OAuthConnectorFactory(SsoServiceInfo sinfo, OAuthConnectorOptions config)
        {
            _info = sinfo;
            _config = config;
        }

        public IOptions<OAuthServiceOptions> Create(IServiceProvider provider)
        {
            var opts = _configurer.Configure(_info, _config);
            return opts;
        }
    }
}
