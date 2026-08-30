namespace BuildingBlocks.EventBus;

/// <summary>
/// Base type for every integration event. Deliberately just data — no
/// behavior, no reference back to any domain entity. The ebook this repo
/// generalizes patterns from is explicit that integration events belong at
/// each microservice's own application layer, not in a shared domain
/// library: sharing one events package across services re-couples them
/// through a common schema for the same reason a shared domain model
/// would. What *is* shared here is this base type and the bus abstraction
/// below — infrastructure, not domain shape.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
