﻿using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Orleans.Messaging;
using Orleans.Runtime.Configuration;
using Orleans.Runtime.MembershipService;
using OrleansAWSUtils.Storage;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Orleans.Runtime.Membership
{
    internal class DynamoDBGatewayListProvider : IGatewayListProvider
    {
        private const string TABLE_NAME_DEFAULT_VALUE = "OrleansSiloInstances";

        private DynamoDBStorage storage;
        private TimeSpan gatewayListRefreshPeriod;
        private string deploymentId;
        private readonly string INSTANCE_STATUS_ACTIVE = ((int)SiloStatus.Active).ToString();
        private readonly ILoggerFactory loggerFactory;
        public DynamoDBGatewayListProvider(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        #region Implementation of IGatewayListProvider

        public Task InitializeGatewayListProvider(ClientConfiguration conf)
        {
            gatewayListRefreshPeriod = conf.GatewayListRefreshPeriod;
            deploymentId = conf.DeploymentId;

            storage = new DynamoDBStorage(conf.DataConnectionString, loggerFactory);
            return storage.InitializeTable(TABLE_NAME_DEFAULT_VALUE,
                new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = SiloInstanceRecord.DEPLOYMENT_ID_PROPERTY_NAME, KeyType = KeyType.HASH },
                    new KeySchemaElement { AttributeName = SiloInstanceRecord.SILO_IDENTITY_PROPERTY_NAME, KeyType = KeyType.RANGE }
                },
                new List<AttributeDefinition>
                {
                    new AttributeDefinition { AttributeName = SiloInstanceRecord.DEPLOYMENT_ID_PROPERTY_NAME, AttributeType = ScalarAttributeType.S },
                    new AttributeDefinition { AttributeName = SiloInstanceRecord.SILO_IDENTITY_PROPERTY_NAME, AttributeType = ScalarAttributeType.S }
                });
        }

        public async Task<IList<Uri>> GetGateways()
        {
            var expressionValues = new Dictionary<string, AttributeValue>
            {
                { $":{SiloInstanceRecord.DEPLOYMENT_ID_PROPERTY_NAME}", new AttributeValue(deploymentId) },
                { $":{SiloInstanceRecord.STATUS_PROPERTY_NAME}", new AttributeValue { N = INSTANCE_STATUS_ACTIVE } },
                { $":{SiloInstanceRecord.PROXY_PORT_PROPERTY_NAME}", new AttributeValue { N = "0"} }
            };

            var expression =
                $"{SiloInstanceRecord.DEPLOYMENT_ID_PROPERTY_NAME} = :{SiloInstanceRecord.DEPLOYMENT_ID_PROPERTY_NAME} " +
                $"AND {SiloInstanceRecord.STATUS_PROPERTY_NAME} = :{SiloInstanceRecord.STATUS_PROPERTY_NAME} " + 
                $"AND {SiloInstanceRecord.PROXY_PORT_PROPERTY_NAME} > :{SiloInstanceRecord.PROXY_PORT_PROPERTY_NAME}";

            var records = await storage.ScanAsync<Uri>(TABLE_NAME_DEFAULT_VALUE, expressionValues,
                expression, gateway =>
                {
                    return SiloAddress.New(
                        new IPEndPoint(
                            IPAddress.Parse(gateway[SiloInstanceRecord.ADDRESS_PROPERTY_NAME].S),
                            int.Parse(gateway[SiloInstanceRecord.PROXY_PORT_PROPERTY_NAME].N)),
                            int.Parse(gateway[SiloInstanceRecord.GENERATION_PROPERTY_NAME].N)).ToGatewayUri();
                });

            return records;
        }

        public TimeSpan MaxStaleness
        {
            get { return gatewayListRefreshPeriod; }
        }

        public bool IsUpdatable
        {
            get { return true; }
        }

        #endregion
    }
}
