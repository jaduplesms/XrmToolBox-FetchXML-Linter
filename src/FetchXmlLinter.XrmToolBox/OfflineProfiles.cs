using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace FetchXmlLinter.XrmToolBox
{
    internal sealed class OfflineProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool? IsManaged { get; set; }
        public bool? IsValidated { get; set; }
        public override string ToString() => Name;
    }

    internal sealed class OfflineProfileItem
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }
        public string ProfileName { get; set; } = "";
        public string EntityName { get; set; } = "";
        public int? DistributionCriteria { get; set; }
        [JsonIgnore]
        public string ConfiguredXml { get; set; } = "";
        [JsonIgnore]
        public string ReferencedViewXml { get; set; } = "";
        public Guid? ReferencedViewId { get; set; }
        public string ReferencedViewError { get; set; } = "";
        [JsonIgnore]
        public string ViewQuery { get; set; } = "";
        public string SelectedColumns { get; set; } = "";
        [JsonIgnore]
        public string RelationshipData { get; set; } = "";
        public bool? GetRelatedRecords { get; set; }
        public bool? OwnedByMe { get; set; }
        public bool? OwnedByBusinessUnit { get; set; }
        public bool? OwnedByTeam { get; set; }
        public int? SyncIntervalMinutes { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        [JsonIgnore]
        public string Xml => !string.IsNullOrWhiteSpace(ConfiguredXml) ? ConfiguredXml : ReferencedViewXml;
        public string SourceField => !string.IsNullOrWhiteSpace(ConfiguredXml) ? "profileitementityfilter" :
            !string.IsNullOrWhiteSpace(ReferencedViewXml) ? "profileitemrule -> savedquery.fetchxml" : "No explicit FetchXML";
        public string Distribution => DistributionCriteria == 0 ? "Related rows only" :
            DistributionCriteria == 1 ? "All records" : DistributionCriteria == 2 ? "Other data filter" :
            DistributionCriteria == 3 ? "Custom data filter" : "Unknown";
        public override string ToString() => EntityName + " | " + Distribution +
            (string.IsNullOrWhiteSpace(Xml) ? " | no explicit FetchXML" : " | " + SourceField);
    }

    internal sealed class OfflineGeneratedFilter
    {
        public Guid Id { get; set; }
        public string ReturnedTable { get; set; } = "";
        public int? Type { get; set; }
        public int? Subtype { get; set; }
        public string FetchXml { get; set; } = "";
        public string OuterFetchXml { get; set; } = "";
        public string OfflineSql { get; set; } = "";
        public override string ToString() => ReturnedTable + " | type " + Type + "/" + Subtype + " | " + Id;
    }

    internal sealed partial class DataverseReader
    {
        internal System.Threading.CancellationToken CancellationToken => token;

        public List<OfflineProfile> LoadOfflineProfiles()
        {
            var query = new QueryExpression("mobileofflineprofile")
            {
                ColumnSet = new ColumnSet("name", "ismanaged", "isvalidated")
            };
            query.AddOrder("name", OrderType.Ascending);
            query.AddOrder("mobileofflineprofileid", OrderType.Ascending);
            return ReadOfflinePages(query).Select(row => new OfflineProfile
            {
                Id = row.Id, Name = row.GetAttributeValue<string>("name") ?? row.Id.ToString(),
                IsManaged = row.GetAttributeValue<bool?>("ismanaged"),
                IsValidated = row.GetAttributeValue<bool?>("isvalidated")
            }).ToList();
        }

        public List<OfflineProfileItem> LoadOfflineItems(OfflineProfile profile)
        {
            if (profile.Id == Guid.Empty) throw new ArgumentException("Select a profile with a valid ID.", nameof(profile));
            var query = new QueryExpression("mobileofflineprofileitem")
            {
                ColumnSet = new ColumnSet("selectedentitytypecode", "recorddistributioncriteria",
                    "profileitementityfilter", "profileitemrule", "viewquery", "selectedcolumns",
                    "relationshipdata", "getrelatedentityrecords", "recordsownedbyme",
                    "recordsownedbymybusinessunit", "recordsownedbymyteam", "syncintervalinminutes")
            };
            query.Criteria.AddCondition("regardingobjectid", ConditionOperator.Equal, profile.Id);
            query.AddOrder("selectedentitytypecode", OrderType.Ascending);
            query.AddOrder("mobileofflineprofileitemid", OrderType.Ascending);
            var captured = DateTime.UtcNow;
            var items = ReadOfflinePages(query).Select(row => new OfflineProfileItem
            {
                Id = row.Id, ProfileId = profile.Id, ProfileName = profile.Name,
                EntityName = row.GetAttributeValue<string>("selectedentitytypecode") ?? "",
                DistributionCriteria = row.GetAttributeValue<OptionSetValue>("recorddistributioncriteria")?.Value,
                ConfiguredXml = row.GetAttributeValue<string>("profileitementityfilter") ?? "",
                ReferencedViewId = row.GetAttributeValue<EntityReference>("profileitemrule")?.Id,
                ViewQuery = row.GetAttributeValue<string>("viewquery") ?? "",
                SelectedColumns = row.GetAttributeValue<string>("selectedcolumns") ?? "",
                RelationshipData = row.GetAttributeValue<string>("relationshipdata") ?? "",
                GetRelatedRecords = row.GetAttributeValue<bool?>("getrelatedentityrecords"),
                OwnedByMe = row.GetAttributeValue<bool?>("recordsownedbyme"),
                OwnedByBusinessUnit = row.GetAttributeValue<bool?>("recordsownedbymybusinessunit"),
                OwnedByTeam = row.GetAttributeValue<bool?>("recordsownedbymyteam"),
                SyncIntervalMinutes = row.GetAttributeValue<int?>("syncintervalinminutes"),
                CapturedAtUtc = captured
            }).ToList();
            foreach (var item in items.Where(i => string.IsNullOrWhiteSpace(i.ConfiguredXml) && i.ReferencedViewId.HasValue))
            {
                try
                {
                    item.ReferencedViewXml = Call(() => client.Retrieve("savedquery", item.ReferencedViewId!.Value,
                        new ColumnSet("fetchxml"))).GetAttributeValue<string>("fetchxml") ?? "";
                }
                catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
                {
                    item.ReferencedViewError = "Referenced view unavailable: " + ex.Detail.Message;
                }
            }
            token.ThrowIfCancellationRequested();
            return items;
        }

        public List<OfflineGeneratedFilter> LoadOfflineGeneratedFilters(OfflineProfileItem item)
        {
            if (item.Id == Guid.Empty || item.ProfileId == Guid.Empty)
                throw new ArgumentException("Select an item with valid profile and item IDs.", nameof(item));
            var query = new QueryExpression("mobileofflineprofileitemfilter")
            {
                ColumnSet = new ColumnSet("returnedtypecode", "type", "subtype", "fetchxml", "outerfetchXml", "offlinesql")
            };
            query.Criteria.AddCondition("mobileofflineprofileid", ConditionOperator.Equal, item.ProfileId);
            query.Criteria.AddCondition("mobileofflineprofileitemid", ConditionOperator.Equal, item.Id);
            query.AddOrder("mobileofflineprofileitemfilterid", OrderType.Ascending);
            return ReadOfflinePages(query).Select(row => new OfflineGeneratedFilter
            {
                Id = row.Id, ReturnedTable = row.GetAttributeValue<string>("returnedtypecode") ?? "",
                Type = row.GetAttributeValue<OptionSetValue>("type")?.Value,
                Subtype = row.GetAttributeValue<OptionSetValue>("subtype")?.Value,
                FetchXml = row.GetAttributeValue<string>("fetchxml") ?? "",
                OuterFetchXml = row.GetAttributeValue<string>("outerfetchXml") ?? "",
                OfflineSql = row.GetAttributeValue<string>("offlinesql") ?? ""
            }).ToList();
        }

        private List<Entity> ReadOfflinePages(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = 250, PageNumber = 1 };
            var rows = new List<Entity>();
            var ids = new HashSet<Guid>();
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var page = Call(() => client.RetrieveMultiple(query));
                token.ThrowIfCancellationRequested();
                rows.AddRange(page.Entities.Where(row => ids.Add(row.Id)));
                progress.Report(rows.Count + " " + query.EntityName + " definitions read...");
                if (!page.MoreRecords) return rows;
                if (query.PageInfo.PageNumber >= 1000 || string.IsNullOrEmpty(page.PagingCookie) || !cookies.Add(page.PagingCookie))
                    throw new InvalidOperationException("Offline definition collection incomplete: missing/repeated paging cookie or safety limit reached.");
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }
}
