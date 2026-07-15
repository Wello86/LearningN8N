using AiShoppingAssistant.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pgvector;

namespace AiShoppingAssistant.Infrastructure;

/// <summary>
/// EF Core/Npgsql database context for the AI Shopping Assistant. Entities
/// (defined in <c>AiShoppingAssistant.Core.Entities</c>, per constitution
/// Principle IV) are mapped here via Fluent API only — Core stays free of
/// any EF Core / Infrastructure reference.
/// </summary>
public sealed class AiShoppingAssistantDbContext : DbContext
{
    public AiShoppingAssistantDbContext(DbContextOptions<AiShoppingAssistantDbContext> options)
        : base(options)
    {
    }

    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();

    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ConversationSession>(entity =>
        {
            entity.ToTable("conversation_sessions");
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.CustomerId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<ConversationTurn>(entity =>
        {
            entity.ToTable("conversation_turns");
            entity.HasKey(e => e.TurnId);
            entity.Property(e => e.UserMessage).IsRequired();
            entity.Property(e => e.ToolCallsExecuted).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.RetrievedSimilarityScores).HasColumnType("jsonb");
            entity.Property(e => e.AssistantMessage).IsRequired();
            entity.Property(e => e.EscalateToHuman).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Ordering within a session (data-model.md: "ordered by TurnIndex").
            entity.HasIndex(e => new { e.SessionId, e.TurnIndex });

            entity.HasOne<ConversationSession>()
                .WithMany()
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // data-model.md "Order" — queried via get_order_status (contracts/react-tooling.md).
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(e => e.OrderId);
            entity.Property(e => e.OrderId).IsRequired();
            entity.Property(e => e.CustomerId).IsRequired();
            entity.Property(e => e.ProductName).IsRequired();
            entity.Property(e => e.OrderDate).HasColumnType("date").IsRequired();
            entity.Property(e => e.DeliveryDate).HasColumnType("date");
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.Amount).HasColumnType("numeric(12,2)").IsRequired();

            // FR-010: get_order_status filters by both OrderId and CustomerId
            // in one query (research.md §6) — index supports that lookup.
            entity.HasIndex(e => new { e.OrderId, e.CustomerId });
        });

        // data-model.md "KnowledgeDocument" — searched via
        // search_policy_and_product_docs (contracts/react-tooling.md). The
        // Core entity keeps Embedding as float[] (Principle IV); this
        // conversion to Pgvector.Vector is the Infrastructure-only boundary.
        modelBuilder.Entity<KnowledgeDocument>(entity =>
        {
            entity.ToTable("knowledge_documents");
            entity.HasKey(e => e.DocumentId);
            entity.Property(e => e.DocumentType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Embedding)
                .HasConversion(
                    v => new Vector(v),
                    v => v.ToArray(),
                    new ValueComparer<float[]>(
                        (a, b) => a!.SequenceEqual(b!),
                        a => a.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
                        a => a.ToArray()))
                .HasColumnType("vector(1536)")
                .IsRequired();
        });
    }
}
