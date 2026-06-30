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

    /// <summary>
    /// Reproduces the exact logout data-loss path: an entity created but never persisted goes through a
    /// detach/attach cycle and is then <em>modified</em>. Before the fix EF tracks it as Unchanged after the
    /// re-attach, so the modification issues a 0-row UPDATE which throws
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> and rolls back the whole save.
    /// After the fix the entity is restored to Added and is simply INSERTed with its current values.
    /// </summary>
    /// <returns>The task.</returns>
    [Test]
    [Explicit("Integration test - requires a running PostgreSQL database (e.g. the openmu-postgres docker container).")]
    public async Task ModifiedAfterReattachIsInsertedNotRolledBackAsync()
    {
        var contextProvider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        var name = "m" + Guid.NewGuid().ToString("N")[..9];
        const string expectedEmail = "after-reattach@example.com";
        try
        {
            using var context = contextProvider.CreateNewContext();

            var account = context.CreateNew<Account>(); // Added, with a real GUID already assigned.
            account.LoginName = name;
            context.Detach(account); // e.g. item dropped / moved to a temporary storage.
            context.Attach(account); // e.g. picked up / traded / restored on logout.

            // Modify after the re-attach: without the fix this entity is Unchanged, so this turns into a
            // 0-row UPDATE that rolls back the whole batch.
            account.EMail = expectedEmail;

            var success = await context.SaveChangesAsync().ConfigureAwait(false);
            Assert.That(success, Is.True);

            using var verifyContext = contextProvider.CreateNewContext();
            var saved = (await verifyContext.GetAsync<Account>().ConfigureAwait(false))
                .Where(a => a.LoginName == name)
                .ToList();
            Assert.That(saved, Has.Count.EqualTo(1), "Exactly one row must be inserted, not zero (lost) nor duplicated.");
            Assert.That(saved[0].EMail, Is.EqualTo(expectedEmail), "The modified value must be persisted.");
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

    /// <summary>
    /// Guards against over-promotion: a row that already exists in the database (loaded, hence Unchanged)
    /// must NOT be turned into an INSERT by the detach/attach handling. It must be UPDATEd in place, leaving
    /// a single row - never duplicated or resurrected. This is the inverse of the phantom case and protects
    /// the fix from re-inserting genuinely persisted entities.
    /// </summary>
    /// <returns>The task.</returns>
    [Test]
    [Explicit("Integration test - requires a running PostgreSQL database (e.g. the openmu-postgres docker container).")]
    public async Task PersistedEntityDetachedAndReattachedIsUpdatedNotDuplicatedAsync()
    {
        var contextProvider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        var name = "e" + Guid.NewGuid().ToString("N")[..9];
        try
        {
            // Arrange: a genuinely persisted account.
            using (var seedContext = contextProvider.CreateNewContext())
            {
                var seed = seedContext.CreateNew<Account>();
                seed.LoginName = name;
                seed.EMail = "before@example.com";
                await seedContext.SaveChangesAsync().ConfigureAwait(false);
            }

            // Act: load it (Unchanged), run it through a detach/attach cycle, then modify and save.
            using var context = contextProvider.CreateNewContext();
            var account = (await context.GetAsync<Account>().ConfigureAwait(false))
                .First(a => a.LoginName == name);
            context.Detach(account);
            context.Attach(account);
            account.EMail = "updated@example.com";
            var success = await context.SaveChangesAsync().ConfigureAwait(false);
            Assert.That(success, Is.True);

            // Assert: still exactly one row (UPDATE, not a duplicate INSERT), with the new value.
            using var verifyContext = contextProvider.CreateNewContext();
            var rows = (await verifyContext.GetAsync<Account>().ConfigureAwait(false))
                .Where(a => a.LoginName == name)
                .ToList();
            Assert.That(rows, Has.Count.EqualTo(1), "A persisted entity must not be duplicated by detach/attach.");
            Assert.That(rows[0].EMail, Is.EqualTo("updated@example.com"));
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
