﻿using System;
using IronFrame.Utilities;
using NLog;

namespace IronFrame
{
    internal interface ILocalTcpPortManager
    {
        int ReserveLocalPort(int port, string userName);
        void ReleaseLocalPort(int? port, string userName);

        void RemoveFirewallRules(string userName);
        void CreateOutboundFirewallRule(string userName, FirewallRuleSpec firewallRuleSpec);
    }

    internal class LocalTcpPortManager : ILocalTcpPortManager
    {
        private readonly IFirewallManager firewallManager;
        private readonly Logger log = LogManager.GetCurrentClassLogger();
        private readonly INetShRunner netShRunner;

        public LocalTcpPortManager()
            : this(new FirewallManager(), new NetShRunner())
        { }

        public LocalTcpPortManager(IFirewallManager firewallManager, INetShRunner netShRunner)
        {
            this.netShRunner = netShRunner;
            this.firewallManager = firewallManager;
        }

        /// <summary>
        ///     netsh http add urlacl http://*:8888/ user=warden_094850238
        /// </summary>
        public int ReserveLocalPort(int port, string userName)
        {
            if (String.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentNullException("userName");
            }

            if (port == default(ushort))
            {
                port = IPUtilities.RandomFreePort();
            }

            log.Info("Reserving port {0} for user {1}", port, userName);

            //string arguments = String.Format("http add urlacl http://*:{0}/ user={1}", port, userName);
            if (netShRunner.AddRule(port, userName))
            {
                try
                {
                    firewallManager.OpenPort(port, userName);
                }
                catch (Exception ex)
                {
                    throw new Exception(String.Format("Error adding firewall rule for port '{0}', user '{1}'", port, userName), ex);
                }
            }
            else
            {
                throw new Exception(String.Format("Error reserving port '{0}' for user '{1}'", port, userName));
            }

            return port;
        }

        /// <summary>
        ///     netsh http delete urlacl http://*:8888/
        /// </summary>
        public void ReleaseLocalPort(int? port, string userName)
        {
            if (String.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentNullException("userName");
            }

            log.Info("Releasing port {0} for user {1}", port, userName);


            if (port.HasValue)
            {
                netShRunner.DeleteRule(port.Value);
            }

            firewallManager.ClosePort(userName);
        }

        public void RemoveFirewallRules(string userName)
        {
            firewallManager.RemoveAllFirewallRules(userName);
        }

        public void CreateOutboundFirewallRule(string userName, FirewallRuleSpec firewallRuleSpec)
        {
            firewallManager.CreateOutboundFirewallRule(userName, firewallRuleSpec);
        }
    }
}