using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using FetchXmlLinter.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

namespace FetchXmlLinter.XrmToolBox
{
    internal sealed class UserTarget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name + " | " + Id;
    }

    internal sealed class ViewSource
    {
        public Guid ViewId { get; set; }
        public Guid OwnerId { get; set; }
        public string Name { get; set; } = "";
        public string Xml { get; set; } = "";
        public string Layout { get; set; } = "";
        public string Table { get; set; } = "";
        public DateTime? ModifiedOnUtc { get; set; }
        public Candidate? Candidate { get; set; }
        public override string ToString() => Name + " | " + Table + " | owner " + OwnerId +
            (Candidate == null ? "" : " | " + Candidate.Attribution + " | " + Candidate.MetricKind);
    }

    internal sealed class CollectionResult
    {
        public List<ViewSource> Views { get; } = new List<ViewSource>();
        public List<string> Outcomes { get; } = new List<string>();
        public bool Cancelled { get; set; }
    }

    internal interface IDataverseSession : IDisposable
    {
        IOrganizationService Service { get; }
        Guid CallerId { get; set; }
    }

    internal sealed class CrmDataverseSession : IDataverseSession
    {
        private readonly CrmServiceClient client;
        public CrmDataverseSession(CrmServiceClient client) => this.client = client;
        public IOrganizationService Service => client;
        public Guid CallerId { get => client.CallerId; set => client.CallerId = value; }
        public void Dispose() => client.Dispose();
    }

    internal sealed class DataverseReader : IDisposable
    {
        private readonly IDataverseSession session;
        private readonly IOrganizationService client;
        private readonly CancellationToken token;
        private readonly IProgress<string> progress;

        public DataverseReader(CrmServiceClient client, CancellationToken token, IProgress<string> progress)
            : this(new CrmDataverseSession(client), token, progress) { }

        public DataverseReader(IDataverseSession session, CancellationToken token, IProgress<string> progress)
        {
            this.session = session;
            client = session.Service;
            this.token = token;
            this.progress = progress;
        }

        public Guid OrganizationId() => ((WhoAmIResponse)Call(() => client.Execute(new WhoAmIRequest()))).OrganizationId;

        public List<UserTarget> FindUsers(string search)
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname"),
                TopCount = 200
            };
            query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            query.Criteria.AddCondition("accessmode", ConditionOperator.Equal, 0);
            if (Guid.TryParse(search, out var id))
                query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, id);
            else
                query.Criteria.AddCondition("fullname", ConditionOperator.BeginsWith, search);
            query.AddOrder("fullname", OrderType.Ascending);
            query.AddOrder("systemuserid", OrderType.Ascending);
            return Call(() => client.RetrieveMultiple(query)).Entities
                .Select(e => new UserTarget { Id = e.Id, Name = e.GetAttributeValue<string>("fullname") ?? e.Id.ToString() }).ToList();
        }

        public UserTarget ResolveActor(Candidate candidate)
        {
            if (candidate.UserIdType == "DataverseSystemUserId")
            {
                var user = Call(() => client.Retrieve("systemuser", candidate.UserId, new ColumnSet("fullname")));
                return new UserTarget { Id = user.Id, Name = user.GetAttributeValue<string>("fullname") ?? user.Id.ToString() };
            }
            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("fullname"), TopCount = 2 };
            query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, candidate.UserId);
            var users = Call(() => client.RetrieveMultiple(query)).Entities;
            if (users.Count != 1)
                throw new InvalidOperationException("The Entra identity did not resolve to exactly one Dataverse user.");
            return new UserTarget { Id = users[0].Id, Name = users[0].GetAttributeValue<string>("fullname") ?? users[0].Id.ToString() };
        }

        public CollectionResult Collect(IEnumerable<UserTarget> users, IReadOnlyList<Candidate>? candidates = null)
        {
            var result = new CollectionResult();
            var originalCaller = session.CallerId;
            try
            {
                foreach (var user in users.GroupBy(u => u.Id).Select(g => g.First()))
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report("Collecting owned views for " + user.Name);
                    session.CallerId = user.Id;
                    try
                    {
                        VerifyUser(user.Id);
                        var query = new QueryExpression("userquery")
                        {
                            ColumnSet = new ColumnSet("name", "ownerid", "fetchxml", "layoutxml", "modifiedon"),
                            PageInfo = new PagingInfo { Count = 250, PageNumber = 1 }
                        };
                        query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, user.Id);
                        query.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
                        query.AddOrder("userqueryid", OrderType.Ascending);
                        var owned = new List<ViewSource>();
                        var cookies = new HashSet<string>(StringComparer.Ordinal);
                        while (true)
                        {
                            var page = Call(() => client.RetrieveMultiple(query));
                            owned.AddRange(page.Entities.Select(e => new ViewSource
                            {
                                ViewId = e.Id, OwnerId = user.Id,
                                Name = e.GetAttributeValue<string>("name") ?? e.Id.ToString(),
                                Xml = e.GetAttributeValue<string>("fetchxml") ?? "",
                                Table = new Linter().Analyze(e.GetAttributeValue<string>("fetchxml") ?? "").Table,
                                Layout = e.GetAttributeValue<string>("layoutxml") ?? "",
                                ModifiedOnUtc = e.Contains("modifiedon") ? e.GetAttributeValue<DateTime>("modifiedon").ToUniversalTime() : (DateTime?)null
                            }));
                            if (!page.MoreRecords) break;
                            if (query.PageInfo.PageNumber >= 1000 || string.IsNullOrEmpty(page.PagingCookie) || !cookies.Add(page.PagingCookie))
                                throw new InvalidOperationException("Invalid/repeated paging cookie or collection page limit exceeded.");
                            query.PageInfo.PageNumber++;
                            query.PageInfo.PagingCookie = page.PagingCookie;
                        }
                        if (candidates == null)
                            result.Views.AddRange(owned);
                        else
                        {
                            foreach (var candidate in candidates.Where(c =>
                                (c.ResolvedDataverseUserId ?? (c.UserIdType == "DataverseSystemUserId" ? c.UserId : Guid.Empty)) == user.Id))
                            {
                                var matches = owned.Where(v =>
                                    (!candidate.ViewId.HasValue || v.ViewId == candidate.ViewId) &&
                                    (candidate.EntityName.Length == 0 || string.Equals(candidate.EntityName, v.Table, StringComparison.OrdinalIgnoreCase))).ToList();
                                if (candidate.ViewId.HasValue && matches.Count == 0)
                                    result.Outcomes.Add("Unresolved exact marker " + candidate.ViewId +
                                        ": not an owned ordinary view matching this actor/table. It may be shared, system, deleted, changed or inaccessible; no fallback was applied.");
                                else if (matches.Count == 0)
                                    result.Outcomes.Add(user.Id + ": no owned ordinary definitions matched candidate table '" +
                                        candidate.EntityName + "'. No broader selection was applied.");
                                result.Views.AddRange(matches.Select(v => new ViewSource
                                {
                                    ViewId = v.ViewId, OwnerId = v.OwnerId, Name = v.Name,
                                    Xml = v.Xml, Layout = v.Layout, Table = v.Table, ModifiedOnUtc = v.ModifiedOnUtc, Candidate = candidate
                                }));
                            }
                        }
                        result.Outcomes.Add(user.Id + ": completed; " + owned.Count + " owned ordinary views; selection still required.");
                    }
                    catch (FaultException<OrganizationServiceFault> ex)
                    {
                        result.Outcomes.Add(user.Id + ": FAILED (not zero views); " + ex.Detail.ErrorCode.ToString(CultureInfo.InvariantCulture) + ": " + ex.Detail.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        result.Outcomes.Add(user.Id + ": FAILED (not zero views); " + ex.Message);
                    }
                    finally { session.CallerId = originalCaller; }
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                result.Outcomes.Add("Cancelled. Only fully collected users are included; remaining scope was not evaluated.");
            }
            return result;
        }

        public DictionaryMetadata GetMetadata(IEnumerable<string> tables, IList<string> errors)
        {
            var metadata = new DictionaryMetadata();
            foreach (var table in tables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                progress.Report("Reading column metadata for " + table);
                try
                {
                    var response = (RetrieveEntityResponse)Call(() => client.Execute(new RetrieveEntityRequest
                    {
                        LogicalName = table, EntityFilters = EntityFilters.Attributes, RetrieveAsIfPublished = false
                    }));
                    foreach (var attribute in response.EntityMetadata.Attributes)
                    {
                        var kind = attribute is MemoAttributeMetadata ? ColumnKind.Memo :
                            attribute is StringAttributeMetadata ? ColumnKind.String :
                            attribute is LookupAttributeMetadata ? ColumnKind.Lookup :
                            attribute is EnumAttributeMetadata ? ColumnKind.Choice : ColumnKind.Other;
                        metadata.Add(table, attribute.LogicalName, new ColumnMetadata
                        {
                            Kind = kind, MaxLength = (attribute as StringAttributeMetadata)?.MaxLength,
                            IsLogical = attribute.IsLogical,
                            IsCalculated = attribute.SourceType == 1 || attribute.SourceType == 3 ? true :
                                attribute.SourceType == null || attribute.SourceType == 0 || attribute.SourceType == 2 ? false : (bool?)null
                        });
                    }
                }
                catch (FaultException<OrganizationServiceFault> ex)
                {
                    errors.Add("Metadata unavailable for " + table + ": " + ex.Detail.Message);
                }
            }
            return metadata;
        }

        private void VerifyUser(Guid expected)
        {
            const string xml = "<fetch><entity name='systemuser'><attribute name='systemuserid'/><filter>" +
                "<condition attribute='systemuserid' operator='eq-userid'/></filter></entity></fetch>";
            var users = Call(() => client.RetrieveMultiple(new FetchExpression(xml))).Entities;
            if (users.Count != 1 || users[0].Id != expected)
                throw new InvalidOperationException("Effective-user verification failed. No owned-view query was attempted.");
        }

        private T Call<T>(Func<T> operation)
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return operation(); }
                catch (FaultException<OrganizationServiceFault> ex) when (attempt < 2 && IsThrottle(ex.Detail.ErrorCode))
                {
                    var delay = Math.Pow(2, attempt + 1);
                    if (ex.Detail.ErrorDetails.TryGetValue("Retry-After", out var raw))
                    {
                        if (raw is TimeSpan interval) delay = Math.Max(delay, interval.TotalSeconds);
                        else if (double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) delay = Math.Max(delay, seconds);
                        else throw new InvalidOperationException("Unrecognized Retry-After value; request was not retried.", ex);
                    }
                    if (double.IsNaN(delay) || double.IsInfinity(delay) || delay > 120)
                        throw new InvalidOperationException("Retry-After is invalid or exceeds the 120-second retry budget.", ex);
                    progress.Report("Dataverse throttled the read; waiting before retry.");
                    if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay))) token.ThrowIfCancellationRequested();
                }
            }
        }

        private static bool IsThrottle(int code) => code == unchecked((int)0x80072321) ||
            code == unchecked((int)0x80072322) || code == unchecked((int)0x80072326);

        public void Dispose() => session.Dispose();
    }
}
