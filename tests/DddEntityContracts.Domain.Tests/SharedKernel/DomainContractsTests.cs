using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class DomainContractsTests
{
    private sealed class TestEntity :
        IDomainCreatable<TestEntity, TestCreateRequest>,
        IDomainUpdatable<TestUpdateRequest>
    {
        private string _name;

        private TestEntity(string name) { _name = name; }

        public string Name => _name;

        public static Result<TestEntity> Create(TestCreateRequest request) =>
            Result<TestEntity>.Success(new TestEntity(request.Name));

        public Result Update(TestUpdateRequest request)
        {
            _name = request.NewName;
            return Result.Success();
        }
    }

    private sealed record TestCreateRequest(string Name);
    private sealed record TestUpdateRequest(string NewName);

    private static Result<TEntity> CreateGeneric<TEntity, TRequest>(TRequest request)
        where TEntity : IDomainCreatable<TEntity, TRequest>
    {
        return TEntity.Create(request);
    }

    [Fact]
    public void DomainContracts_TestEntity_ImplementsStaticAbstractCreate()
    {
        var result = CreateGeneric<TestEntity, TestCreateRequest>(new TestCreateRequest("Alice"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Alice");
    }

    [Fact]
    public void DomainContracts_TestEntity_ImplementsDomainUpdatable()
    {
        var entity = TestEntity.Create(new TestCreateRequest("Alice")).Value;

        var result = entity.Update(new TestUpdateRequest("Bob"));

        result.IsSuccess.Should().BeTrue();
        entity.Name.Should().Be("Bob");
    }
}
