using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using FetchXmlLinter.Core;
using FetchXmlLinter.XrmToolBox;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using Xunit;

namespace FetchXmlLinter.Host.Tests
{
    public class CollectionTests
    {
        private static DataverseReader Reader(FakeSession session, CancellationToken token = default) =>
            new DataverseReader(session, token, new ImmediateProgress());

        [Fact]
        public void OfflineProvenanceExportOmitsQueryBodiesAndRawRelationshipConfiguration()
        {
            var item = new OfflineProfileItem
            {
                Id = Guid.NewGuid(), ProfileId = Guid.NewGuid(), EntityName = "account",
                ConfiguredXml = "<fetch><entity name='account'/></fetch>", ReferencedViewXml = "referenced query body",
                ViewQuery = "select accountid from account", RelationshipData = "private relationship blob",
                SelectedColumns = "[\"name\"]"
            };
            var report = JsonConvert.SerializeObject(item);
            Assert.Contains(item.Id.ToString(), report);
            Assert.Contains("profileitementityfilter", report);
            Assert.Contains("SelectedColumns", report);
            Assert.DoesNotContain("<fetch", report);
            Assert.DoesNotContain("referenced query body", report);
            Assert.DoesNotContain("select accountid", report);
            Assert.DoesNotContain("private relationship blob", report);
        }

        [Fact]
        public void PagesOfflineProfilesAndItemsWithoutImpersonationOrDefinitionExecution()
        {
            var profileId = Guid.NewGuid();
            var reads = 0;
            var session = new FakeSession();
            var caller = session.CallerId;
            session.ReadDefinitions = query =>
            {
                Assert.Null(query.TopCount);
                Assert.False(query.ColumnSet.AllColumns);
                Assert.Equal(250, query.PageInfo.Count);
                Assert.Equal(caller, session.CallerId);
                reads++;
                if (query.EntityName == "mobileofflineprofile")
                {
                    if (query.PageInfo.PageNumber == 1)
                        return Page(new Entity(query.EntityName, profileId) { ["name"] = "Synthetic profile", ["isvalidated"] = true }, true, "next");
                    Assert.Equal(2, query.PageInfo.PageNumber);
                    Assert.Equal("next", query.PageInfo.PagingCookie);
                    return Page(new Entity(query.EntityName, Guid.NewGuid()) { ["name"] = "Second profile" });
                }
                Assert.Equal("mobileofflineprofileitem", query.EntityName);
                Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "regardingobjectid" && Equals(c.Values.Single(), profileId));
                return Page(new Entity(query.EntityName, Guid.NewGuid())
                {
                    ["selectedentitytypecode"] = "account", ["recorddistributioncriteria"] = new OptionSetValue(3),
                    ["profileitementityfilter"] = "<fetch><entity name='account'><all-attributes/></entity></fetch>",
                    ["selectedcolumns"] = "[\"name\"]", ["recordsownedbyme"] = true
                });
            };
            using (var reader = Reader(session))
            {
                var profiles = reader.LoadOfflineProfiles();
                Assert.Equal(2, profiles.Count);
                var item = Assert.Single(reader.LoadOfflineItems(profiles[0]));
                Assert.Equal(profileId, item.ProfileId);
                Assert.True(item.OwnedByMe);
                Assert.Equal("[\"name\"]", item.SelectedColumns);
                Assert.Equal("profileitementityfilter", item.SourceField);
                Assert.Equal(3, reads);
                Assert.Empty(session.FetchReads);
                Assert.Equal(caller, session.CallerId);
            }
        }

        [Fact]
        public void PagesGeneratedDefinitionsScopedToBothProfileAndItemAndPreservesPlaceholdersAndSql()
        {
            var item = new OfflineProfileItem { Id = Guid.NewGuid(), ProfileId = Guid.NewGuid() };
            var session = new FakeSession();
            var reads = 0;
            session.ReadDefinitions = query =>
            {
                Assert.Equal("mobileofflineprofileitemfilter", query.EntityName);
                Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "mobileofflineprofileid" && Equals(c.Values.Single(), item.ProfileId));
                Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "mobileofflineprofileitemid" && Equals(c.Values.Single(), item.Id));
                Assert.Contains("outerfetchXml", query.ColumnSet.Columns);
                reads++;
                if (reads == 2) Assert.Equal("generated-next", query.PageInfo.PagingCookie);
                return Page(new Entity(query.EntityName, Guid.NewGuid())
                {
                    ["returnedtypecode"] = "account", ["type"] = new OptionSetValue(2), ["subtype"] = new OptionSetValue(5),
                    ["fetchxml"] = "[\"\"]", ["outerfetchXml"] = "<fetch><entity name='account'/></fetch>",
                    ["offlinesql"] = "/*SelectStart*/ select [accountid] from account"
                }, reads == 1, reads == 1 ? "generated-next" : null);
            };
            using (var reader = Reader(session))
            {
                var filters = reader.LoadOfflineGeneratedFilters(item);
                Assert.Equal(2, filters.Count);
                Assert.All(filters, f =>
                {
                    Assert.Equal("[\"\"]", f.FetchXml);
                    Assert.StartsWith("/*SelectStart*/", f.OfflineSql);
                    Assert.StartsWith("<fetch>", f.OuterFetchXml);
                    Assert.Equal(2, f.Type);
                    Assert.Equal(5, f.Subtype);
                });
                Assert.Empty(session.FetchReads);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OfflinePagingCancellationOrRepeatedCookieNeverReturnsACompletePartialList(bool cancel)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var session = new FakeSession();
                session.ReadDefinitions = query =>
                {
                    if (cancel) cancellation.Cancel();
                    return Page(new Entity(query.EntityName, Guid.NewGuid()) { ["name"] = "Synthetic" }, true, "repeat");
                };
                using (var reader = Reader(session, cancellation.Token))
                {
                    if (cancel) Assert.Throws<OperationCanceledException>(() => reader.LoadOfflineProfiles());
                    else Assert.Throws<InvalidOperationException>(() => reader.LoadOfflineProfiles());
                }
            }
        }

        [Fact]
        public void MissingConfiguredFilterResolvesReferencedViewButNeverUsesViewQueryAsXml()
        {
            var viewId = Guid.NewGuid();
            var session = new FakeSession
            {
                ReadDefinitions = query =>
                {
                    var page = Page(new Entity(query.EntityName, Guid.NewGuid())
                    {
                        ["selectedentitytypecode"] = "account", ["profileitemrule"] = new EntityReference("savedquery", viewId),
                        ["viewquery"] = "select accountid from account"
                    });
                    page.Entities.Add(new Entity(query.EntityName, Guid.NewGuid())
                    {
                        ["selectedentitytypecode"] = "contact", ["recorddistributioncriteria"] = new OptionSetValue(0),
                        ["viewquery"] = "select contactid from contact"
                    });
                    return page;
                },
                ReadRecord = (table, id, columns) =>
                {
                    Assert.Equal("savedquery", table);
                    Assert.Equal(viewId, id);
                    Assert.Equal(new[] { "fetchxml" }, columns.Columns);
                    return new Entity(table, id) { ["fetchxml"] = "<fetch><entity name='account'/></fetch>" };
                }
            };
            using (var reader = Reader(session))
            {
                var items = reader.LoadOfflineItems(new OfflineProfile { Id = Guid.NewGuid() });
                Assert.StartsWith("<fetch>", items[0].Xml);
                Assert.Contains("savedquery", items[0].SourceField);
                Assert.Empty(items[1].Xml);
                Assert.Equal("Related rows only", items[1].Distribution);
            }
        }

        [Fact]
        public void OfflineReferenceAndGeneratedPermissionFailuresRemainExplicit()
        {
            var session = new FakeSession
            {
                ReadDefinitions = query => Page(new Entity(query.EntityName, Guid.NewGuid())
                {
                    ["profileitemrule"] = new EntityReference("savedquery", Guid.NewGuid())
                }),
                ReadRecord = (_, __, ___) => throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { Message = "View read denied" })
            };
            using (var reader = Reader(session))
            {
                var item = Assert.Single(reader.LoadOfflineItems(new OfflineProfile { Id = Guid.NewGuid() }));
                Assert.Empty(item.Xml);
                Assert.Contains("View read denied", item.ReferencedViewError);
                session.ReadDefinitions = _ => throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { Message = "Generated read denied" });
                Assert.Throws<FaultException<OrganizationServiceFault>>(() => reader.LoadOfflineGeneratedFilters(item));
            }
        }

        [Fact]
        public void LoadsMoreThanTenThousandUsersWithMinimalColumnsAndPaging()
        {
            var session = new FakeSession();
            var pageNumber = 0;
            session.ReadUsers = query =>
            {
                Assert.Null(query.TopCount);
                Assert.Equal(new[] { "fullname", "internalemailaddress" }, query.ColumnSet.Columns);
                Assert.Equal(1000, query.PageInfo.Count);
                Assert.Equal(++pageNumber, query.PageInfo.PageNumber);
                Assert.Equal(pageNumber == 1 ? null : "users-" + (pageNumber - 1), query.PageInfo.PagingCookie);
                Assert.Equal(2, query.Criteria.Conditions.Count);
                Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "isdisabled" && Equals(c.Values.Single(), false));
                Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "accessmode" && Equals(c.Values.Single(), 0));
                Assert.Equal(new[] { "fullname", "systemuserid" }, query.Orders.Select(o => o.AttributeName));
                var page = new EntityCollection { MoreRecords = pageNumber < 11, PagingCookie = "users-" + pageNumber };
                for (var i = 0; i < (pageNumber == 11 ? 1 : 1000); i++)
                    page.Entities.Add(new Entity("systemuser", Guid.NewGuid())
                    {
                        ["fullname"] = "Synthetic user", ["internalemailaddress"] = "synthetic@example.invalid"
                    });
                return page;
            };
            using (var reader = Reader(session))
            {
                var result = reader.LoadUsers();
                Assert.True(result.Complete);
                Assert.Equal(10001, result.Users.Count);
                Assert.All(result.Users, u => Assert.Equal("synthetic@example.invalid", u.Email));
                Assert.Equal(0, session.ViewReads);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UserCancellationOrPagingFailureRetainsExplicitlyIncompleteDirectory(bool cancel)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var session = new FakeSession();
                var reads = 0;
                var id = Guid.NewGuid();
                session.ReadUsers = _ =>
                {
                    reads++;
                    if (cancel) cancellation.Cancel();
                    return Page(new Entity("systemuser", id) { ["fullname"] = "Synthetic user" }, true, "repeat");
                };
                using (var reader = Reader(session, cancellation.Token))
                {
                    var result = reader.LoadUsers();
                    Assert.False(result.Complete);
                    Assert.Single(result.Users);
                    Assert.Contains("incomplete", result.Outcome);
                    Assert.Contains(cancel ? "Cancelled" : "FAILED", result.Outcome);
                    Assert.Equal(cancel ? 1 : 2, reads);
                }
            }
        }

        [Fact]
        public void UserPermissionFailureIsNotAnEmptySuccessfulDirectory()
        {
            var session = new FakeSession();
            session.ReadUsers = _ => throw new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = "Directory access denied" });
            using (var reader = Reader(session))
            {
                var result = reader.LoadUsers();
                Assert.False(result.Complete);
                Assert.Empty(result.Users);
                Assert.Contains("FAILED", result.Outcome);
                Assert.Contains("Directory access denied", result.Outcome);
            }
        }

        [Fact]
        public void MismatchedEffectiveUserPreventsViewReadAndRestoresCaller()
        {
            var session = new FakeSession { VerifyAs = Guid.NewGuid() };
            var original = session.CallerId;
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { Target() });
                Assert.Empty(result.Views);
                Assert.Contains(result.Outcomes, s => s.Contains("FAILED"));
                Assert.Equal(0, session.ViewReads);
                Assert.Equal(original, session.CallerId);
            }
            Assert.True(session.Disposed);
        }

        [Fact]
        public void PermissionFailureIsNotReportedAsSuccessfulZeroViews()
        {
            var session = new FakeSession { VerificationFault = true };
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { Target() });
                Assert.Contains(result.Outcomes, s => s.Contains("FAILED") && s.Contains("Denied"));
                Assert.DoesNotContain(result.Outcomes, s => s.Contains("completed;"));
                Assert.Equal(0, session.ViewReads);
            }
        }

        [Fact]
        public void PagesDefinitionsAndDoesNotExecuteTheirFetchXml()
        {
            var session = new FakeSession();
            session.Pages.Enqueue(Page(View("one"), true, "cookie-1"));
            session.Pages.Enqueue(Page(View("two")));
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { Target() });
                Assert.Equal(2, result.Views.Count);
                Assert.Equal(new[] { 1, 2 }, session.PageNumbers);
                Assert.Equal(new[] { null, "cookie-1" }, session.Cookies);
                Assert.All(session.FetchReads, s => Assert.Contains("eq-userid", s));
                Assert.Contains(result.Outcomes, s => s.Contains("completed; 2"));
            }
        }

        [Fact]
        public void MultipleUsersCollectTheirOwnedViewsWithReadableOwnerNames()
        {
            var first = new UserTarget { Id = Guid.NewGuid(), Name = "Synthetic owner A" };
            var second = new UserTarget { Id = Guid.NewGuid(), Name = "Synthetic owner B" };
            var session = new FakeSession();
            var original = session.CallerId;
            session.Pages.Enqueue(Page(View("Same view name")));
            session.Pages.Enqueue(Page(View("Same view name")));
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { first, second, first });
                Assert.Equal(2, session.ViewReads);
                Assert.Equal(new[] { first.Id, second.Id }, result.Views.Select(v => v.OwnerId));
                Assert.Equal(new[] { first.Name, second.Name }, result.Views.Select(v => v.OwnerName));
                Assert.Contains("owner " + first.Name, result.Views[0].ToString());
                Assert.Contains("owner " + second.Name, result.Views[1].ToString());
                Assert.All(result.Views, view => Assert.DoesNotContain(view.OwnerId.ToString(), view.ToString()));
                Assert.Equal(2, session.FetchReads.Count);
                Assert.Equal(2, result.Outcomes.Count(s => s.Contains("completed;")));
                Assert.Equal(original, session.CallerId);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public void MissingOwnerNameRetainsGuidInViewDisplay(string name)
        {
            var user = new UserTarget { Id = Guid.NewGuid(), Name = name };
            var session = new FakeSession();
            session.Pages.Enqueue(Page(View("Synthetic view")));
            using (var reader = Reader(session))
            {
                var found = Assert.Single(reader.Collect(new[] { user }).Views);
                Assert.Contains("owner " + user.Id, found.ToString());
                Assert.Equal("Synthetic view | " + user.Id + " | 0 findings",
                    new AnalysisEntry { SourceName = found.Name, Source = found }.ToString());
            }
        }

        [Fact]
        public void RepeatedPagingCookieRejectsPartialUser()
        {
            var session = new FakeSession();
            session.Pages.Enqueue(Page(View("one"), true, "repeat"));
            session.Pages.Enqueue(Page(View("two"), true, "repeat"));
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { Target() });
                Assert.Empty(result.Views);
                Assert.Contains(result.Outcomes, s => s.Contains("FAILED") && s.Contains("paging"));
            }
        }

        [Fact]
        public void CancellationRetainsOnlyCompletedUsersAndRestoresCaller()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var session = new FakeSession();
                var original = session.CallerId;
                session.AfterViewRead = () => cancellation.Cancel();
                session.Pages.Enqueue(Page(View("complete")));
                using (var reader = Reader(session, cancellation.Token))
                {
                    var result = reader.Collect(new[] { Target(), Target() });
                    Assert.True(result.Cancelled);
                    Assert.Single(result.Views);
                    Assert.Equal(original, session.CallerId);
                }
            }
        }

        [Fact]
        public void ExactMarkerDoesNotFallBackToOtherOwnedViews()
        {
            var user = Target();
            var session = new FakeSession();
            session.Pages.Enqueue(Page(View("unrelated")));
            var candidate = Candidate(user, Guid.NewGuid());
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { user }, new[] { candidate });
                Assert.Empty(result.Views);
                Assert.Contains(result.Outcomes, s => s.Contains("no fallback"));
            }
        }

        [Fact]
        public void TableCandidatePreservesOriginalEntraIdentityAndFiltersOtherTables()
        {
            var user = Target();
            var session = new FakeSession();
            var entraId = Guid.NewGuid();
            var candidate = new Candidate
            {
                UserId = entraId, UserIdType = "EntraObjectId", ResolvedDataverseUserId = user.Id,
                Attribution = "UserTableCandidate", EntityName = "account"
            };
            var differentTable = View("contact");
            differentTable["fetchxml"] = "<fetch><entity name='contact'><attribute name='fullname'/></entity></fetch>";
            var page = Page(View("account"));
            page.Entities.Add(differentTable);
            session.Pages.Enqueue(page);
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { user }, new[] { candidate });
                var found = Assert.Single(result.Views);
                Assert.Equal("account", found.Table);
                Assert.Same(candidate, found.Candidate);
                Assert.Equal(entraId, found.Candidate!.UserId);
                Assert.Equal("EntraObjectId", found.Candidate.UserIdType);
                Assert.Equal(user.Name, found.OwnerName);
                Assert.Contains("owner " + user.Name, found.ToString());
                Assert.DoesNotContain(user.Id.ToString(), found.ToString());
            }
        }

        [Fact]
        public void EmptySuccessfulCollectionIsExplicit()
        {
            var session = new FakeSession();
            session.Pages.Enqueue(new EntityCollection());
            using (var reader = Reader(session))
            {
                var result = reader.Collect(new[] { Target() });
                Assert.Empty(result.Views);
                Assert.Contains(result.Outcomes, s => s.Contains("completed; 0"));
                Assert.False(result.Cancelled);
            }
        }

        [Fact]
        public void MidUserCancellationDiscardsThatUsersPartialPages()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var session = new FakeSession();
                var original = session.CallerId;
                session.AfterViewRead = () => cancellation.Cancel();
                session.Pages.Enqueue(Page(View("partial"), true, "next"));
                using (var reader = Reader(session, cancellation.Token))
                {
                    var result = reader.Collect(new[] { Target() });
                    Assert.True(result.Cancelled);
                    Assert.Empty(result.Views);
                    Assert.Equal(original, session.CallerId);
                    Assert.DoesNotContain(result.Outcomes, s => s.Contains("completed;"));
                }
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, null)]
        [InlineData(99, null)]
        public void MapsDocumentedSourceTypeAndLeavesUnknownTypesUnevaluated(int? sourceType, bool? calculated)
        {
            var session = new FakeSession();
            session.ExecuteRequest = request =>
            {
                var metadataRequest = Assert.IsType<RetrieveEntityRequest>(request);
                Assert.False(metadataRequest.RetrieveAsIfPublished);
                var entity = new EntityMetadata();
                // SDK response metadata exposes Attributes through a non-public setter.
                typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(entity,
                    new AttributeMetadata[]
                    {
                        new StringAttributeMetadata { LogicalName = "name", SourceType = sourceType, MaxLength = 160 }
                    });
                return new RetrieveEntityResponse
                {
                    Results = new ParameterCollection
                    {
                        ["EntityMetadata"] = entity
                    }
                };
            };
            using (var reader = Reader(session))
            {
                var errors = new List<string>();
                var metadata = reader.GetMetadata(new[] { "account" }, errors);
                Assert.Empty(errors);
                Assert.Equal(calculated, metadata.FindColumn("account", "name")!.IsCalculated);
            }
        }

        [Fact]
        public void NormalizesCompatibleJoinTypesWithoutTreatingMultiChoiceAsInteger()
        {
            var primary = new UniqueIdentifierAttributeMetadata { LogicalName = "accountid" };
            typeof(AttributeMetadata).GetProperty(nameof(AttributeMetadata.IsPrimaryId))!.SetValue(primary, true);
            var entity = new EntityMetadata();
            typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(entity,
                new AttributeMetadata[]
                {
                    primary,
                    new LookupAttributeMetadata { LogicalName = "parentid", Targets = new[] { "account" } },
                    new PicklistAttributeMetadata { LogicalName = "choice" },
                    new IntegerAttributeMetadata { LogicalName = "number" },
                    new MultiSelectPicklistAttributeMetadata { LogicalName = "choices" }
                });
            var session = new FakeSession
            {
                ExecuteRequest = _ => new RetrieveEntityResponse
                {
                    Results = new ParameterCollection { ["EntityMetadata"] = entity }
                }
            };
            using (var reader = Reader(session))
            {
                var errors = new List<string>();
                var metadata = reader.GetMetadata(new[] { "account" }, errors);
                Assert.Empty(errors);
                Assert.Equal(metadata.FindColumn("account", "accountid")!.StorageType, metadata.FindColumn("account", "parentid")!.StorageType);
                Assert.True(metadata.FindColumn("account", "accountid")!.IsPrimaryId);
                Assert.Equal(new[] { "account" }, metadata.FindColumn("account", "parentid")!.LookupTargets);
                Assert.Equal(metadata.FindColumn("account", "number")!.StorageType, metadata.FindColumn("account", "choice")!.StorageType);
                Assert.NotEqual(metadata.FindColumn("account", "number")!.StorageType, metadata.FindColumn("account", "choices")!.StorageType);
            }
        }

        private static Candidate Candidate(UserTarget user, Guid view) => new Candidate
        {
            UserId = user.Id, UserIdType = "DataverseSystemUserId", ViewId = view,
            Attribution = "ExactViewMarker"
        };

        private static UserTarget Target() => new UserTarget { Id = Guid.NewGuid(), Name = "Synthetic user" };

        private static Entity View(string name) => new Entity("userquery", Guid.NewGuid())
        {
            ["name"] = name,
            ["fetchxml"] = "<fetch><entity name='account'><attribute name='name'/></entity></fetch>"
        };

        private static EntityCollection Page(Entity view, bool more = false, string? cookie = null)
        {
            var page = new EntityCollection { MoreRecords = more, PagingCookie = cookie };
            page.Entities.Add(view);
            return page;
        }

        private sealed class ImmediateProgress : IProgress<string>
        {
            public void Report(string value) { }
        }

        private sealed class FakeSession : IDataverseSession, IOrganizationService
        {
            public Guid CallerId { get; set; } = Guid.NewGuid();
            public IOrganizationService Service => this;
            public Guid? VerifyAs { get; set; }
            public bool VerificationFault { get; set; }
            public bool Disposed { get; private set; }
            public int ViewReads { get; private set; }
            public Queue<EntityCollection> Pages { get; } = new Queue<EntityCollection>();
            public List<int> PageNumbers { get; } = new List<int>();
            public List<string?> Cookies { get; } = new List<string?>();
            public List<string> FetchReads { get; } = new List<string>();
            public Action? AfterViewRead { get; set; }
            public Func<OrganizationRequest, OrganizationResponse>? ExecuteRequest { get; set; }
            public Func<QueryExpression, EntityCollection>? ReadUsers { get; set; }
            public Func<QueryExpression, EntityCollection>? ReadDefinitions { get; set; }
            public Func<string, Guid, ColumnSet, Entity>? ReadRecord { get; set; }

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                if (query is FetchExpression fetch)
                {
                    FetchReads.Add(fetch.Query);
                    if (VerificationFault)
                        throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { Message = "Denied" });
                    var users = new EntityCollection();
                    users.Entities.Add(new Entity("systemuser", VerifyAs ?? CallerId));
                    return users;
                }
                var views = Assert.IsType<QueryExpression>(query);
                if (views.EntityName.StartsWith("mobileofflineprofile", StringComparison.Ordinal))
                    return (ReadDefinitions ?? throw new InvalidOperationException("Unexpected offline definition read."))(views);
                if (views.EntityName == "systemuser")
                    return (ReadUsers ?? throw new InvalidOperationException("Unexpected user directory read."))(views);
                Assert.Equal("userquery", views.EntityName);
                Assert.Contains(views.Criteria.Conditions, c => c.AttributeName == "ownerid" && Equals(c.Values.Single(), CallerId));
                Assert.Contains(views.Criteria.Conditions, c => c.AttributeName == "querytype" && Equals(c.Values.Single(), 0));
                ViewReads++;
                PageNumbers.Add(views.PageInfo.PageNumber);
                Cookies.Add(views.PageInfo.PagingCookie);
                AfterViewRead?.Invoke();
                return Pages.Dequeue();
            }

            public void Dispose() => Disposed = true;
            public Guid Create(Entity entity) => throw new NotSupportedException("Writes must never be called.");
            public void Update(Entity entity) => throw new NotSupportedException("Writes must never be called.");
            public void Delete(string entityName, Guid id) => throw new NotSupportedException("Writes must never be called.");
            public Entity Retrieve(string entityName, Guid id, ColumnSet columns) =>
                ReadRecord == null ? throw new NotSupportedException() : ReadRecord(entityName, id, columns);
            public OrganizationResponse Execute(OrganizationRequest request) =>
                ExecuteRequest == null ? throw new NotSupportedException() : ExecuteRequest(request);
            public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection related) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection related) => throw new NotSupportedException();
        }
    }
}
