using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using EasyPanel.Shared.Kernel.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Ticketing;

/// <summary>
/// Implementação de <see cref="ITicketAttachmentService"/> (Fase 6 — R5), sobre
/// <see cref="IFileStorage"/>. Valida tamanho/tipo antes de qualquer chamada ao
/// storage (R5.2); só persiste os metadados depois que o objeto foi gravado com
/// sucesso (nenhum anexo órfão).
/// </summary>
public sealed class TicketAttachmentService : ITicketAttachmentService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;
    private readonly IFileStorage _fileStorage;
    private readonly TicketingOptions _options;

    public TicketAttachmentService(
        AppDbContext context,
        ICurrentUserAccessor currentUser,
        IAuditLogger auditLogger,
        TimeProvider clock,
        IFileStorage fileStorage,
        IOptions<TicketingOptions> options)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
        _fileStorage = fileStorage;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<Result<TicketAttachmentDto>> UploadAsync(
        Guid ticketId, UploadTicketAttachmentRequest request, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        var ticket = await _context.Set<Ticket>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            .ConfigureAwait(false);
        if (ticket is null)
        {
            return Result.Failure<TicketAttachmentDto>(TicketingErrors.NotFound);
        }

        if (request.SizeBytes > _options.MaxAttachmentSizeBytes)
        {
            return Result.Failure<TicketAttachmentDto>(TicketingErrors.AttachmentTooLarge);
        }

        if (!_options.AllowedAttachmentContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<TicketAttachmentDto>(TicketingErrors.AttachmentTypeNotAllowed);
        }

        var attachmentId = Guid.NewGuid();
        var tenantId = ticket.TenantId;
        var sanitizedFileName = SanitizeFileName(request.FileName);
        var storageKey = $"tickets/{tenantId:D}/{ticketId:D}/{attachmentId:D}-{sanitizedFileName}";

        var uploadResult = await _fileStorage
            .UploadAsync(storageKey, content, request.ContentType, request.SizeBytes, ct)
            .ConfigureAwait(false);
        if (uploadResult.IsFailure)
        {
            return Result.Failure<TicketAttachmentDto>(TicketingErrors.AttachmentStorageFailure);
        }

        var now = _clock.GetUtcNow();
        var attachment = new TicketAttachment
        {
            Id = attachmentId,
            TicketId = ticketId,
            FileName = sanitizedFileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            StorageKey = storageKey,
            UploadedByUserId = _currentUser.UserId ?? Guid.Empty,
            UploadedAt = now,
            UploadedAtTicks = now.UtcTicks,
            CreatedAt = now,
        };

        _context.Set<TicketAttachment>().Add(attachment);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = tenantId,
                Action = "ticket.attachment_upload",
                ResourceType = nameof(TicketAttachment),
                ResourceId = attachment.Id.ToString(),
                NewValues = $"TicketId={ticketId};FileName={sanitizedFileName};SizeBytes={request.SizeBytes}",
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(attachment));
    }

    /// <inheritdoc />
    public async Task<Result<TicketAttachmentDownload>> DownloadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await _context.Set<TicketAttachment>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == ticketId, ct)
            .ConfigureAwait(false);
        if (attachment is null)
        {
            return Result.Failure<TicketAttachmentDownload>(TicketingErrors.NotFound);
        }

        var downloadResult = await _fileStorage.DownloadAsync(attachment.StorageKey, ct).ConfigureAwait(false);
        if (downloadResult.IsFailure)
        {
            return Result.Failure<TicketAttachmentDownload>(TicketingErrors.AttachmentStorageFailure);
        }

        return Result.Success(new TicketAttachmentDownload(attachment.FileName, attachment.ContentType, downloadResult.Value));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TicketAttachmentDto>>> ListAsync(Guid ticketId, CancellationToken ct)
    {
        var ticketExists = await _context.Set<Ticket>().AsNoTracking().AnyAsync(t => t.Id == ticketId, ct)
            .ConfigureAwait(false);
        if (!ticketExists)
        {
            return Result.Failure<IReadOnlyList<TicketAttachmentDto>>(TicketingErrors.NotFound);
        }

        var attachments = await _context.Set<TicketAttachment>().AsNoTracking()
            .Where(a => a.TicketId == ticketId)
            .OrderByDescending(a => a.UploadedAtTicks)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<TicketAttachmentDto>>(attachments.Select(MapToDto).ToList());
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "arquivo" : fileName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > 200 ? sanitized[..200] : sanitized;
    }

    private static TicketAttachmentDto MapToDto(TicketAttachment a) => new(
        a.Id, a.TicketId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.UploadedAt);
}
