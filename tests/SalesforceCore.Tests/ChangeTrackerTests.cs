using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Tracking;
using Xunit;

namespace SalesforceCore.Tests;

public class ChangeTrackerTests
{
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;

        [SalesforceField("Name")]
        public string Name { get; set; } = string.Empty;

        [SalesforceField("Amount")]
        public double Amount { get; set; }
    }

    [Fact]
    public void Track_StartsTrackingUnchanged()
    {
        var tracker = new ChangeTracker();
        var entity = new TestEntity { Id = "123", Name = "Old Name", Amount = 100 };

        tracker.Track(entity);

        tracker.GetState(entity).Should().Be(EntityState.Unchanged);
        tracker.HasChanges(entity).Should().BeFalse();
    }

    [Fact]
    public void DetectsChanges_WhenPropertyModified()
    {
        var tracker = new ChangeTracker();
        var entity = new TestEntity { Id = "123", Name = "Old Name", Amount = 100 };

        tracker.Track(entity);
        entity.Name = "New Name";

        tracker.HasChanges(entity).Should().BeTrue();
        tracker.GetState(entity).Should().Be(EntityState.Modified);
        
        var changes = tracker.GetChanges(entity);
        changes.Should().Contain(c => c.FieldName == "Name" && (string?)c.CurrentValue == "New Name" && (string?)c.OriginalValue == "Old Name");
    }

    [Fact]
    public void RevertChanges_RestoresOriginalValues()
    {
        var tracker = new ChangeTracker();
        var entity = new TestEntity { Id = "123", Name = "Original", Amount = 100 };

        tracker.Track(entity);
        entity.Name = "Changed";
        entity.Amount = 200;

        tracker.RevertChanges(entity);

        entity.Name.Should().Be("Original");
        entity.Amount.Should().Be(100);
        tracker.GetState(entity).Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public void AcceptChanges_UpdatesOriginalValues()
    {
        var tracker = new ChangeTracker();
        var entity = new TestEntity { Id = "123", Name = "Original", Amount = 100 };

        tracker.Track(entity);
        entity.Name = "Changed";

        tracker.AcceptChanges(entity);

        tracker.GetState(entity).Should().Be(EntityState.Unchanged);
        tracker.HasChanges(entity).Should().BeFalse();
        
        // Change again to verify new baseline
        entity.Name = "Changed Again";
        var changes = tracker.GetChanges(entity);
        changes.Should().Contain(c => (string?)c.OriginalValue == "Changed");
    }
}
