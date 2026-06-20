// <copyright file="PhantomDeleteReconciliationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence.EntityFramework;

/// <summary>
/// Integration tests for the phantom-delete reconciliation in the entity framework save path.
/// </summary>
/// <remarks>
/// These tests require a running PostgreSQL database with an initialized schema (e.g. the
/// <c>openmu-postgres</c> docker container) and are therefore marked <see cref="ExplicitAttribute"/>.
/// Run them on demand with <c>dotnet test --filter FullyQualifiedName~PhantomDelete</c>.
/// </remarks>
[TestFixture]
internal class PhantomDeleteReconciliationTests
{
    /// <summary>
    /// Verifies that a "phantom" delete - a DELETE of a row which never existed in the database -
    /// does not abort the whole save batch, so unrelated changes in the same batch are still persisted.
    /// <para>
    /// This reproduces the logout data-loss bug: an entity created via <c>CreateNew</c> (which gets a
    /// real GUID immediately) but never persisted gets demoted from <c>Added</c> to <c>Unchanged</c> by a
    /// detach/attach cycle (item drop/pickup, trade, player store, temporary-storage restore). A later
    /// removal then makes EF emit a DELETE which affects 0 rows, throwing
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> and rolling back everything.
    /// With no optimistic concurrency token configured, a 0-row DELETE just means the row is already gone,
    /// so the reconciliation detaches it and retries instead of losing the whole save.
    /// </para>
    /// </summary>
    /// <returns>The task.</returns>
    [Test]
    [Explicit("Integration test - requires a running PostgreSQL database (e.g. the openmu-postgres docker container).")]
    public async Task PhantomDeleteDoesNotAbortTheWholeSaveAsync()
    {
        var contextProvider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        var siblingName = "s" + Guid.NewGuid().ToString("N")[..9];
        try
        {
            using var context = contextProvider.CreateNewContext();

            // Arrange the phantom: created, never persisted, then detached + re-attached => EF tracks it as Unchanged.
            var phantom = context.CreateNew<Account>();
            phantom.LoginName = "p" + Guid.NewGuid().ToString("N")[..9];
            context.Detach(phantom);
            context.Attach(phantom);

            // => Deleted: EF believes the row exists and will emit a DELETE which affects 0 rows.
            await context.DeleteAsync(phantom).ConfigureAwait(false);

            // A sibling change in the same batch which must survive the phantom delete.
            var sibling = context.CreateNew<Account>();
            sibling.LoginName = siblingName;

            // Act: without the fix this throws DbUpdateConcurrencyException and rolls everything back.
            var success = await context.SaveChangesAsync().ConfigureAwait(false);

            // Assert: the save reported success and the sibling actually reached the database.
            Assert.That(success, Is.True);

            using var verifyContext = contextProvider.CreateNewContext();
            var accounts = await verifyContext.GetAsync<Account>().ConfigureAwait(false);
            Assert.That(
                accounts.Any(a => a.LoginName == siblingName),
                Is.True,
                "The sibling change must be persisted despite the phantom delete.");
        }
        finally
        {
            using var cleanupContext = contextProvider.CreateNewContext();
            var toDelete = (await cleanupContext.GetAsync<Account>().ConfigureAwait(false))
                .FirstOrDefault(a => a.LoginName == siblingName);
            if (toDelete is not null)
            {
                await cleanupContext.DeleteAsync(toDelete).ConfigureAwait(false);
                await cleanupContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Diagnostic: verifies whether an entity that was created (Added) but never saved, and then went
    /// through a detach/attach cycle (as happens on item drop/pickup, trade, store), is still INSERTed.
    /// If EF demotes it to Unchanged on Attach, it will NOT be inserted and is silently lost - which would
    /// mean Layer A (phantom-delete resilience) stops the crash but does not actually save such items.
    /// </summary>
    /// <returns>The task.</returns>
    [Test]
    [Explicit("Integration test - requires a running PostgreSQL database (e.g. the openmu-postgres docker container).")]
    public async Task AddedEntityThatWasDetachedAndReattachedIsStillPersistedAsync()
    {
        var contextProvider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        var name = "n" + Guid.NewGuid().ToString("N")[..9];
        try
        {
            using var context = contextProvider.CreateNewContext();

            var account = context.CreateNew<Account>(); // Added, with a real GUID already assigned.
            account.LoginName = name;
            context.Detach(account); // e.g. item dropped.
            context.Attach(account); // e.g. item picked up / traded / restored.

            var success = await context.SaveChangesAsync().ConfigureAwait(false);
            Assert.That(success, Is.True);

            using var verifyContext = contextProvider.CreateNewContext();
            var accounts = await verifyContext.GetAsync<Account>().ConfigureAwait(false);
            Assert.That(
                accounts.Any(a => a.LoginName == name),
                Is.True,
                "A created-then-detached-then-reattached entity must still be persisted (inserted).");
        }
        finally
        {
            using var cleanupContext = contextProvider.CreateNewContext();
            var toDelete = (await cleanupContext.GetAsync<Account>().ConfigureAwait(false))
                .FirstOrDefault(a => a.LoginName == name);
            if (toDelete is not null)
            {
                await cleanupContext.DeleteAsync(toDelete).ConfigureAwait(false);
                await cleanupContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}
