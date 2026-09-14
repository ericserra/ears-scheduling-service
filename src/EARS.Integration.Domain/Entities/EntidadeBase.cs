namespace EARS.Integration.Domain.Entities;

public abstract class EntidadeBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CriadoEm { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; protected set; } = DateTimeOffset.UtcNow;

    protected void Tocar() => AtualizadoEm = DateTimeOffset.UtcNow;
}
