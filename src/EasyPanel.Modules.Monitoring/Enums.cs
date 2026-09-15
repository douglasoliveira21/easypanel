namespace EasyPanel.Modules.Monitoring;

/// <summary>Estado de um <see cref="WindowsClient"/> (R3.7/R6). Persistido como int.</summary>
public enum WindowsClientState
{
    /// <summary>Registrado, ainda sem heartbeat confirmado.</summary>
    Registered = 0,

    /// <summary>Ativo: enviando heartbeats dentro do limite.</summary>
    Active = 1,

    /// <summary>Sem heartbeat dentro do limite configurável (R6.4).</summary>
    HeartbeatMissing = 2,

    /// <summary>Desabilitado administrativamente.</summary>
    Disabled = 3,
}

/// <summary>Status de uma <see cref="Printer"/> (R9.2). Persistido como int.</summary>
public enum PrinterStatus
{
    /// <summary>Impressora online e respondendo.</summary>
    Online = 0,

    /// <summary>Impressora offline.</summary>
    Offline = 1,

    /// <summary>Status desconhecido (ainda não coletado).</summary>
    Unknown = 2,

    /// <summary>Desabilitada; coleta suspensa (R10.5).</summary>
    Disabled = 3,

    /// <summary>Sem comunicação recente (falha de coleta).</summary>
    NoCommunication = 4,
}

/// <summary>Origem de um <see cref="PrinterCounter"/> (R11.2). Persistido como int.</summary>
public enum CounterSource
{
    /// <summary>Coletado automaticamente pelo agente.</summary>
    Automatic = 0,

    /// <summary>Informado manualmente por um usuário.</summary>
    Manual = 1,

    /// <summary>Recebido via API/integração.</summary>
    Api = 2,
}

/// <summary>
/// Tipo de contador (R11.3). Os valores fixos cobrem os tipos padrão; tipos
/// adicionais configuráveis são representados por <see cref="Other"/> combinado a
/// um rótulo textual no <see cref="PrinterCounter"/>.
/// </summary>
public enum CounterType
{
    /// <summary>Preto e branco (monocromático).</summary>
    BlackAndWhite = 0,

    /// <summary>Colorido.</summary>
    Color = 1,

    /// <summary>Formato A3.</summary>
    A3 = 2,

    /// <summary>Formato A4.</summary>
    A4 = 3,

    /// <summary>Digitalização (scan).</summary>
    Scan = 4,

    /// <summary>Outro tipo configurável (ver rótulo associado).</summary>
    Other = 99,
}

/// <summary>Tipo de <see cref="PrinterEvent"/> (R6.4/R12.3). Persistido como int.</summary>
public enum PrinterEventType
{
    /// <summary>Mudança de status da impressora.</summary>
    StatusChanged = 0,

    /// <summary>Movimentação/ciclo de vida.</summary>
    Movement = 1,

    /// <summary>Falha de coleta (R12.3).</summary>
    CollectionFailure = 2,

    /// <summary>Heartbeat do agente ausente (R6.4).</summary>
    HeartbeatMissing = 3,

    /// <summary>Nível de suprimento cruzou o limiar configurado (Fase 4).</summary>
    SupplyLow = 4,
}

/// <summary>Operação de ciclo de vida de uma <see cref="Printer"/> (R10.1). Persistido como int.</summary>
public enum MovementOperation
{
    /// <summary>Instalação inicial em um Local.</summary>
    Install = 0,

    /// <summary>Transferência entre Locais.</summary>
    Transfer = 1,

    /// <summary>Recolhimento (retirada de operação).</summary>
    Collect = 2,

    /// <summary>Desabilitação.</summary>
    Disable = 3,

    /// <summary>Reativação.</summary>
    Reactivate = 4,
}

/// <summary>Resultado de uma <see cref="Collection"/> (R12.1). Persistido como int.</summary>
public enum CollectionResult
{
    /// <summary>Coleta bem-sucedida.</summary>
    Success = 0,

    /// <summary>Coleta com falha.</summary>
    Failure = 1,
}
