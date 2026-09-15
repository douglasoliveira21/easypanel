namespace EasyPanel.WindowsClient;

/// <summary>
/// Serviço interno do agente supervisionado pelo <see cref="Guardian"/> (R2.2).
///
/// Cada serviço (Rede/Comunicação/Atualização) expõe seu estado de saúde e uma
/// operação de reinício. O Guardian consulta <see cref="IsHealthy"/> periodicamente
/// e chama <see cref="RestartAsync"/> quando um serviço para de responder,
/// registrando o reinício.
/// </summary>
public interface ISupervisedService
{
    /// <summary>Nome do serviço, usado em logs e no registro de reinícios.</summary>
    string Name { get; }

    /// <summary>Indica se o serviço está saudável (ativo e respondendo).</summary>
    bool IsHealthy { get; }

    /// <summary>Reinicia o serviço após uma parada detectada.</summary>
    Task RestartAsync(CancellationToken ct);
}
