﻿using System;
using Foundatio.Repositories.Elasticsearch.Configuration;
using Foundatio.Repositories.Elasticsearch.Tests.Repositories.Configuration.Types;

namespace Foundatio.Repositories.Elasticsearch.Tests.Repositories.Configuration.Indexes {
    public sealed class DailyLogEventIndex : DailyIndex {
        public DailyLogEventIndex(IElasticConfiguration configuration) : base(configuration, "daily-logevents", 1) {
            AddType(LogEvent = new LogEventType(this));
            AddAlias($"{Name}-today", TimeSpan.FromDays(1));
            AddAlias($"{Name}-last7days", TimeSpan.FromDays(7));
            AddAlias($"{Name}-last30days", TimeSpan.FromDays(30));
        }

        public LogEventType LogEvent { get; }
    }
}