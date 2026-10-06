using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.ProjectFiles.DTOs;
using Aura.Application.ProjectFiles.Services;
using Aura.Application.Projects.DTOs;
using Aura.Application.Projects.Services;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

internal static class UpdateChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), UserId = userId, Name = "Old", LocalPath = @"C:\Work\App" };
        var file = new ProjectFile { Id = Guid.NewGuid(), ProjectId = project.Id,
            RelativePath = "src/a.txt", FileName = "a.txt", Extension = ".txt" };
        var context = DispatchProxy.Create<IAuraDbContext, UpdateContextProxy>();
        var proxy = (UpdateContextProxy)(object)context;
        proxy.Projects = new CheckDbSet<Project>([project]);
        proxy.Files = new CheckDbSet<ProjectFile>([file]);
        var identity = new CheckIdentity(userId);
        var clock = new CheckClock();
        var service = new ProjectService(context, identity, clock);
        var request = new UpdateProjectRequest(" Updated ", "description", project.LocalPath,
            "https://example.invalid/repo", " main ", " paused ");
        var result = await service.UpdateAsync(project.Id, request);
        check(result is not null && project.LocalPath == @"C:\Work\App" && proxy.Saves == 1,
            "same project LocalPath accepted and retained");
        check(project.Name == "Updated" && project.Description == request.Description &&
            project.RepositoryUrl == request.RepositoryUrl && project.CurrentBranch == "main" &&
            project.Status == "Paused" && project.UpdatedAt == clock.UtcNow,
            "unrelated mutable project fields still update with existing normalization");

        async Task Rejected(string? stored, string? incoming, string label)
        {
            project.LocalPath = stored;
            var previousName = project.Name;
            var saves = proxy.Saves;
            var rejected = false;
            try { await service.UpdateAsync(project.Id, request with { LocalPath = incoming, Name = "Must not mutate" }); }
            catch (AppValidationException ex) { rejected = ex.Message == "Registered project root cannot be changed."; }
            check(rejected && project.LocalPath == stored && project.Name == previousName && proxy.Saves == saves, label);
        }
        await Rejected(@"C:\Work\App", @"C:\Work\Other", "project root A to B rejected before mutation/save");
        await Rejected(@"C:\Work\App", null, "project root value to null rejected");
        await Rejected(null, @"C:\Work\App", "project root null to value rejected");
        await Rejected(@"C:\Work\App", " C:\\Work\\App ", "root whitespace change rejected under verbatim creation policy");
        await Rejected(@"C:\Work\App", "C:/Work/App", "root slash change rejected under verbatim creation policy");
        await Rejected(@"C:\Work\App", @"c:\work\app", "root case change rejected under verbatim creation policy");
        await Rejected(null, "", "null and empty project root identities remain distinct");
        project.LocalPath = null;
        check((await service.UpdateAsync(project.Id, request with { LocalPath = null }))?.LocalPath is null,
            "null to null project root accepted");
        project.LocalPath = " C:/Work/App ";
        check((await service.UpdateAsync(project.Id, request with { LocalPath = project.LocalPath }))?.LocalPath == project.LocalPath,
            "verbatim legacy root formatting retained on identical update");
        project.UserId = Guid.NewGuid();
        var before = proxy.Saves;
        check(await service.UpdateAsync(project.Id, request) is null && proxy.Saves == before,
            "foreign project still returns not found without mutation");
        project.UserId = userId;

        var files = new ProjectFileService(context, identity, clock);
        check((await files.UpdateAsync(project.Id, file.Id, new(" src\\a.txt ", " a.txt ", " .txt ")))
            ?.RelativePath == "src/a.txt", "normalized equivalent file identity accepted");
        foreach (var (update, label) in new[]
        {
            (new UpdateProjectFileRequest("other/a.txt", "a.txt", ".txt"), "file relative path mutation rejected"),
            (new UpdateProjectFileRequest("src/b.txt", "b.txt", ".txt"), "file name mutation rejected"),
            (new UpdateProjectFileRequest("src/a.txt", "a.txt", ""), "file extension mutation rejected")
        })
        {
            var saves = proxy.Saves;
            var rejected = false;
            try { await files.UpdateAsync(project.Id, file.Id, update); }
            catch (AppValidationException) { rejected = true; }
            check(rejected && file.RelativePath == "src/a.txt" && file.FileName == "a.txt" &&
                file.Extension == ".txt" && proxy.Saves == saves, label);
        }
    }
}

// Query-only doubles exercise real service branching/mutation without a database or new packages.
// They do not verify EF SQL translation or database concurrency.
public class UpdateContextProxy : DispatchProxy
{
    internal DbSet<Project> Projects { get; set; } = null!;
    internal DbSet<ProjectFile> Files { get; set; } = null!;
    internal DbSet<ProjectMemory> Memories { get; set; } = null!;
    internal DbSet<ProjectSession> Sessions { get; set; } = null!;
    internal int Saves { get; private set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Projects" => Projects,
        "get_ProjectFiles" => Files,
        "get_ProjectMemories" => Memories,
        "get_ProjectSessions" => Sessions,
        "SaveChangesAsync" => Save(),
        _ => throw new InvalidOperationException("Unexpected context operation: " + method?.Name)
    };
    private Task<int> Save() { Saves++; return Task.FromResult(1); }
}

internal sealed class CheckIdentity(Guid id) : IUserIdentityService
{
    public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(id);
}
internal sealed class CheckClock : IDateTimeProvider
{
    public DateTime UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
internal sealed class CheckDbSet<T>(IEnumerable<T> items) : DbSet<T>, IQueryable<T> where T : class
{
    public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Add(T entity)
    { ((ICollection<T>)items).Add(entity); return null!; }
    public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Remove(T entity)
    { ((ICollection<T>)items).Remove(entity); return null!; }
    public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType => throw new NotSupportedException();
    private readonly IQueryable<T> query = items.AsQueryable();
    Type IQueryable.ElementType => typeof(T);
    Expression IQueryable.Expression => query.Expression;
    IQueryProvider IQueryable.Provider => new CheckQueryProvider(query.Provider);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => query.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => query.GetEnumerator();
}
internal sealed class CheckQuery<T>(Expression expression, IQueryProvider inner) : IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public Type ElementType => typeof(T);
    public Expression Expression => expression;
    public IQueryProvider Provider => new CheckQueryProvider(inner);
    public IEnumerator<T> GetEnumerator() => inner.CreateQuery<T>(expression).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new CheckAsyncEnumerator<T>(GetEnumerator(), cancellationToken);
}
internal sealed class CheckAsyncEnumerator<T>(IEnumerator<T> inner, CancellationToken cancellationToken) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(inner.MoveNext()); }
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}
internal sealed class CheckQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) => throw new NotSupportedException();
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new CheckQuery<TElement>(expression, inner);
    public object? Execute(Expression expression) => inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);
    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valueType = typeof(TResult).GetGenericArguments().Single();
        var value = inner.Execute(expression);
        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(valueType).Invoke(null, [value])!;
    }
}
