using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using FetchXmlLinter.XrmToolBox;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;
using Xunit;

namespace FetchXmlLinter.Host.Tests
{
    public sealed class ConnectionCallbackTests
    {
        [Fact]
        public void DisconnectedClosureWaitsForConnectionThenReplaysOnce()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    RequestConnectionEventArgs? request = null;
                    control.OnRequestConnection += (_, args) => request = Assert.IsType<RequestConnectionEventArgs>(args);
                    var calls = 0;
                    control.ExecuteWithConnection(() => calls++);
                    Assert.Equal(0, calls);
                    Assert.NotNull(request);
                    Assert.Same(control, request!.Control);
                    Assert.Equal("Recaller", request.ActionName);
                    Assert.IsType<ExternalMethodCallerInfo>(request.Parameter);

                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), request.ActionName, request.Parameter);
                    Assert.Equal(1, calls);
                }
            });
        }

        [Fact]
        public void ConnectedClosureRunsWithoutRequestingAnotherConnection()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), string.Empty, null!);
                    var requests = 0;
                    control.OnRequestConnection += (_, __) => requests++;
                    var calls = 0;
                    control.ExecuteWithConnection(() => calls++);
                    Assert.Equal(1, calls);
                    Assert.Equal(0, requests);
                }
            });
        }

        private static void RunSta(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private sealed class NoRequestsService : IOrganizationService
        {
            public Guid Create(Entity entity) => throw new NotSupportedException();
            public void Update(Entity entity) => throw new NotSupportedException();
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
            public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
            public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}
