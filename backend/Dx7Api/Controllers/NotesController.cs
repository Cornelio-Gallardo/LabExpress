using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/notes")]
public class NotesController : TenantBaseController
{
    private readonly AppDbContext _db;
    public NotesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetBySession([FromQuery] Guid sessionId)
    {
        // Verify session belongs to this tenant+client
        var session = await _db.Sessions.FirstOrDefaultAsync(s =>
            s.Id == sessionId && s.TenantId == TenantId &&
            (!ClientId.HasValue || s.ClientId == ClientId.Value));
        if (session == null) return NotFound();

        var notes = await _db.MdNotes
            .Include(n => n.MdUser)
            .Where(n => n.SessionId == sessionId && n.TenantId == TenantId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return Ok(notes.Select(n => new MdNoteDto(
            n.Id, n.SessionId, n.NoteText,
            n.MdUser.Name, n.CreatedAt, n.UpdatedAt,
            CanEditNote(n)
        )));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNoteRequest req)
    {
        if (!IsMd) return Forbid();

        // Verify session belongs to this tenant+client
        var session = await _db.Sessions.FirstOrDefaultAsync(s =>
            s.Id == req.SessionId && s.TenantId == TenantId &&
            (!ClientId.HasValue || s.ClientId == ClientId.Value));
        if (session == null) return NotFound();

        var note = new MdNote
        {
            TenantId = TenantId,
            SessionId = req.SessionId,
            MdUserId = CurrentUserId,
            NoteText = req.NoteText,
        };

        _db.MdNotes.Add(note);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetBySession), new { sessionId = req.SessionId }, note.Id);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNoteRequest req)
    {
        if (!IsMd) return Forbid();

        var note = await _db.MdNotes.FirstOrDefaultAsync(n =>
            n.Id == id && n.TenantId == TenantId);
        if (note == null) return NotFound();
        if (note.MdUserId != CurrentUserId) return Forbid();
        if (!CanEditNote(note)) return BadRequest(new { message = "Note can only be edited within 24 hours of creation" });

        note.NoteText = req.NoteText;
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private bool CanEditNote(MdNote note) =>
        note.MdUserId == CurrentUserId &&
        DateTime.UtcNow - note.CreatedAt < TimeSpan.FromHours(24);
}
