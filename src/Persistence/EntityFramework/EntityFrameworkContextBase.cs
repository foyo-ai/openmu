// <copyright file="EntityFrameworkContextBase.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Composition;
using MUnique.OpenMU.DataModel.Configuration;
using Nito.AsyncEx;
using Nito.Disposables;

/// <summary>
/// Abstract base class for an <see cref="IContext"/> which uses an <see cref="DbContext"/>.
/// </summary>
internal class EntityFrameworkContextBase : IContext
{
    private static readonly object PendingInsertMarker = new();

    private readonly bool _isOwner;
    private readonly IConfigurationChangeListener? _changeListener;
    private readonly AsyncLock _lock = new();
    private readonly ILogger _logger;

    /// <summary>
    /// Entities which were created in this context (and therefore assigned a real key) but never
    /// persisted, and have since been detached. EF forgets that such an entity still needs an INSERT,
    /// so a later <see cref="Attach"/> would demote it to <see cref="EntityState.Unchanged"/> - causing
    /// either a silent lost INSERT or, once it is modified, a 0-row UPDATE that throws
    /// <see cref="DbUpdateConcurrencyException"/> and rolls back the whole save (logout data loss).
    /// We remember them here so <see cref="Attach"/> can restore the <see cref="EntityState.Added"/>
    /// state. Weak keys: an entity that is never re-attached is simply collected, no leak.
    /// </summary>
    private readonly ConditionalWeakTable<object, object> _detachedPendingInserts = new();

    private bool _isDisposed;
    private int _notificationSuspensions;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkContextBase" /> class.
    /// </summary>
    /// <param name="context">The db context.</param>
    /// <param name="repositoryProvider">The repository provider.</param>
    /// <param name="isOwner">If set to <c>true</c>, this instance owns the <see cref="Context" />. That means it will be disposed when this instance will be disposed.</param>
    /// <param name="changeListener">The change listener.</param>
    /// <param name="logger">The logger.</param>
    protected EntityFrameworkContextBase(DbContext context, IContextAwareRepositoryProvider repositoryProvider, bool isOwner, IConfigurationChangeListener? changeListener, ILogger logger)
    {
        this.Context = context;
        this.RepositoryProvider = repositoryProvider;
        this._isOwner = isOwner;
        this._changeListener = changeListener;
        this._logger = logger;

        // Ensure that the model is created.
        _ = context.Model;
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="EntityFrameworkContextBase"/> class.
    /// </summary>
    ~EntityFrameworkContextBase() => this.Dispose(false);

    /// <inheritdoc />
    public bool HasChanges => this.Context.ChangeTracker.HasChanges();

    /// <summary>
    /// Gets the entity framework context.
    /// </summary>
    internal DbContext Context { get; }

    /// <summary>
    /// Gets the repository provider.
    /// </summary>
    protected IContextAwareRepositoryProvider RepositoryProvider { get; }

    /// <inheritdoc/>
    public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        using var l = await this._lock.LockAsync();

        // when we have a change publisher attached, we want to get the changed entries before accepting them.
        // Otherwise, we can accept them.
        var acceptChanges = true;

        object? sender = null;
        SavedChangesEventArgs? args = null;
        if (this._changeListener is { })
        {
            this.Context.SavedChanges += OnSavedChanges;
            acceptChanges = false;
        }

        try
        {
            await this.SaveChangesWithPhantomDeleteReconciliationAsync(acceptChanges, cancellationToken).ConfigureAwait(false);

            if (args is not null)
            {
                await this.OnSavedChangesAsync(sender, args).ConfigureAwait(false);
            }
        }
        finally
        {
            this.Context.SavedChanges -= OnSavedChanges;
        }

        return true;

        void OnSavedChanges(object? s, SavedChangesEventArgs e)
        {
            sender = s;
            args = e;
        }
    }

    /// <summary>
    /// Saves the changes, reconciling "phantom" deletes before giving up.
    /// </summary>
    /// <param name="acceptChanges">If set to <c>true</c>, the changes are accepted on success.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A phantom delete can occur when an entity was created (and thus assigned a real key by the
    /// <see cref="GuidV7ValueGenerator"/>) but never persisted, and then got detached and re-attached -
    /// e.g. through item drop/pickup, trade, player store or temporary storage restore. After the
    /// re-attach the entity is tracked as <see cref="EntityState.Unchanged"/> despite not existing in
    /// the database, so removing it later issues a DELETE which affects 0 rows and throws a
    /// <see cref="DbUpdateConcurrencyException"/>, rolling back the whole batch.
    /// Because OpenMU does not use optimistic concurrency tokens, a DELETE affecting 0 rows simply means
    /// the row is already gone - which is the desired end state. We therefore detach such entries and
    /// retry, so the rest of the batch is persisted instead of the entire save being lost.
    /// A 0-row result for any non-deleted entry (e.g. a genuine missing UPDATE target) is a real signal
    /// and is not swallowed.
    /// </remarks>
    private async Task SaveChangesWithPhantomDeleteReconciliationAsync(bool acceptChanges, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await this.Context.SaveChangesAsync(acceptChanges, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts && IsPhantomDelete(ex))
            {
                foreach (var entry in ex.Entries)
                {
                    this._logger.LogWarning(
                        "Reconciling phantom delete of {EntityType} (Id: {Id}), which was not present in the database; detaching and retrying save (attempt {Attempt}/{MaxAttempts}).",
                        entry.Entity.GetType().Name,
                        TryGetPrimaryKeyValue(entry),
                        attempt,
                        maxAttempts);

                    // Detached (not Unchanged): the row really is gone, and the entity is no longer
                    // referenced by its aggregate, so re-marking it Unchanged would just be detected as
                    // an orphan and deleted again on the next attempt, looping forever.
                    entry.State = EntityState.Detached;
                }
            }
            catch (DbUpdateException ex)
            {
                // Diagnostic: this save failure is NOT a clean phantom-delete reconciliation
                // (mixed/non-delete entries, or attempts exhausted), so the whole batch rolls back.
                // Log every affected entry to identify the culprit, then rethrow unchanged so behavior
                // is preserved.
                this.LogFailedSaveEntries(ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Logs the entries which caused a <see cref="DbUpdateException"/> (and therefore a rolled-back
    /// save), including entity type, primary key, tracking state and a best-effort description, so the
    /// offending entity can be identified. Purely diagnostic; does not alter the save outcome.
    /// </summary>
    /// <param name="ex">The update exception.</param>
    private void LogFailedSaveEntries(DbUpdateException ex)
    {
        if (ex.Entries.Count == 0)
        {
            this._logger.LogError(ex, "Save failed and rolled back, but the exception reported no affected entries.");
            return;
        }

        foreach (var entry in ex.Entries)
        {
            string description;
            try
            {
                description = entry.Entity.ToString() ?? entry.Entity.GetType().Name;
            }
            catch (Exception)
            {
                description = entry.Entity.GetType().Name;
            }

            this._logger.LogError(
                "Save rolled back due to {EntityType} (Id: {Id}) tracked as {State}: {Description}",
                entry.Entity.GetType().Name,
                TryGetPrimaryKeyValue(entry),
                entry.State,
                description);
        }
    }

    /// <summary>
    /// Determines whether the given exception was caused exclusively by deletes of rows which do not
    /// exist anymore (or never existed), and can therefore be safely reconciled by detaching them.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns><c>true</c> if every affected entry is in the <see cref="EntityState.Deleted"/> state.</returns>
    private static bool IsPhantomDelete(DbUpdateConcurrencyException ex)
    {
        return ex.Entries.Count > 0 && ex.Entries.All(entry => entry.State == EntityState.Deleted);
    }

    private static object? TryGetPrimaryKeyValue(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        return keyProperty is null ? null : entry.Property(keyProperty.Name).CurrentValue;
    }

    /// <inheritdoc />
    public IDisposable SuspendChangeNotifications()
    {
        Interlocked.Increment(ref this._notificationSuspensions);
        return new Disposable(() => Interlocked.Decrement(ref this._notificationSuspensions));
    }

    /// <inheritdoc />
    public bool Detach(object item)
    {
        using var l = this._lock.Lock();
        return this.DetachInternal(item);
    }

    /// <inheritdoc />
    public void Attach(object item)
    {
        using var l = this._lock.Lock();
        this.Context.Attach(item);
        this.RestorePendingInsertState(item);
    }

    private bool DetachInternal(object item)
    {
        var entry = this.Context.Entry(item);
        if (entry is null)
        {
            return false;
        }

        var previousState = entry.State;
        entry.State = EntityState.Detached;

        if (previousState == EntityState.Added)
        {
            // The entity was created in this context but never persisted. Detaching it makes EF forget
            // it still needs to be INSERTed; remember it so a later Attach can restore the Added state
            // instead of letting EF demote it to Unchanged. See RestorePendingInsertState.
            this._detachedPendingInserts.AddOrUpdate(item, PendingInsertMarker);
        }

        this.ForEachAggregate(item, obj => this.DetachInternal(obj));

        return previousState != EntityState.Added;
    }

    /// <summary>
    /// Restores the <see cref="EntityState.Added"/> state for any entity in the just-attached aggregate
    /// that was created but never persisted before it was detached (see <see cref="_detachedPendingInserts"/>).
    /// <see cref="DbContext.Attach(object)"/> marks every reachable entity as
    /// <see cref="EntityState.Unchanged"/>, which would skip the INSERT for such a phantom (losing the item,
    /// or - if it is later modified - issuing a 0-row UPDATE that rolls back the whole save). A correctly
    /// re-Added entity simply gets INSERTed.
    /// </summary>
    /// <param name="item">The root of the attached aggregate.</param>
    private void RestorePendingInsertState(object item)
    {
        if (this._detachedPendingInserts.Remove(item))
        {
            this.Context.Entry(item).State = EntityState.Added;
        }

        this.ForEachAggregate(item, this.RestorePendingInsertState);
    }

    /// <inheritdoc />
    public T CreateNew<T>(params object?[] args)
        where T : class
    {
        using var l = this._lock.Lock();
        var instance = typeof(CachingEntityFrameworkContext).Assembly.CreateNew<T>(args);
        this.Context.Add(instance);
        return instance;
    }

    /// <inheritdoc />
    public object CreateNew(Type type, params object?[] args)
    {
        using var l = this._lock.Lock();
        var instance = typeof(CachingEntityFrameworkContext).Assembly.CreateNew(type, args);
        this.Context.Add(instance);
        return instance;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeleteAsync<T>(T obj)
        where T : class
    {
        using var l = await this._lock.LockAsync();

        var result = false;
        var entry = this.Context.Entry(obj);
        if (entry.State == EntityState.Detached)
        {
            this.Context.Attach(obj);
            entry = this.Context.Entry(obj);
        }

        switch (entry.State)
        {
            case EntityState.Detached:
                return true;
            case EntityState.Added:
                this.DetachInternal(obj);
                break;
            default:
                this.Context.Remove(obj);
                this.ForEachAggregate(obj, a => this.Context.Remove(a));
                break;
        }

        result = true;

        return result;
    }

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync<T>(Guid id, CancellationToken cancellationToken)
        where T : class
    {
        using var l = await this._lock.LockAsync(cancellationToken);
        using var context = this.RepositoryProvider.ContextStack.UseContext(this);
        return await this.GetRepository<T>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<object?> GetByIdAsync(Guid id, Type type, CancellationToken cancellationToken)
    {
        using var l = await this._lock.LockAsync(cancellationToken).ConfigureAwait(false);
        using var context = this.RepositoryProvider.ContextStack.UseContext(this);
        return await this.GetRepository(type).GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IEnumerable<T>> GetAsync<T>(CancellationToken cancellationToken)
        where T : class
    {
        using var l = await this._lock.LockAsync(cancellationToken).ConfigureAwait(false);
        using var context = this.RepositoryProvider.ContextStack.UseContext(this);
        return await this.GetRepository<T>().GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IEnumerable> GetAsync(Type type, CancellationToken cancellationToken)
    {
        using var l = await this._lock.LockAsync(cancellationToken).ConfigureAwait(false);
        using var context = this.RepositoryProvider.ContextStack.UseContext(this);
        return await this.GetRepository(type).GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool IsSupporting(Type type)
    {
        var currentSearchType = type;
        do
        {
            if (currentSearchType is null)
            {
                break;
            }

            if (this.Context.Model.FindLeastDerivedEntityTypes(currentSearchType).FirstOrDefault() is not null)
            {
                return true;
            }

            if (currentSearchType.Name != currentSearchType.BaseType?.Name)
            {
                break;
            }

            currentSearchType = currentSearchType.BaseType;
        }
        while (currentSearchType != typeof(object));

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!this._isDisposed)
        {
            this.Dispose(true);
        }

        this._isDisposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool dispose)
    {
        if (!dispose || !this._isOwner)
        {
            return;
        }

        this.Context.Dispose();
    }

    private IRepository<T> GetRepository<T>()
        where T : class
    {
        if (this.RepositoryProvider.GetRepository<T>() is { } repository)
        {
            return repository;
        }

        throw new RepositoryNotFoundException(typeof(T));
    }

    private IRepository GetRepository(Type type)
    {
        if (this.RepositoryProvider.GetRepository(type) is { } repository)
        {
            return repository;
        }

        throw new RepositoryNotFoundException(type);
    }

    private void ForEachAggregate(object obj, Action<object> action)
    {
        var aggregateProperties = obj.GetType()
            .GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<MemberOfAggregateAttribute>() is { }
                        || p.Name.StartsWith("Joined"));
        foreach (var propertyInfo in aggregateProperties)
        {
            var propertyValue = propertyInfo.GetMethod?.Invoke(obj, []);
            if (propertyValue is IEnumerable enumerable)
            {
                foreach (var value in enumerable)
                {
                    action(value);
                }
            }
            else if (propertyValue is { })
            {
                action(propertyValue);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Catching all Exceptions.")]
    private async ValueTask OnSavedChangesAsync(object? sender, SavedChangesEventArgs e)
    {
        try
        {
            if (this._changeListener is null || this._notificationSuspensions > 0)
            {
                // should never be the case
                return;
            }

            if (e.EntitiesSavedCount == 0)
            {
                // why are we getting this event then anyway?
                return;
            }

            var changedEntries = this.Context.ChangeTracker.Entries()
                .Where(entity => entity.State != EntityState.Unchanged).ToList();
            foreach (var entry in changedEntries)
            {
                var (parent, parentCollectionNavigation) = this.GetParentInformation(entry);

                switch (entry.State)
                {
                    case EntityState.Added:
                        await this._changeListener.ConfigurationAddedAsync(entry.Metadata.ClrType, entry.Entity.GetId(), entry.Entity, parent, parentCollectionNavigation).ConfigureAwait(false);
                        break;
                    case EntityState.Deleted:
                        await this._changeListener.ConfigurationRemovedAsync(entry.Metadata.ClrType, entry.Entity.GetId(), parent, parentCollectionNavigation).ConfigureAwait(false);
                        break;
                    case EntityState.Modified:
                        await this._changeListener.ConfigurationChangedAsync(entry.Metadata.ClrType, entry.Entity.GetId(), entry.Entity, parent).ConfigureAwait(false);
                        break;
                    default:
                        // no change publishing required.
                        break;
                }

                if (parent is not null && parent is not GameConfiguration && parent is not Guid)
                {
                    await this._changeListener.ConfigurationChangedAsync(parent.GetType(), parent.GetId(), parent, null).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Unexpected error publishing changes.");
        }
        finally
        {
            try
            {
                this.Context.ChangeTracker.AcceptAllChanges();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Unexpected error when accepting all saved changes.");
            }
        }
    }

    private (object? Parent, INavigationBase? ParentCollectionNavigation) GetParentInformation(EntityEntry entry)
    {
        var propertyToParent = entry.Properties
            .FirstOrDefault(p => p.Metadata.IsForeignKey()
                                 && (p.Metadata.IsShadowProperty() || p.Metadata.PropertyInfo?.GetCustomAttribute<IsLinkToParentAttribute>() is not null));

        var parentId = (Guid?)(propertyToParent?.CurrentValue ?? propertyToParent?.OriginalValue);
        if (parentId is null)
        {
            return (null, null);
        }

        object? parent = null;
        INavigationBase? parentCollectionNavigation = null;
        var parentEntry = this.Context.ChangeTracker.Entries().FirstOrDefault(e => e.Entity.GetId() == parentId);
        if (parentEntry is not null && propertyToParent is not null)
        {
            parent = parentEntry.Entity;
            var parentCollection = parentEntry.Collections
                .FirstOrDefault(c => c.Metadata.IsCollection
                                     && (c.Metadata as INavigation)?.ForeignKey == propertyToParent.Metadata.GetContainingForeignKeys().FirstOrDefault());
            parentCollectionNavigation = parentCollection?.Metadata;
        }

        return (parent ?? parentId, parentCollectionNavigation);
    }
}