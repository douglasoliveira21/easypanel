namespace EasyPanel.WindowsClient;

/// <summary>
/// Garante que apenas uma instância do agente executa por máquina (R2.1), via um
/// mutex nomeado de escopo global. A posse do mutex representa a instância única
/// ativa; uma segunda instância falha ao adquiri-lo e encerra graciosamente.
///
/// <para>É <see cref="IDisposable"/>: liberar a instância libera o mutex,
/// permitindo que uma nova instância assuma (ex.: após atualização/reinício).</para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private bool _hasHandle;

    private SingleInstanceGuard(Mutex? mutex, bool hasHandle)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
    }

    /// <summary>Indica se esta instância detém a posse exclusiva (é a única ativa).</summary>
    public bool IsOwner => _hasHandle;

    /// <summary>
    /// Tenta adquirir a instância única identificada por <paramref name="name"/>.
    /// Retorna um guarda cujo <see cref="IsOwner"/> indica se a aquisição teve
    /// sucesso. Um nome com prefixo <c>Global\</c> torna o mutex visível entre
    /// sessões (serviço + usuário), adequado a um Windows Service.
    /// </summary>
    /// <param name="name">Nome lógico da instância (sem prefixo de escopo).</param>
    public static SingleInstanceGuard Acquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutexName = $"Global\\EasyPanelWindowsClient_{name}";

        // createdNew distingue de forma confiável a primeira instância das demais,
        // tanto entre processos quanto dentro do mesmo processo (ao contrário de
        // WaitOne, que é reentrante para a mesma thread). A primeira instância cria
        // o mutex e o retém até o Dispose; as demais o encontram já existente.
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);

        if (!createdNew)
        {
            mutex.Dispose();
            return new SingleInstanceGuard(null, hasHandle: false);
        }

        return new SingleInstanceGuard(mutex, hasHandle: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (_hasHandle)
        {
            try
            {
                // Libera a posse quando chamado na thread proprietária. Fora dela,
                // o descarte do handle abaixo libera o mutex do SO de qualquer modo.
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Dispose em thread diferente da proprietária: ignorado; o Dispose
                // do handle libera o mutex.
            }

            _hasHandle = false;
        }

        _mutex.Dispose();
    }
}
